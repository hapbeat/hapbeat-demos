// Compile-check only. See stubs/README.md.
using System;
using System.Reflection;

namespace HarmonyLib
{
    /// <summary>Stub of <c>HarmonyLib.Harmony</c> — only manual patching is used.</summary>
    public class Harmony
    {
        public Harmony(string id) { }

        public MethodInfo Patch(MethodBase original,
            HarmonyMethod prefix = null,
            HarmonyMethod postfix = null,
            HarmonyMethod transpiler = null,
            HarmonyMethod finalizer = null)
        {
            return null;
        }
    }

    /// <summary>Stub of <c>HarmonyLib.HarmonyMethod</c>.</summary>
    public class HarmonyMethod
    {
        public HarmonyMethod(MethodInfo method) { }
    }

    /// <summary>Stub of <c>HarmonyLib.AccessTools</c> — the two lookups this mod uses.</summary>
    public static class AccessTools
    {
        public static Type TypeByName(string name) { return null; }

        public static MethodInfo Method(Type type, string name,
            Type[] parameters = null, Type[] generics = null)
        {
            return null;
        }
    }
}
