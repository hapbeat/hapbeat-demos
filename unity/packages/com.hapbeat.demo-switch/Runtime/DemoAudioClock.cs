using System;
using UnityEngine;

namespace Hapbeat.DemoSwitch
{
    /// <summary>
    /// The audio (DSP) clock at this moment, for sounds scheduled at an exact time (DEC-093).
    /// <c>AudioSettings.dspTime</c> only moves once per mixed buffer (21 ms at 1024 samples / 48 kHz), so a sound
    /// scheduled at "dspTime + d" lands up to one buffer early, a different amount each time. Sampled every frame,
    /// wall − dspTime is smallest right after a buffer starts: its minimum over the last one to two seconds is the
    /// clock offset, and <see cref="Now"/> = wall − offset.
    /// </summary>
    public static class DemoAudioClock
    {
        private const double BucketSeconds = 1.0;
        private static double _min = double.MaxValue, _previousMin = double.MaxValue, _bucketStart = double.NegativeInfinity;
        private static bool _driven;

        /// <summary>The DSP time now (seconds, on the <c>AudioSettings.dspTime</c> scale).</summary>
        public static double Now
        {
            get
            {
                Sample();
                return Wall - Math.Min(_min, _previousMin);
            }
        }

        private static double Wall => Time.realtimeSinceStartupAsDouble;

        /// <summary>Takes one sample (also done every frame once <see cref="Now"/> was read).</summary>
        public static void Sample()
        {
            if (!_driven && Application.isPlaying)
            {
                _driven = true;
                var go = new GameObject("Hapbeat Demo Audio Clock") { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<Driver>();
            }
            double wall = Wall, offset = wall - AudioSettings.dspTime;
            if (wall - _bucketStart > BucketSeconds)
            {
                _previousMin = _min;
                _min = double.MaxValue;
                _bucketStart = wall;
            }
            if (offset < _min) _min = offset;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _min = _previousMin = double.MaxValue;
            _bucketStart = double.NegativeInfinity;
            _driven = false;
        }

        private sealed class Driver : MonoBehaviour
        {
            private void Update() => Sample();
        }
    }
}
