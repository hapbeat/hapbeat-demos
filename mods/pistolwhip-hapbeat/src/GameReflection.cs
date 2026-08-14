using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Hapbeat.PistolWhip
{
    /// <summary>
    /// Reads members off game objects without compiling against the game's types.
    /// <para>
    /// The hook targets were established from third-party mods and are not verified
    /// against the current Steam build (see README "実機確認チェックリスト"). Binding to
    /// them by name means a renamed or retyped field degrades into one skipped haptic
    /// event with a log line, instead of a compile error for the person building the mod
    /// or a hard crash inside an IL2CPP postfix.
    /// </para>
    /// Lookups are cached per (declaring type, member name); an absent member is cached
    /// too, so a miss on a per-frame hook costs a dictionary probe.
    /// </summary>
    internal static class GameReflection
    {
        private const BindingFlags Instance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Nested rather than keyed by "type|name": the flat form had to build that key
        // string on every lookup, which allocates. One of these lookups sits on a
        // per-frame hook at VR refresh rates, where a per-call allocation turns into GC
        // pressure and visible hitching.
        private static readonly Dictionary<Type, Dictionary<string, MemberInfo>> _cache =
            new Dictionary<Type, Dictionary<string, MemberInfo>>();
        private static readonly object _cacheLock = new object();

        /// <summary>Value of the field or property <paramref name="name"/> on
        /// <paramref name="owner"/>, or null when absent or unreadable.</summary>
        public static object GetMember(object owner, string name)
        {
            if (owner == null || string.IsNullOrEmpty(name))
                return null;

            MemberInfo member = FindMember(owner.GetType(), name);
            if (member == null)
                return null;

            try
            {
                var field = member as FieldInfo;
                if (field != null)
                    return field.GetValue(owner);

                var property = member as PropertyInfo;
                if (property != null && property.CanRead)
                    return property.GetValue(owner, null);
            }
            catch (Exception)
            {
                // A proxy property can throw when the underlying il2cpp object is gone.
                // Callers treat null as "unknown" and fail open.
            }
            return null;
        }

        /// <summary>Bool member value, or <paramref name="fallback"/> when absent.</summary>
        public static bool GetBool(object owner, string name, bool fallback)
        {
            object value = GetMember(owner, name);
            return value is bool ? (bool)value : fallback;
        }

        /// <summary>Int member value, or <paramref name="fallback"/> when absent.</summary>
        public static int GetInt(object owner, string name, int fallback)
        {
            object value = GetMember(owner, name);
            if (value is int) return (int)value;
            if (value is short) return (short)value;
            if (value is byte) return (byte)value;
            if (value is long) return (int)(long)value;
            if (value is float) return (int)(float)value;
            return fallback;
        }

        /// <summary>
        /// The UnityEngine <c>name</c> of a member (e.g. the <c>hand</c> of a gun), used
        /// as a stable per-hand key. Empty string when it cannot be resolved — callers
        /// use that as a single shared bucket rather than dropping the event.
        /// </summary>
        public static string GetChildName(object owner, string memberName)
        {
            object child = GetMember(owner, memberName);
            if (child == null)
                return string.Empty;
            object name = GetMember(child, "name");
            return name != null ? name.ToString() : string.Empty;
        }

        /// <summary>Invoke a no-argument method by name. Returns null when absent or on error.</summary>
        public static object CallMethod(object owner, string name)
        {
            if (owner == null)
                return null;
            try
            {
                MethodInfo method = FindMethod(owner.GetType(), name, 0);
                return method != null ? method.Invoke(owner, null) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Enumerate a list-like object. Handles both a managed <see cref="IEnumerable"/>
        /// and an il2cpp list proxy (which may only expose <c>Count</c> + an indexer),
        /// so callers do not have to know which one the generated assembly produced.
        /// </summary>
        public static IEnumerable<object> EnumerateList(object list)
        {
            if (list == null)
                yield break;

            MethodInfo item = FindMethod(list.GetType(), "get_Item", 1);
            if (item != null)
            {
                int count = GetInt(list, "Count", -1);
                if (count >= 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        object element = null;
                        try { element = item.Invoke(list, new object[] { i }); }
                        catch (Exception) { continue; }
                        if (element != null)
                            yield return element;
                    }
                    yield break;
                }
            }

            var enumerable = list as IEnumerable;
            if (enumerable == null)
                yield break;
            foreach (object element in enumerable)
            {
                if (element != null)
                    yield return element;
            }
        }

        private static MemberInfo FindMember(Type type, string name)
        {
            Dictionary<string, MemberInfo> byName;
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(type, out byName))
                {
                    MemberInfo cached;
                    if (byName.TryGetValue(name, out cached))
                        return cached;
                }
            }

            MemberInfo found = null;
            for (Type t = type; t != null && found == null; t = t.BaseType)
            {
                found = (MemberInfo)t.GetField(name, Instance) ?? t.GetProperty(name, Instance);
                if (found == null)
                {
                    // il2cpp proxies sometimes differ in case from the original field.
                    found = (MemberInfo)t.GetField(name, Instance | BindingFlags.IgnoreCase)
                            ?? t.GetProperty(name, Instance | BindingFlags.IgnoreCase);
                }
            }

            lock (_cacheLock)
            {
                if (!_cache.TryGetValue(type, out byName))
                {
                    byName = new Dictionary<string, MemberInfo>(StringComparer.Ordinal);
                    _cache[type] = byName;
                }
                byName[name] = found; // null is cached deliberately: a miss must stay cheap
            }
            return found;
        }

        private static MethodInfo FindMethod(Type type, string name, int parameterCount)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                MethodInfo[] methods = t.GetMethods(Instance);
                for (int i = 0; i < methods.Length; i++)
                {
                    if (methods[i].Name == name && methods[i].GetParameters().Length == parameterCount)
                        return methods[i];
                }
            }
            return null;
        }
    }
}
