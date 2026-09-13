using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Hapbeat.Boxing
{
    [DefaultExecutionOrder(0)]
    public sealed class BoxingGame : MonoBehaviour
    {
        public BoxingTuning tuning;
        public BoxingInput input;
        public BoxingFeedback feedback;
        public BoxingPresentation presentation;
        public BoxingMenu menu;
        public BoxingRound Round { get; private set; } = new BoxingRound();
        public BoxingOpponent Opponent { get; private set; }
        public bool Paused { get; private set; }
        public string PauseReason { get; private set; } = "";
        public bool TrackingReady => validTime >= 0.2f;
        private bool focus = true, appPaused;
        private float validTime, leftCooldown, rightCooldown;
        private bool haveHistory, leftContact, rightContact;
        private BoxerPose previous;
        private Vector3 oldEnemyLeft, oldEnemyRight, oldEnemyHead, oldEnemyBody;
        private int resolvedAttack, lastCompleted;
        private BoxingPhase previousPhase;
        private XRDisplaySubsystem display;
        private bool displayFocused = true;
        private readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

        public void Initialize() { if (Opponent == null) Opponent = new BoxingOpponent(tuning); previousPhase = Round.Phase; }
        private void Awake() => Initialize();
        private void OnEnable()
        {
            Hapbeat.DemoSwitch.DemoSwitch.BeforeSwitch += BeforeSwitch;
            Hapbeat.DemoSwitch.DemoSwitch.LaunchContextDetected += LaunchContext;
            Application.focusChanged += FocusChanged;
        }
        private void OnDisable()
        {
            Hapbeat.DemoSwitch.DemoSwitch.BeforeSwitch -= BeforeSwitch;
            Hapbeat.DemoSwitch.DemoSwitch.LaunchContextDetected -= LaunchContext;
            Application.focusChanged -= FocusChanged;
            if (display != null) display.displayFocusChanged -= DisplayFocus;
            if (feedback != null) feedback.StopFeedback();
        }
        // A failed external launch leaves a resumable menu, not a permanent switch-pending latch.
        public void PauseForExternalTransition() { if (!menu.IsOpen) menu.Open(); ResetHistory(); feedback.StopFeedback(); }
        private void BeforeSwitch(string _) => PauseForExternalTransition();
        private void LaunchContext() => PauseForExternalTransition();
        private void FocusChanged(bool value) { focus = value; if (!value) PauseForExternalTransition(); }
        private void DisplayFocus(bool value) { displayFocused = value; if (!value) PauseForExternalTransition(); }
        private void OnApplicationPause(bool value) { appPaused = value; if (value) PauseForExternalTransition(); }
        public void ResetHistory() { haveHistory = false; validTime = 0; leftContact = rightContact = false; leftCooldown = rightCooldown = 0; }
        public void StartRound()
        {
            Round.Start(tuning.roundSeconds); Opponent.Reset(input.Current.head.y > 0.5f ? input.Current.head.y : 1.65f);
            resolvedAttack = lastCompleted = 0; ResetHistory(); feedback.StopFeedback(); menu.Close();
        }
        private void Update() => Simulate(Time.unscaledDeltaTime, input.Current);
        public void Simulate(float dt, BoxerPose pose)
        {
            bool tracking = pose.valid && dt > 0 && dt <= 0.1f;
            bool outside = new Vector2(pose.head.x, pose.head.z).magnitude > tuning.playRadius;
            if (display == null)
            {
                SubsystemManager.GetSubsystems(displays);
                display = displays.Find(d => d.running);
                if (display != null) display.displayFocusChanged += DisplayFocus;
            }
            bool unavailable = !input.HasOverride && (!focus || appPaused || !displayFocused);
            if (!tracking || outside || unavailable || (menu != null && menu.IsOpen))
            {
                if (!Paused) feedback.StopFeedback();
                Paused = true;
                PauseReason = unavailable ? "HEADSET PAUSED" : outside ? "RETURN TO YOUR START POSITION" : !tracking ? "TRACKING LOST - SHOW BOTH HANDS / CONTROLLERS" : "PAUSED";
                ResetHistory();
                if (presentation != null) presentation.Render(this, pose, false);
                return;
            }
            validTime += dt;
            Paused = !TrackingReady;
            PauseReason = Paused ? "HOLD STILL" : "";
            if (Paused)
            {
                leftContact = TouchingEnemy(pose.left); rightContact = TouchingEnemy(pose.right);
                SaveHistory(pose); return;
            }
            if (haveHistory && (Vector3.Distance(pose.left, previous.left) > 0.6f || Vector3.Distance(pose.right, previous.right) > 0.6f || Vector3.Distance(pose.head, previous.head) > 0.35f))
            { ResetHistory(); feedback.StopFeedback(); return; }
            Round.Tick(dt, false);
            Opponent.Tick(dt, pose.head, Round.Phase == BoxingPhase.Fighting);
            if (Round.Phase != previousPhase)
            {
                if (Round.Phase == BoxingPhase.Results) { feedback.StopFeedback(); menu.Open(); }
                if (Round.Phase == BoxingPhase.Fighting || Round.Phase == BoxingPhase.Results) feedback.Ring();
                previousPhase = Round.Phase;
            }
            if (haveHistory && Round.Phase == BoxingPhase.Fighting)
            {
                leftCooldown = Mathf.Max(0, leftCooldown - dt); rightCooldown = Mathf.Max(0, rightCooldown - dt);
                ResolveEnemy(pose, dt);
                ResolvePlayer(pose.left, previous.left, pose.leftClosed, ImpactZone.LeftGlove, ref leftContact, ref leftCooldown, dt);
                ResolvePlayer(pose.right, previous.right, pose.rightClosed, ImpactZone.RightGlove, ref rightContact, ref rightCooldown, dt);
                if (Opponent.CompletedAttacks > lastCompleted)
                {
                    if (resolvedAttack != Opponent.AttackId) Round.Dodge();
                    lastCompleted = Opponent.CompletedAttacks;
                }
            }
            SaveHistory(pose);
            if (presentation != null) presentation.Render(this, pose, true);
        }
        private void ResolveEnemy(BoxerPose p, float dt)
        {
            if (!Opponent.Striking || resolvedAttack == Opponent.AttackId) return;
            Vector3 from = Opponent.AttackLeft ? oldEnemyLeft : oldEnemyRight;
            Vector3 to = Opponent.AttackLeft ? Opponent.Left : Opponent.Right;
            float earliest = float.PositiveInfinity;
            ImpactZone zone = ImpactZone.Head;
            Vector3 targetFrom = Vector3.zero, targetTo = Vector3.zero;
            void Candidate(ImpactZone z, Vector3 a, Vector3 b, float radius)
            {
                if (BoxingCollision.Sweep(from, to, tuning.gloveRadius, a, b, radius, out float t) && t < earliest)
                { earliest = t; zone = z; targetFrom = a; targetTo = b; }
            }
            Candidate(ImpactZone.LeftGlove, previous.left, p.left, tuning.gloveRadius);
            Candidate(ImpactZone.RightGlove, previous.right, p.right, tuning.gloveRadius);
            Candidate(ImpactZone.Head, previous.head, p.head, tuning.headRadius);
            if (float.IsPositiveInfinity(earliest)) return;
            resolvedAttack = Opponent.AttackId;
            float speed = BoxingCollision.RelativeSpeed(from, to, targetFrom, targetTo, dt);
            if (speed >= tuning.minimumImpactSpeed) Report(new BoxingImpact(zone, speed, false, Vector3.Lerp(from, to, earliest), tuning));
        }
        private void ResolvePlayer(Vector3 to, Vector3 from, bool closed, ImpactZone side, ref bool contact, ref float cooldown, float dt)
        {
            bool head = BoxingCollision.Sweep(from, to, tuning.gloveRadius, oldEnemyHead, Opponent.Head, tuning.enemyHeadRadius, out _);
            bool body = BoxingCollision.Sweep(from, to, tuning.gloveRadius, oldEnemyBody, Opponent.Body, tuning.enemyBodyRadius, out _);
            bool overlap = Vector3.Distance(to, Opponent.Head) <= tuning.gloveRadius + tuning.enemyHeadRadius + 0.03f ||
                Vector3.Distance(to, Opponent.Body) <= tuning.gloveRadius + tuning.enemyBodyRadius + 0.03f;
            // Hysteresis releases an existing contact; it must never create an early one.
            bool wasContact = contact; contact = head || body || (wasContact && overlap);
            if ((!head && !body) || wasContact || cooldown > 0 || !closed) return;
            float speed = BoxingCollision.RelativeSpeed(from, to, head ? oldEnemyHead : oldEnemyBody, head ? Opponent.Head : Opponent.Body, dt);
            if (speed < tuning.minimumImpactSpeed) return;
            cooldown = tuning.hitCooldown; Opponent.React(tuning.Gain(speed));
            Report(new BoxingImpact(side, speed, true, to, tuning));
        }
        private bool TouchingEnemy(Vector3 position) => Vector3.Distance(position, Opponent.Head) <= tuning.gloveRadius + tuning.enemyHeadRadius + 0.03f ||
            Vector3.Distance(position, Opponent.Body) <= tuning.gloveRadius + tuning.enemyBodyRadius + 0.03f;
        private void Report(BoxingImpact impact)
        {
            Round.Report(impact); feedback.Impact(impact);
            if (presentation != null) presentation.Flash(impact);
        }
        private void SaveHistory(BoxerPose pose)
        {
            previous = pose; oldEnemyLeft = Opponent.Left; oldEnemyRight = Opponent.Right;
            oldEnemyHead = Opponent.Head; oldEnemyBody = Opponent.Body; haveHistory = true;
        }
    }
}
