using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace TsiExtentionsV40.Helpers.Internal
{
    public class PropertyHelper
    {
        private const BindingFlags DeclaredOnlyLookup = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private static ConcurrentDictionary<string, Delegate> _getterCache
            = new ConcurrentDictionary<string, Delegate>();

        private static ConcurrentDictionary<string, Delegate> _setterCache
            = new ConcurrentDictionary<string, Delegate>();

        private static readonly MethodInfo CallInnerDelegateMethod =
            typeof(PropertyHelper).GetMethod(nameof(CallInnerDelegate), BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo CallPropertySetterOpenGenericMethod =
            typeof(PropertyHelper).GetMethod(nameof(CallPropertySetter), DeclaredOnlyLookup);

        public static Func<object, TResult> MakeFastPropertyGetter<TResult>(PropertyInfo property)
            => (Func<object, TResult>)_getterCache.GetOrAdd(BuildPropertyKey(property), key =>
            {
                var getMethod = property.GetGetMethod();
                var declaringClass = property.DeclaringType;
                var typeOfResult = typeof(TResult);

                var getMethodDelegateType = typeof(Func<,>).MakeGenericType(declaringClass, typeOfResult);

                var getMethodDelegate = Delegate.CreateDelegate(declaringClass, getMethod);

                var callInnerGenericMethodWithTypes = CallInnerDelegateMethod
                    .MakeGenericMethod(declaringClass, typeOfResult);

                var result = (Delegate)callInnerGenericMethodWithTypes.Invoke(null, new[] { getMethodDelegate });

                return result;
            });

        public static void SetPropertyValue(object ObjectToSetProperty, string propertyName, object propertyValue)
        {
            var typeOfObjectToSetProperty = ObjectToSetProperty.GetType();

            var propertyInfo = typeOfObjectToSetProperty.GetProperty(propertyName);

            if (propertyInfo == null)
            {
                return;
            }

            var setterDelegate = MakeFastPropertySetter(propertyInfo);
            setterDelegate?.Invoke(ObjectToSetProperty, propertyValue);
        }

        public static Action<object, object> MakeFastPropertySetter(PropertyInfo property)
        {
            var key = BuildPropertyKey(property);
            if (_setterCache.ContainsKey(key))
            {
                return (Action<object, object>)_setterCache[key];
            }

            try
            {
                var setMethod = property.GetSetMethod();

                if (setMethod == null)
                {
                    return null;
                }

                var declaringType = property.DeclaringType;

                var parameters = setMethod.GetParameters();
                var typeInput = setMethod.DeclaringType;
                var parameterType = parameters[0].ParameterType;


                var propertySetterAsAction =
                    Delegate.CreateDelegate(typeof(Action<,>).MakeGenericType(declaringType, parameterType),
                        setMethod);

                var callPropertySetterClosedGenericMethod =
                    CallPropertySetterOpenGenericMethod.MakeGenericMethod(typeInput, parameterType);

                var callPropertySetterDelegate =
                    Delegate.CreateDelegate(typeof(Action<object, object>), propertySetterAsAction,
                        callPropertySetterClosedGenericMethod);

                var result = (Action<object, object>)callPropertySetterDelegate;

                _setterCache.TryAdd(key, result);

                return result;

            }
            catch (Exception ex)
            {
                var message = ex.Message;
            }

            return null;
        }


        private static Func<object, TResult> CallInnerDelegate<TClass, TResult>(
            Func<TClass, TResult> deleg)
            => instance => deleg((TClass)instance);

        private static void CallPropertySetter<TTarget, TValue>(
            Action<TTarget, TValue> setter,
            object target,
            object value)
        {
            if (value == null)
            {
                setter((TTarget)target, default(TValue));
            }
            else
            {
                setter((TTarget)target, (TValue)value);
            }
        }

        private static string BuildPropertyKey(PropertyInfo property)
            => $"{property?.DeclaringType?.FullName}|{property?.Name}";
    }
}
