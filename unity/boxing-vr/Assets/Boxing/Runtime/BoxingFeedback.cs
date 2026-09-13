using System;
using Hapbeat;
using UnityEngine;

namespace Hapbeat.Boxing
{
    [DefaultExecutionOrder(-10000)]
    public sealed class BoxingFeedback : MonoBehaviour
    {
        public GameObject sdkRoot;
        // Two entries per zone: soft then hard. Continuous mode uses the soft waveform.
        public HapbeatUnityEventTrigger[] impactTriggers = new HapbeatUnityEventTrigger[6];
        public AudioSource audioSource;
        public AudioClip softSound, hardSound, bell;
        public bool soundEnabled = true;
        public bool hapticsEnabled = true;
        public bool forceSilent;
        public int Reports { get; private set; }
        public int Sends { get; private set; }
        public BoxingImpact LastImpact { get; private set; }
        public event Action<BoxingImpact> Reported;
        public bool CanSend => !Application.isBatchMode && !forceSilent && hapticsEnabled;
        private void Awake()
        {
            if (Application.isBatchMode || forceSilent)
            {
                if (sdkRoot != null) sdkRoot.SetActive(false);
                if (audioSource != null) audioSource.mute = true;
            }
        }
        public void Impact(BoxingImpact impact)
        {
            LastImpact = impact; Reports++; Reported?.Invoke(impact);
            if (CanSend)
            {
                int index = (int)impact.zone * 2 + (impact.hard ? 1 : 0);
                if (index < impactTriggers.Length && impactTriggers[index] != null)
                {
                    var trigger = impactTriggers[index];
                    trigger.GainMultiplier = impact.gain;
                    // Position addressing selects the wearable; do not silence one channel of a wrist unit.
                    trigger.Pan = 0;
                    trigger.Fire(); Sends++;
                }
            }
            PlaySound(impact.hard ? hardSound : softSound, Mathf.Clamp01(impact.gain));
        }
        public void Ring() => PlaySound(bell, 0.35f);
        private void PlaySound(AudioClip clip, float volume)
        {
            if (!Application.isBatchMode && !forceSilent && soundEnabled && audioSource != null && clip != null)
                audioSource.PlayOneShot(clip, volume);
        }
        public void StopFeedback()
        {
            foreach (var trigger in impactTriggers)
                if (trigger != null) { trigger.FlushPendingDelayCoroutines(); if (!Application.isBatchMode && !forceSilent) trigger.Stop(); }
            if (!Application.isBatchMode && !forceSilent && HapbeatManager.Instance != null) HapbeatManager.Instance.StopAll();
            if (audioSource != null) audioSource.Stop();
        }
        private void OnDisable() => StopFeedback();
    }
}
