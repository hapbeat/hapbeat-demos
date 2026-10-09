using UnityEngine;

namespace Hapbeat.DemoHub
{
    /// <summary>
    /// The manage screen's 触覚テスト and CONTROL `haptics_test`: a 100 Hz sine for 1 s at a fixed moderate level, streamed by the
    /// Hapbeat SDK with no target, so the SDK addresses it to this headset's device address (the package's
    /// DemoDeviceAddressApplier sets `hapbeat-device.json` on the SDK, as in every demo). Haptics only: nothing is played as sound.
    /// </summary>
    public static class HubHapticsTest
    {
        public const float FrequencyHz = 100f;
        public const float DurationSeconds = 1f;
        /// <summary>The fixed moderate level: the SDK stream gain of a full-scale sine.</summary>
        public const float Gain = 0.5f;
        /// <summary>The SDK's stream output rate, so the tone is sent without resampling.</summary>
        public const int SampleRate = 16000;

        /// <summary>Hands mono samples to the haptics output; false with the error when it did not take them.</summary>
        public delegate bool Output(float[] samples, int sampleRate, float gain, out string error);

        /// <summary>The tone's mono samples: a whole number of periods, so it starts and ends at zero.</summary>
        internal static float[] Samples(int sampleRate)
        {
            var count = Mathf.RoundToInt(DurationSeconds * sampleRate);
            var samples = new float[count];
            for (var index = 0; index < count; index++)
                samples[index] = Mathf.Sin(2f * Mathf.PI * FrequencyHz * index / sampleRate);
            return samples;
        }

#if HAPBEAT_DEMO_HUB_HAS_SDK
        /// <summary>The Hapbeat SDK output.</summary>
        public static Output SdkOutput => StreamWithSdk;

        /// <summary>
        /// Starts the SDK's <see cref="HapbeatManager"/> unless one exists, so devices answer PING (the stream needs a
        /// PONG-backed endpoint) and the device address is applied before the first test.
        /// </summary>
        public static void StartSdk()
        {
            if (HapbeatManager.Instance != null) return;
            new GameObject("Hapbeat SDK").AddComponent<HapbeatManager>();
        }

        private static bool StreamWithSdk(float[] samples, int sampleRate, float gain, out string error)
        {
            var manager = HapbeatManager.Instance;
            if (manager == null)
            {
                error = "The Hapbeat SDK is not running.";
                return false;
            }
            var clip = AudioClip.Create("Hapbeat Hub haptics test", samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            // The SDK copies the samples while adding the source, so the clip is not needed afterwards.
            var playback = manager.StreamAudioClip(clip, gain);
            Object.Destroy(clip);
            error = playback == null ? "The Hapbeat SDK is not connected." : null;
            return playback != null;
        }
#else
        /// <summary>Null: this Hub was built without the Hapbeat SDK.</summary>
        public static Output SdkOutput => null;

        public static void StartSdk() { }
#endif
    }
}
