// Recommendation A (docs/INTEGRATION_0322_FOLLOWUPS.md): binding an optional mod's public API by its exact shape, with no
// game types so it is unit-tested on synthetic assemblies (tests/CompanionAI.Tests/OptionalApiBindingTests.cs).
// ModdedDamageReduction passes the game and TabletopTweaks-Core types it expects.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace CompanionAI_Pathfinder.Tactics
{
    public static class OptionalApiBinding
    {
        /// <summary>One public instance method the optional type must have, with exactly these types.</summary>
        public sealed class MethodShape
        {
            public readonly string Name;
            public readonly Type Result;
            public readonly Type[] Arguments;

            public MethodShape(string name, Type result, params Type[] arguments)
            {
                Name = name;
                Result = result;
                Arguments = arguments;
            }
        }

        /// <summary>
        /// The optional type and its methods, in the order of <paramref name="methods"/>. Null (type null) when the optional
        /// assembly is not loaded: absent is normal, not a warning. Throws <see cref="MissingMemberException"/> when the
        /// assembly is loaded but its API differs (the caller logs once and keeps its own fallback).
        /// </summary>
        public static MethodInfo[] Find(IEnumerable<Assembly> loaded, string assemblyName, string typeName, Type requiredBase,
            out Type type, params MethodShape[] methods)
        {
            type = null;
            var assembly = loaded?.FirstOrDefault(a => a.GetName().Name == assemblyName);
            if (assembly == null) return null;
            var found = assembly.GetType(typeName, false);
            if (found == null || !found.IsPublic || found.IsAbstract || !requiredBase.IsAssignableFrom(found))
                throw new MissingMemberException("expected public " + typeName + " type");
            var result = methods.Select(m => Exact(found, m.Name, m.Result, m.Arguments)).ToArray();
            type = found;
            return result;
        }

        public static MethodInfo Exact(Type type, string name, Type result, params Type[] arguments)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, arguments, null);
            // The default binder also accepts widened primitives (a double parameter for a float): exact means equal types.
            if (method == null || method.ReturnType != result || method.IsGenericMethod ||
                !method.GetParameters().Select(p => p.ParameterType).SequenceEqual(arguments))
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        // Compiled once at binding: a call on an instance held as object, with no per-call reflection.

        public static Action<object> Action(Type type, MethodInfo method)
        {
            var instance = Expression.Parameter(typeof(object), "instance");
            return Expression.Lambda<Action<object>>(Expression.Call(Expression.Convert(instance, type), method), instance).Compile();
        }

        public static Func<object, T1, TResult> Func<T1, TResult>(Type type, MethodInfo method)
        {
            var instance = Expression.Parameter(typeof(object), "instance");
            var a1 = Expression.Parameter(typeof(T1), "a1");
            return Expression.Lambda<Func<object, T1, TResult>>(
                Expression.Call(Expression.Convert(instance, type), method, a1), instance, a1).Compile();
        }

        public static Func<object, T1, T2, T3, TResult> Func<T1, T2, T3, TResult>(Type type, MethodInfo method)
        {
            var instance = Expression.Parameter(typeof(object), "instance");
            var a1 = Expression.Parameter(typeof(T1), "a1");
            var a2 = Expression.Parameter(typeof(T2), "a2");
            var a3 = Expression.Parameter(typeof(T3), "a3");
            return Expression.Lambda<Func<object, T1, T2, T3, TResult>>(
                Expression.Call(Expression.Convert(instance, type), method, a1, a2, a3), instance, a1, a2, a3).Compile();
        }
    }
}
