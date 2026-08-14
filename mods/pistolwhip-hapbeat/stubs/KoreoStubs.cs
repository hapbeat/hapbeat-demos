// Compile-check only. See stubs/README.md.
using System;

namespace Il2CppInterop.Runtime
{
    /// <summary>Stub of <c>Il2CppInterop.Runtime.DelegateSupport</c>.</summary>
    public static class DelegateSupport
    {
        /// <summary>Wrap a managed delegate so il2cpp code can call it.</summary>
        public static TIl2Cpp ConvertDelegate<TIl2Cpp>(Delegate managed) where TIl2Cpp : class
        {
            return null;
        }
    }
}

// The namespace follows the same switch BeatSync.cs uses, so compile-check exercises
// whichever shape the build targets. MelonLoader 0.7.x generates the Il2Cpp-prefixed
// one for Pistol Whip (verified on a real install); older/other setups may emit the
// bare name, which is what -p:KoreoIl2CppNamespace=false selects.
#if HAPBEAT_KOREO_IL2CPP_NS
namespace Il2CppSonicBloom.Koreo
#else
namespace SonicBloom.Koreo
#endif
{
    /// <summary>Stub of Koreographer's timed event callback.</summary>
    public delegate void KoreographyEventCallbackWithTime(KoreographyEvent koreoEvent,
        int sampleTime, int sampleDelta, DeltaSlice deltaSlice);

    /// <summary>Stub of a single Koreography event. Its members are read reflectively,
    /// so nothing beyond the type identity is needed here.</summary>
    public class KoreographyEvent { }

    /// <summary>Stub of Koreographer's per-frame timing slice (callback parameter only).</summary>
    public class DeltaSlice { }

    /// <summary>Stub of a loaded Koreography asset. Its track list is read reflectively.</summary>
    public class Koreography { }

    /// <summary>Stub of the Koreographer singleton.</summary>
    public class Koreographer
    {
        public static Koreographer Instance { get { return null; } }

        public void LoadKoreography(Koreography koreography) { }

        public void RegisterForEventsWithTime(string eventId, KoreographyEventCallbackWithTime callback) { }

        public void UnregisterForEvents(string eventId, KoreographyEventCallbackWithTime callback) { }
    }
}
