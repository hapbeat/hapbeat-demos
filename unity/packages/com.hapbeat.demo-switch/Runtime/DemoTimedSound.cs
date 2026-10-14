using System.Collections.Generic;
using UnityEngine;

namespace Hapbeat.DemoSwitch
{
    /// <summary>
    /// One-shot sounds started at an exact time on the audio clock (<c>PlayScheduled</c> at
    /// <see cref="DemoAudioClock.Now"/> + delay), each on its own pooled copy of a template source, so overlapping
    /// one-shots never cut each other (DEC-093). <c>PlayOneShot</c> starts at the next mixed buffer: up to one buffer
    /// late, a different amount each time. A delay of at least <see cref="Lead"/> is placed exactly.
    /// </summary>
    public sealed class DemoTimedSound
    {
        private readonly AudioSource _template;
        private readonly List<AudioSource> _voices = new List<AudioSource>();
        private readonly List<double> _busyUntil = new List<double>();

        /// <param name="template">Source whose settings (mixer group, spatial blend, volume, pitch, mute, ...) every voice copies; it is not played itself.</param>
        public DemoTimedSound(AudioSource template, int voices = 8)
        {
            _template = template;
            for (var index = 0; index < Mathf.Max(1, voices); index++)
            {
                var go = new GameObject("Timed voice " + index);
                go.transform.SetParent(template.transform, false);
                var voice = go.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                _voices.Add(voice);
                _busyUntil.Add(0);
            }
        }

        /// <summary>The shortest delay placed exactly: one mixed buffer plus 5 ms (a shorter one plays at the next buffer).</summary>
        public static double Lead
        {
            get
            {
                AudioSettings.GetDSPBufferSize(out var length, out _);
                var rate = Mathf.Max(1, AudioSettings.outputSampleRate);
                return (double)length / rate + 0.005;
            }
        }

        /// <summary>Plays <paramref name="clip"/> at the template's volume × <paramref name="volume"/>, <paramref name="delaySeconds"/> from now.</summary>
        public void Play(AudioClip clip, float volume, double delaySeconds)
        {
            if (clip == null) return;
            var at = DemoAudioClock.Now + System.Math.Max(0, delaySeconds);
            var pick = 0;
            for (var index = 1; index < _voices.Count; index++)
                if (_busyUntil[index] < _busyUntil[pick]) pick = index;
            var voice = _voices[pick];
            Copy(_template, voice);
            voice.clip = clip;
            voice.volume = _template.volume * volume;
            voice.PlayScheduled(at);
            _busyUntil[pick] = at + clip.length / Mathf.Max(0.01f, Mathf.Abs(voice.pitch));
        }

        public void StopAll()
        {
            foreach (var voice in _voices) if (voice != null) voice.Stop();
            for (var index = 0; index < _busyUntil.Count; index++) _busyUntil[index] = 0;
        }

        private static void Copy(AudioSource from, AudioSource to)
        {
            to.outputAudioMixerGroup = from.outputAudioMixerGroup;
            to.mute = from.mute;
            to.bypassEffects = from.bypassEffects;
            to.bypassListenerEffects = from.bypassListenerEffects;
            to.bypassReverbZones = from.bypassReverbZones;
            to.priority = from.priority;
            to.pitch = from.pitch;
            to.panStereo = from.panStereo;
            to.spatialBlend = from.spatialBlend;
            to.reverbZoneMix = from.reverbZoneMix;
            to.dopplerLevel = from.dopplerLevel;
            to.spread = from.spread;
            to.rolloffMode = from.rolloffMode;
            to.minDistance = from.minDistance;
            to.maxDistance = from.maxDistance;
            to.ignoreListenerPause = from.ignoreListenerPause;
            to.ignoreListenerVolume = from.ignoreListenerVolume;
        }
    }
}
