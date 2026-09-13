using System;
using Hapbeat;
using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>Every gameplay moment that is worth feeling. Phase 3 binds these to Hapbeat events.</summary>
    public enum DemoHapticEvent
    {
        LeftGrab,
        RightGrab,
        LeftRelease,
        RightRelease,
        LeftArmCollide,
        RightArmCollide,
        BodyCollide,
        LeftChargeLoop,
        RightChargeLoop,
        TargetHit,
        LauncherHit,
        LauncherStunned,
        BallIncomingWarning,
        WaveStarted,
        WaveCleared,
        GameOver,
        BowlingLeftImpact, BowlingRightImpact, BowlingBodyImpact,
        VolleyballLeftImpact, VolleyballRightImpact, VolleyballBodyImpact,
        FoamLeftImpact, FoamRightImpact, FoamBodyImpact,
        BasketballLeftImpact, BasketballRightImpact, BasketballBodyImpact,
        PerforatedLeftImpact, PerforatedRightImpact, PerforatedBodyImpact,
    }

    [Serializable]
    public class HapticEventBinding
    {
        public DemoHapticEvent Event;
        public AudioClip Clip;
        [Range(0f, 1f)] public float Volume = 1f;
    }

    /// <summary>One gameplay event routed to the Hapbeat trigger that plays it.</summary>
    [Serializable]
    public class HapbeatEventBinding
    {
        public DemoHapticEvent Event;
        public HapbeatUnityEventTrigger Trigger;
    }

    [Serializable]
    public class HapbeatLoopBinding
    {
        public DemoHapticEvent Event;
        public HapbeatSequenceTrigger Trigger;
    }

    /// <summary>
    /// Single funnel for gameplay feedback: sound out of the speakers, haptics out to the
    /// Hapbeat device. Every gameplay script reports here and none of them know that either
    /// destination exists.
    ///
    /// The haptic side goes through the SDK's own trigger components rather than calling
    /// HapbeatManager directly, so the events stay editable in the EventMap window - gain,
    /// clip and target can be retuned without touching code.
    /// </summary>
    public class HapticEventRelay : MonoBehaviour
    {
        [SerializeField] private HapticEventBinding[] _bindings = Array.Empty<HapticEventBinding>();
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private bool _verboseLog;

        [Header("Hapbeat")]
        [SerializeField] private HapbeatEventBinding[] _hapbeatBindings = Array.Empty<HapbeatEventBinding>();

        [SerializeField] private HapbeatLoopBinding[] _loopBindings = Array.Empty<HapbeatLoopBinding>();

        public static HapticEventRelay Instance { get; private set; }
        private BallImpactBindings _ballImpactBindings;

        /// <summary>Subscribed by diagnostics (batch smoke runs) to observe the event stream.</summary>
        public event Action<DemoHapticEvent, Vector3, float> EventReported;

        /// <summary>Observes audio-only requests without requiring an AudioSource or playing a clip.</summary>
        public event Action<DemoHapticEvent, Vector3, float> AudioRequested;

        private readonly System.Collections.Generic.HashSet<DemoHapticEvent> _activeLoops = new System.Collections.Generic.HashSet<DemoHapticEvent>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            var impactPrefab = Resources.Load<GameObject>("BallImpactBindings");
            if (impactPrefab != null) _ballImpactBindings = Instantiate(impactPrefab, transform).GetComponent<BallImpactBindings>();
            if (_audioSource == null)
            {
                _audioSource = GetComponent<AudioSource>();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public static void Report(DemoHapticEvent evt, Vector3 position, float gain = 1f)
        {
            if (Instance != null)
            {
                Instance.ReportInstance(evt, position, gain);
            }
        }

        public static void ReportBallImpact(Ball ball, DemoHapticEvent surface, Vector3 position)
        {
            PlayAudioOnly(surface, position);
            ReportHapticOnly(ball.ImpactEvent(surface), position);
        }

        public void ReportInstance(DemoHapticEvent evt, Vector3 position, float gain)
        {
            if (_verboseLog)
            {
                Debug.Log($"[Haptic] {evt} gain={gain:0.00} at {position}");
            }

            PlayAudioOnlyInstance(evt, position, gain);
            ReportHapticOnlyInstance(evt, position, gain);
        }

        /// <summary>Reports an event to Hapbeat and diagnostics without requesting its audio clip.</summary>
        public static void ReportHapticOnly(DemoHapticEvent evt, Vector3 position, float gain = 1f)
        {
            if (Instance != null) Instance.ReportHapticOnlyInstance(evt, position, gain);
        }

        /// <summary>Requests only the event's bound audio clip; no Hapbeat trigger is fired.</summary>
        public static void PlayAudioOnly(DemoHapticEvent evt, Vector3 position, float gain = 1f)
        {
            if (Instance != null) Instance.PlayAudioOnlyInstance(evt, position, gain);
        }

        private void ReportHapticOnlyInstance(DemoHapticEvent evt, Vector3 position, float gain)
        {
            FireHapbeat(evt, gain);
            EventReported?.Invoke(evt, position, gain);
        }

        private void PlayAudioOnlyInstance(DemoHapticEvent evt, Vector3 position, float gain)
        {
            AudioRequested?.Invoke(evt, position, gain);
            PlayClip(evt, position, gain);
        }

        /// <summary>
        /// Hands the event to its Hapbeat trigger. The gain the gameplay reported becomes the
        /// trigger's multiplier, so the EventMap keeps the absolute level and the game only ever
        /// says how hard this particular instance was.
        /// </summary>
        private void FireHapbeat(DemoHapticEvent evt, float gain)
        {
            // Batch verification runs report and count events but must not put them on the wire;
            // see DemoHapticsGate.
            if (!DemoHapticsGate.LiveSendEnabled)
            {
                return;
            }

            var trigger = FindTrigger(evt);
            if (trigger == null)
            {
                return;
            }

            trigger.GainMultiplier = Mathf.Clamp(gain, 0f, 2f);
            trigger.Fire();
        }

        /// <summary>Trigger bound to an event, or null. Used by the batch run to prove the wiring.</summary>
        public HapbeatUnityEventTrigger GetHapbeatTrigger(DemoHapticEvent evt) => FindTrigger(evt);

        public HapbeatSequenceTrigger GetLoopTrigger(DemoHapticEvent evt) => FindLoopTrigger(evt);

        private HapbeatUnityEventTrigger FindTrigger(DemoHapticEvent evt)
        {
            if (_ballImpactBindings != null && evt >= DemoHapticEvent.BowlingLeftImpact)
                return _ballImpactBindings.Find(evt);
            if (_hapbeatBindings == null)
            {
                return null;
            }

            foreach (var binding in _hapbeatBindings)
            {
                if (binding != null && binding.Event == evt)
                {
                    return binding.Trigger;
                }
            }

            return null;
        }

        /// <summary>Starts the held-ball loop. Repeated calls while active only update the gain.</summary>
        public static void BeginLoop(DemoHapticEvent evt, Vector3 position, float gain)
        {
            if (Instance == null)
            {
                return;
            }

            if (Instance._activeLoops.Add(evt))
            {
                Instance.ReportInstance(evt, position, gain);
                Instance.StartLoopHaptic(evt, gain);
                return;
            }
            UpdateLoop(evt, position, gain);
        }

        /// <summary>
        /// Gain modulation while the ball is held. The multiplier is pushed onto the stream that
        /// is already playing, so the hum swells with the hand instead of restarting.
        /// </summary>
        public static void UpdateLoop(DemoHapticEvent evt, Vector3 position, float gain)
        {
            if (Instance == null || !Instance._activeLoops.Contains(evt))
            {
                return;
            }

            var trigger = Instance.FindLoopTrigger(evt);
            if (trigger != null)
            {
                trigger.GainMultiplier = Mathf.Clamp(gain, 0f, 2f);
            }
            Instance.EventReported?.Invoke(evt, position, gain);
        }

        private void StartLoopHaptic(DemoHapticEvent evt, float gain)
        {
            if (!DemoHapticsGate.LiveSendEnabled)
            {
                return;
            }

            var trigger = FindLoopTrigger(evt);
            if (trigger == null)
            {
                return;
            }

            trigger.GainMultiplier = Mathf.Clamp(gain, 0f, 2f);
            trigger.Fire();
        }

        public static void EndLoop(DemoHapticEvent evt)
        {
            if (Instance == null || !Instance._activeLoops.Remove(evt))
            {
                return;
            }

            var trigger = Instance.FindLoopTrigger(evt);
            if (trigger != null && DemoHapticsGate.LiveSendEnabled)
            {
                trigger.Stop();
            }
        }

        private HapbeatSequenceTrigger FindLoopTrigger(DemoHapticEvent evt)
        {
            foreach (var binding in _loopBindings)
                if (binding != null && binding.Event == evt) return binding.Trigger;
            return null;
        }

        private void PlayClip(DemoHapticEvent evt, Vector3 position, float gain)
        {
            if (_bindings == null || _audioSource == null)
            {
                return;
            }

            foreach (var binding in _bindings)
            {
                if (binding == null || binding.Event != evt || binding.Clip == null)
                {
                    continue;
                }

                // One shared source keeps the voice count bounded on Quest.
                _audioSource.transform.position = position;
                _audioSource.PlayOneShot(binding.Clip, Mathf.Clamp01(binding.Volume * gain));
                return;
            }
        }
    }
}
