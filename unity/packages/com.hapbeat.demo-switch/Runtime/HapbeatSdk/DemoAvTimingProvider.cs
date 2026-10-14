// HapbeatAvSync and the stream start delay arrived in com.hapbeat.sdk 0.7.0-d1; older SDKs leave the pause menu
// without 音と振動のタイミング.
#if HAPBEAT_SDK_AV_SYNC
using UnityEngine;

namespace Hapbeat.DemoSwitch
{
    /// <summary>
    /// <see cref="DemoAvTiming"/> on the Hapbeat SDK (DEC-093): the offset is <see cref="HapbeatAvSync.OffsetMs"/>
    /// (saved per app on the device), the model's value from <see cref="DemoAvTiming.ModelDefaults"/> is its default,
    /// and the test pattern is a click on this device's audio with a pulse streamed to its Hapbeats.
    /// Compiled only when `com.hapbeat.sdk` is installed; no scene component or demo code is required.
    /// </summary>
    internal sealed class DemoAvTimingProvider : IDemoAvTimingProvider
    {
        private DemoAvTimingPattern _pattern;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            var model = DemoAvTiming.ModelDefaultMs;
            if (model.HasValue) HapbeatAvSync.DefaultOffsetMs = model.Value;
            DemoAvTiming.Provider = new DemoAvTimingProvider();
            Debug.Log($"[Demo AV Timing] {DemoAvTiming.DeviceModel}: offset {HapbeatAvSync.OffsetMs:0} ms " +
                      (HapbeatAvSync.HasSavedOffset ? "(saved on this device)" : model.HasValue ? "(model default)" : "(no model default)"));
        }

        public float OffsetMs
        {
            get => HapbeatAvSync.OffsetMs;
            set => HapbeatAvSync.OffsetMs = value;
        }

        public bool HasSavedOffset => HapbeatAvSync.HasSavedOffset;

        public void ClearSavedOffset() => HapbeatAvSync.ClearSavedOffset();

        public void StartPattern()
        {
            if (_pattern != null) return;
            var go = new GameObject("Hapbeat Demo AV Timing Pattern");
            Object.DontDestroyOnLoad(go);
            _pattern = go.AddComponent<DemoAvTimingPattern>();
        }

        public void StopPattern()
        {
            if (_pattern == null) return;
            Object.Destroy(_pattern.gameObject);
            _pattern = null;
        }
    }

    /// <summary>
    /// Every second: a click on this device's audio and a short pulse streamed to the Hapbeats, both placed exactly
    /// (<see cref="DemoTimedSound"/> on the audio clock, the stream timeline for the haptic) and moved apart by the
    /// offset as a game firing would be (<see cref="HapbeatAvSync.Split"/> with sync points 0). The click ignores
    /// the pause (the shared pause mutes Unity audio while this runs).
    /// </summary>
    internal sealed class DemoAvTimingPattern : MonoBehaviour
    {
        public const double PeriodSeconds = 1.0;
        private const int PulseRate = 16000;
        private AudioClip _click;
        private AudioClip _pulse;
        private DemoTimedSound _sound;
        private double _next;

        private void Awake()
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 1f;
            source.ignoreListenerPause = true;
            _sound = new DemoTimedSound(source, 2);
            var click = DemoSessionClickSound.Samples(DemoSessionClickSound.SampleRate);
            for (var index = 0; index < click.Length; index++) click[index] *= 1.8f;
            _click = AudioClip.Create("Hapbeat AV Timing Click", click.Length, 1, DemoSessionClickSound.SampleRate, false);
            _click.SetData(click, 0);
            _pulse = AudioClip.Create("Hapbeat AV Timing Pulse", Mathf.RoundToInt(0.06f * PulseRate), 1, PulseRate, false);
            _pulse.SetData(PulseSamples(), 0);
            _next = Time.realtimeSinceStartupAsDouble + 0.3;
        }

        /// <summary>An 80 Hz cosine at 0.9 from its first sample, decaying over about 15 ms: a sharp start to feel.</summary>
        internal static float[] PulseSamples()
        {
            var samples = new float[Mathf.RoundToInt(0.06f * PulseRate)];
            for (var index = 0; index < samples.Length; index++)
            {
                var t = (float)index / PulseRate;
                samples[index] = 0.9f * Mathf.Cos(2f * Mathf.PI * 80f * t) * Mathf.Exp(-t / 0.015f);
            }
            return samples;
        }

        private void Update()
        {
            var now = Time.realtimeSinceStartupAsDouble;
            if (now < _next) return;
            _next = now - _next > PeriodSeconds ? now + PeriodSeconds : _next + PeriodSeconds;
            HapbeatAvSync.Split(0, 0, out var soundDelay, out var hapticDelay);
            var lead = DemoTimedSound.Lead;
            _sound.Play(_click, 1f, lead + soundDelay);
            var manager = HapbeatManager.Instance;
            if (manager != null && manager.IsConnected) manager.StreamAudioClip(_pulse, 1f, 1f, null, false, lead + hapticDelay);
        }

        private void OnDestroy()
        {
            _sound?.StopAll();
            if (_click != null) Destroy(_click);
            if (_pulse != null) Destroy(_pulse);
        }
    }
}
#endif
