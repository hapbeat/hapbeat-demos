#if !HAPBEAT_NO_BEATSYNC
#if HAPBEAT_KOREO_IL2CPP_NS
using Koreo = Il2CppSonicBloom.Koreo;
#else
using Koreo = SonicBloom.Koreo;
#endif
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime;

namespace Hapbeat.PistolWhip
{
    /// <summary>
    /// Fires a haptic pulse on the music beat.
    /// <para>
    /// Pistol Whip drives its music with Sonic Bloom's Koreographer. Koreography event
    /// ids are strings chosen per song by the audio designer, so there is no id to
    /// hard-code: when a song loads, its ids are discovered from the loaded Koreography
    /// asset and a callback is registered for each. Discovered ids are logged so a user
    /// can narrow the set down via <c>beatEventIds</c> in the settings file.
    /// </para>
    /// <para>
    /// This is the one part of the mod that needs the game's types at compile time (the
    /// callback delegate has to be the game's own delegate type). If the build cannot see
    /// <c>SonicBloom.Koreo</c>, build with <c>-p:KoreoIl2CppNamespace=true</c> or, as a
    /// last resort, <c>-p:DisableBeatSync=true</c> — everything else in the mod is
    /// independent of it.
    /// </para>
    /// </summary>
    internal static class BeatSync
    {
        private static readonly Dictionary<string, Koreo.KoreographyEventCallbackWithTime> _registered =
            new Dictionary<string, Koreo.KoreographyEventCallbackWithTime>(StringComparer.Ordinal);
        private static readonly object _lock = new object();

        private static Koreo.KoreographyEventCallbackWithTime _callback;
        private static bool _conversionFailed;

        public static void Install(HarmonyLib.Harmony harmony)
        {
            if (!PistolWhipHapbeatMod.Settings.BeatEnabled)
            {
                PistolWhipHapbeatMod.LogInfo("Beat sync disabled in settings (beatEnabled=false).");
                return;
            }

            try
            {
                MethodInfo target = AccessTools.Method(typeof(Koreo.Koreographer), "LoadKoreography");
                if (target == null)
                {
                    PistolWhipHapbeatMod.LogWarning(
                        "Beat sync unavailable: Koreographer.LoadKoreography not found.");
                    return;
                }

                MethodInfo postfix = AccessTools.Method(typeof(BeatSync), "AfterLoadKoreography");
                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                PistolWhipHapbeatMod.LogInfo("Beat sync armed (waiting for a song to load).");
            }
            catch (Exception ex)
            {
                PistolWhipHapbeatMod.LogWarning("Beat sync could not be installed: " + ex.Message);
            }
        }

        /// <summary>Postfix on <c>Koreographer.LoadKoreography(Koreography)</c>.</summary>
        public static void AfterLoadKoreography(object[] __args)
        {
            try
            {
                object koreography = (__args != null && __args.Length > 0) ? __args[0] : null;
                if (koreography == null)
                    return;

                List<string> discovered = DiscoverEventIds(koreography);
                if (PistolWhipHapbeatMod.Settings.LogDiscoveredBeatIds)
                {
                    // The game preloads every song's Koreography at startup, so this
                    // runs ~100 times with the same handful of ids. Log only when the
                    // set actually changes, otherwise the useful lines are buried.
                    string signature = string.Join(", ", discovered.ToArray());
                    if (signature != _lastLoggedIds)
                    {
                        _lastLoggedIds = signature;
                        PistolWhipHapbeatMod.LogInfo(discovered.Count == 0
                            ? "Song loaded but no Koreography event id could be read from it."
                            : "Song loaded. Koreography event ids: " + signature);
                    }
                }

                List<string> wanted = SelectIds(discovered);
                Register(wanted);
            }
            catch (Exception ex)
            {
                PistolWhipHapbeatMod.HookFailed("Koreographer.LoadKoreography", ex);
            }
        }

        /// <summary>
        /// The ids to listen on: the user's explicit list when set (even if the song did
        /// not advertise them — a song may load its tracks after this point), otherwise
        /// the beat-looking subset of what the song advertises.
        /// <para>
        /// Not every Koreography track is a beat track. Pistol Whip's songs ship
        /// <c>Beat</c> alongside <c>NoBeat</c>, <c>Event</c> and <c>GameplayProp</c>
        /// (observed on a real install); registering for all of them fires haptics at
        /// moments that have nothing to do with the pulse. Falling back to everything
        /// when nothing looks like a beat keeps a differently-named song working, and
        /// the warning tells the user how to pin it down.
        /// </para>
        /// </summary>
        private static List<string> SelectIds(List<string> discovered)
        {
            List<string> configured = PistolWhipHapbeatMod.Settings.BeatEventIds;
            if (configured.Count > 0)
                return new List<string>(configured);

            var beats = new List<string>();
            foreach (string id in discovered)
            {
                if (LooksLikeBeat(id))
                    beats.Add(id);
            }

            if (beats.Count > 0)
                return beats;

            if (discovered.Count > 0)
                WarnNoBeatTrack(discovered);
            return discovered;
        }

        /// <summary>
        /// Whether a Koreography event id names a beat track. Matches ids containing
        /// "beat" while rejecting negations such as <c>NoBeat</c> / <c>OffBeat</c>.
        /// </summary>
        private static bool LooksLikeBeat(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;

            int idx = id.IndexOf("beat", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return false;

            string prefix = id.Substring(0, idx);
            return prefix.IndexOf("no", StringComparison.OrdinalIgnoreCase) < 0
                && prefix.IndexOf("off", StringComparison.OrdinalIgnoreCase) < 0
                && prefix.IndexOf("anti", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static bool _warnedNoBeatTrack;

        private static void WarnNoBeatTrack(List<string> discovered)
        {
            if (_warnedNoBeatTrack)
                return;
            _warnedNoBeatTrack = true;

            PistolWhipHapbeatMod.LogWarning(
                "No Koreography track looks like a beat track (found: " +
                string.Join(", ", discovered.ToArray()) + "), so every id is used. " +
                "Haptics may fire off-beat. Put the right id(s) in \"beatEventIds\" in " +
                "hapbeat_settings.json to pin it down.");
        }

        private static void Register(List<string> ids)
        {
            Koreo.Koreographer instance = Koreo.Koreographer.Instance;
            if (instance == null)
            {
                PistolWhipHapbeatMod.LogWarning("Koreographer.Instance is null; no beat callback registered.");
                return;
            }

            Koreo.KoreographyEventCallbackWithTime callback = GetCallback();
            if (callback == null)
                return;

            lock (_lock)
            {
                // Drop the previous song's registrations. Per-id unregistration is used
                // rather than UnregisterForAllEvents() because that instance is the
                // game's own: clearing it wholesale would take the game's music-driven
                // gameplay and visuals down with it.
                Unregister(instance);

                foreach (string id in ids)
                {
                    if (string.IsNullOrEmpty(id) || _registered.ContainsKey(id))
                        continue;
                    try
                    {
                        instance.RegisterForEventsWithTime(id, callback);
                        _registered[id] = callback;
                    }
                    catch (Exception ex)
                    {
                        PistolWhipHapbeatMod.LogWarning(
                            "Could not register for Koreography event '" + id + "': " + ex.Message);
                    }
                }

                // Same reason as the discovery log above: only report a change.
                string listening = string.Join(", ", new List<string>(_registered.Keys).ToArray());
                if (listening != _lastLoggedRegistration)
                {
                    _lastLoggedRegistration = listening;
                    PistolWhipHapbeatMod.LogInfo(_registered.Count == 0
                        ? "Beat sync registered for no event id."
                        : "Beat sync listening on: " + listening);
                }
            }
        }

        /// <summary>Last logged discovery / registration, so the ~100 preload passes do
        /// not each write a line. Guarded by <see cref="_lock"/> for the registration
        /// one; the discovery one is only touched from the patched method.</summary>
        private static string _lastLoggedIds;
        private static string _lastLoggedRegistration;

        /// <summary>The single il2cpp-side delegate every id is registered with. Built
        /// once: unregistration has to pass the same delegate instance back.</summary>
        private static Koreo.KoreographyEventCallbackWithTime GetCallback()
        {
            if (_callback != null || _conversionFailed)
                return _callback;

            try
            {
                var managed = new Action<Koreo.KoreographyEvent, int, int, Koreo.DeltaSlice>(OnKoreographyEvent);
                _callback = DelegateSupport.ConvertDelegate<Koreo.KoreographyEventCallbackWithTime>(managed);
            }
            catch (Exception ex)
            {
                _conversionFailed = true;
                PistolWhipHapbeatMod.LogWarning(
                    "Beat sync disabled: could not convert the callback delegate (" + ex.Message + ").");
            }
            return _callback;
        }

        private static void OnKoreographyEvent(Koreo.KoreographyEvent koreoEvent, int sampleTime,
            int sampleDelta, Koreo.DeltaSlice deltaSlice)
        {
            try
            {
                // Span events re-fire every frame for the whole span; only the one-shot
                // markers are beats.
                if (!IsOneOff(koreoEvent))
                    return;
                PistolWhipHapbeatMod.Fire(LogicalEvents.Beat);
            }
            catch (Exception ex)
            {
                PistolWhipHapbeatMod.HookFailed("Koreography event callback", ex);
            }
        }

        /// <summary>
        /// True when this event is a one-shot marker rather than a span.
        /// <para>
        /// VERIFY (実機確認チェックリスト §4): the member names probed here come from
        /// Koreographer's documented event model (one-off vs span), not from an inspection
        /// of this game's own generated proxy — the sample-position member could be named
        /// differently in the shipping build. Both the accessor name and the field-style
        /// <c>m</c>-prefixed spelling seen elsewhere in this asset are tried.
        /// </para>
        /// <para>
        /// When none of the probes answers, this returns <c>false</c> (no beat) on purpose.
        /// A span event re-fires every frame for its whole duration, so guessing "one-off"
        /// on an unreadable event would buzz continuously for seconds — the rate limit only
        /// caps that at ~16 pulses/s, it does not stop it. Failing to silence instead is
        /// quiet, and the warning below names the symptom to look for.
        /// </para>
        /// </summary>
        private static bool IsOneOff(object koreoEvent)
        {
            object result = GameReflection.CallMethod(koreoEvent, "IsOneOff");
            if (result is bool)
                return (bool)result;

            // Definition of a one-off event: zero-length sample span.
            for (int i = 0; i < ShapeMembers.Length; i += 2)
            {
                int start = GameReflection.GetInt(koreoEvent, ShapeMembers[i], -1);
                int end = GameReflection.GetInt(koreoEvent, ShapeMembers[i + 1], -1);
                if (start >= 0 && end >= 0)
                    return start == end;
            }

            WarnUnknownShape(koreoEvent);
            return false;
        }

        /// <summary>Candidate (start, end) sample member names, most likely first.</summary>
        private static readonly string[] ShapeMembers =
        {
            "StartSample", "EndSample",
            "mStartSample", "mEndSample",
        };

        private static bool _warnedUnknownShape;

        private static void WarnUnknownShape(object koreoEvent)
        {
            if (_warnedUnknownShape)
                return;
            _warnedUnknownShape = true;

            string typeName = koreoEvent != null ? koreoEvent.GetType().FullName : "(null)";
            PistolWhipHapbeatMod.LogWarning(
                "Beat sync produces no haptic: a Koreography event (" + typeName + ") exposes " +
                "neither IsOneOff() nor a start/end sample pair, so one-shot beats cannot be " +
                "told apart from spans (which repeat every frame). Beats stay silent rather " +
                "than buzz continuously. See README '実機確認チェックリスト' 4.");
        }

        private static List<string> DiscoverEventIds(object koreography)
        {
            var ids = new List<string>();

            // Preferred: the asset's own accessor, when the build exposes one.
            foreach (string name in new[] { "GetEventIDs", "GetAllEventIDs" })
            {
                object list = GameReflection.CallMethod(koreography, name);
                foreach (object item in GameReflection.EnumerateList(list))
                    Add(ids, item as string ?? item.ToString());
                if (ids.Count > 0)
                    return ids;
            }

            // Fallback: walk the track list and read each track's event id.
            object tracks = GameReflection.GetMember(koreography, "mTracks")
                            ?? GameReflection.GetMember(koreography, "Tracks");
            foreach (object track in GameReflection.EnumerateList(tracks))
            {
                object id = GameReflection.GetMember(track, "mEventID")
                            ?? GameReflection.GetMember(track, "EventID");
                if (id != null)
                    Add(ids, id.ToString());
            }
            return ids;
        }

        private static void Add(List<string> ids, string id)
        {
            if (!string.IsNullOrEmpty(id) && !ids.Contains(id))
                ids.Add(id);
        }

        /// <summary>Unregister everything this mod registered (song change / shutdown).</summary>
        public static void Shutdown()
        {
            lock (_lock)
            {
                try { Unregister(Koreo.Koreographer.Instance); }
                catch (Exception) { /* teardown race with scene unload */ }
            }
        }

        private static void Unregister(Koreo.Koreographer instance)
        {
            if (instance == null)
            {
                _registered.Clear();
                return;
            }

            foreach (var kv in _registered)
            {
                try { instance.UnregisterForEvents(kv.Key, kv.Value); }
                catch (Exception) { /* already gone with the previous song */ }
            }
            _registered.Clear();
        }
    }
}
#else
using HarmonyLib;

namespace Hapbeat.PistolWhip
{
    /// <summary>Beat sync compiled out (<c>-p:DisableBeatSync=true</c>).</summary>
    internal static class BeatSync
    {
        public static void Install(HarmonyLib.Harmony harmony)
        {
            PistolWhipHapbeatMod.LogInfo("Beat sync was compiled out of this build (DisableBeatSync).");
        }

        public static void Shutdown() { }
    }
}
#endif
