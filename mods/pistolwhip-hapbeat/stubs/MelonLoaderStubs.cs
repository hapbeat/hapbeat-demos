// Compile-check only. See stubs/README.md.
using System;

namespace MelonLoader
{
    /// <summary>Stub of <c>MelonLoader.MelonMod</c> — only the members this mod overrides.</summary>
    public abstract class MelonMod
    {
        /// <summary>The Harmony instance MelonLoader gives each melon.</summary>
        public HarmonyLib.Harmony HarmonyInstance { get { return new HarmonyLib.Harmony("stub"); } }

        public virtual void OnInitializeMelon() { }
        public virtual void OnDeinitializeMelon() { }
        public virtual void OnApplicationQuit() { }
    }

    [AttributeUsage(AttributeTargets.Assembly)]
    public class MelonInfoAttribute : Attribute
    {
        public MelonInfoAttribute(Type type, string name, string version, string author) { }
    }

    [AttributeUsage(AttributeTargets.Assembly)]
    public class MelonGameAttribute : Attribute
    {
        public MelonGameAttribute(string developer, string gameName) { }
    }

    public static class MelonLogger
    {
        public static void Msg(string text) { }
        public static void Warning(string text) { }
        public static void Error(string text) { }
    }
}
