using System;
using Hapbeat;
using UnityEngine;

namespace Hapbeat.Boxing
{
    [DefaultExecutionOrder(-10000)]
    public sealed class BoxingFeedback : MonoBehaviour
    {
        public GameObject sdkRoot;
        // Per receiver: glove soft/hard, body soft/hard. Surface is independent of attack direction.
        public HapbeatUnityEventTrigger[] impactTriggers = new HapbeatUnityEventTrigger[12];
        public AudioSource audioSource, bellSource;
        public AudioClip[] contactSounds = new AudioClip[4];
        public AudioClip bell;
        public bool soundEnabled = true;
        public bool hapticsEnabled = true;
        public bool forceSilent;
        public int Reports { get; private set; }
        public int Sends { get; private set; }
        public int Rings { get; private set; }
        public BoxingImpact LastImpact { get; private set; }
        public event Action<BoxingImpact> Reported;
        public bool CanSend => !Application.isBatchMode && !forceSilent && hapticsEnabled;
        private void Awake()
        {
            if (Application.isBatchMode || forceSilent)
            {
                if (sdkRoot != null) sdkRoot.SetActive(false);
                if (audioSource != null) audioSource.mute = true;
                if (bellSource != null) bellSource.mute = true;
            }
        }
        public void Impact(BoxingImpact impact)
        {
            LastImpact = impact; Reports++; Reported?.Invoke(impact);
            if (CanSend)
            {
                int index = TriggerIndex(impact);
                if (index < impactTriggers.Length && impactTriggers[index] != null)
                {
                    var trigger = impactTriggers[index];
                    trigger.GainMultiplier = impact.gain;
                    // Position addressing selects the wearable; do not silence one channel of a wrist unit.
                    trigger.Pan = 0;
                    trigger.Fire(); Sends++;
                }
            }
            PlaySound(contactSounds[SoundIndex(impact)], Mathf.Clamp01(impact.gain));
        }
        public static int SoundIndex(BoxingImpact impact) => (int)impact.surface * 2 + (impact.hard ? 1 : 0);
        public static int TriggerIndex(BoxingImpact impact) => (int)impact.zone * 4 + SoundIndex(impact);
        public void Ring()
        {
            Rings++;
            if (!Application.isBatchMode && !forceSilent && soundEnabled && bellSource != null && bell != null)
            { bellSource.Stop(); bellSource.PlayOneShot(bell, 0.5f); }
        }
        private void PlaySound(AudioClip clip, float volume)
        {
            if (!Application.isBatchMode && !forceSilent && soundEnabled && audioSource != null && clip != null)
                audioSource.PlayOneShot(clip, volume);
        }
        public void StopImpacts()
        {
            foreach (var trigger in impactTriggers)
                if (trigger != null) { trigger.FlushPendingDelayCoroutines(); if (!Application.isBatchMode && !forceSilent) trigger.Stop(); }
            if (!Application.isBatchMode && !forceSilent && HapbeatManager.Instance != null && HapbeatManager.Instance.IsConnected) HapbeatManager.Instance.StopAll();
            if (audioSource != null) audioSource.Stop();
        }
        public void StopFeedback() { StopImpacts(); if (bellSource != null) bellSource.Stop(); }
        private void OnDisable() => StopFeedback();
    }
}
