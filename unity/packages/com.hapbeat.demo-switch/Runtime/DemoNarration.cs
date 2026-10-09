using System;
using UnityEngine;

namespace Hapbeat.DemoSwitch
{
    /// <summary>
    /// Narration volume of every demo (hapbeat-contracts specs/demo-session.md "ナレーションの音量"). The Hub's
    /// `narration_volume` arrives in the session ticket; the playback gain is `narration_volume / 100 × engine calibration`.
    /// Demos keep no volume constant of their own: they play narration files normalized to -20 LUFS through
    /// <see cref="Apply"/> (or <see cref="Gain"/>).
    /// </summary>
    public static class DemoNarration
    {
        public const int MinVolume = 0;
        public const int MaxVolume = 100;
        public const int VolumeStep = 10;

        /// <summary>
        /// Unity's engine calibration. Replace with the value measured on a Quest so that the same file at the same
        /// `narration_volume` comes out as loud as in Unreal (HapbeatDemoSession).
        /// </summary>
        public const float EngineCalibration = 1.0f;

        private static int? _volumeForTests;
        private static int _notifiedVolume = DemoSwitchSettings.DefaultNarrationVolume;

        /// <summary>Raised when <see cref="Volume"/> changes (a session ticket was read, or a test value was set).</summary>
        public static event Action Changed;

        /// <summary>The ticket's `narration_volume`, else <see cref="DemoSwitchSettings.DefaultNarrationVolume"/>.</summary>
        public static int Volume => _volumeForTests ?? DemoSession.Ticket?.NarrationVolume ?? DemoSwitchSettings.DefaultNarrationVolume;

        /// <summary>Playback gain for narration: `Volume / 100 × EngineCalibration`.</summary>
        public static float Gain => Volume / 100f * EngineCalibration;

        /// <summary>Sets <paramref name="source"/>'s volume to <see cref="Gain"/>.</summary>
        public static void Apply(AudioSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            source.volume = Gain;
        }

        /// <summary>0..100 in steps of 10 (schema `narration_volume`).</summary>
        public static bool IsValidVolume(long volume) => volume >= MinVolume && volume <= MaxVolume && volume % VolumeStep == 0;

        /// <summary>Raises <see cref="Changed"/> when <see cref="Volume"/> differs from the last notified value.</summary>
        internal static void NotifyIfChanged()
        {
            var volume = Volume;
            if (volume == _notifiedVolume) return;
            _notifiedVolume = volume;
            Changed?.Invoke();
        }

        /// <summary>Replaces <see cref="Volume"/> in tests (null: the ticket or the default again).</summary>
        internal static void SetVolumeForTests(int? volume)
        {
            if (volume.HasValue && !IsValidVolume(volume.Value)) throw new ArgumentOutOfRangeException(nameof(volume));
            _volumeForTests = volume;
            NotifyIfChanged();
        }

        internal static void ResetForTests()
        {
            _volumeForTests = null;
            Changed = null;
            _notifiedVolume = Volume;
        }
    }
}
