using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;

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
        private bool leftBlockedThisFrame, rightBlockedThisFrame;
        private BoxerPose previous;
        private Vector3 oldEnemyLeft, oldEnemyRight, oldEnemyHead, oldEnemyBody;
        private int resolvedAttack, lastCompleted;
        private BoxingPhase previousPhase;
        private XRDisplaySubsystem display;
        private bool displayFocused = true, displayFocusKnown;
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
        private void FocusChanged(bool value) { focus = value; if (!value && !displayFocusKnown) PauseForExternalTransition(); }
        private void DisplayFocus(bool value)
        {
            bool lost = !value && (!displayFocusKnown || displayFocused);
            displayFocusKnown = true; displayFocused = value;
            if (lost) PauseForExternalTransition();
        }
        private void OnApplicationPause(bool value) { appPaused = value; if (value) PauseForExternalTransition(); }
        public void ResetHistory() { haveHistory = false; validTime = 0; leftContact = rightContact = false; leftCooldown = rightCooldown = 0; }
        public void StartRound()
        {
            if (input.HasTracking && !input.HasOverride) input.Recenter();
            Round.Start(tuning.roundSeconds, tuning.maximumHealth); Opponent.Reset(input.Current.head.y > 0.5f ? input.Current.head.y : 1.65f);
            resolvedAttack = lastCompleted = 0; ResetHistory(); feedback.StopFeedback(); menu.Close();
        }
        private void Update() => Simulate(Time.unscaledDeltaTime, input.Current);
        public void Simulate(float dt, BoxerPose pose)
        {
            bool tracking = pose.valid && dt > 0 && dt <= 0.1f;
            Vector3 fromStart = pose.head - input.StartPosition;
            bool outside = new Vector2(fromStart.x, fromStart.z).magnitude > tuning.playRadius;
            if (display != null && !display.running)
            {
                display.displayFocusChanged -= DisplayFocus;
                display = null; displayFocusKnown = false;
            }
            if (display == null)
            {
                SubsystemManager.GetSubsystems(displays);
                display = displays.Find(d => d.running);
                if (display != null) display.displayFocusChanged += DisplayFocus;
            }
            // A focused headset does not require the PC mirror/Game window to have focus.
            // Poll OpenXR as well as receiving events: startup focus may precede subscription.
            if (display != null) DisplayFocus(OpenXRUtility.IsSessionFocused);
            bool unavailable = !input.HasOverride && (appPaused || (displayFocusKnown ? !displayFocused : !focus));
            if (!tracking || outside || unavailable || (menu != null && menu.IsOpen))
            {
                if (!Paused) feedback.StopImpacts();
                Paused = true;
                PauseReason = unavailable ? (appPaused ? "APPLICATION PAUSED" : displayFocusKnown ? "HEADSET PAUSED - OPENXR NOT FOCUSED" : "GAME WINDOW NOT FOCUSED") : outside ? "RETURN TO YOUR START POSITION" : !tracking ? "TRACKING LOST - SHOW BOTH HANDS / CONTROLLERS" : "PAUSED";
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
            if (haveHistory && Round.Phase == BoxingPhase.Fighting)
            {
                leftBlockedThisFrame = rightBlockedThisFrame = false;
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
            if (Round.Phase != previousPhase)
            {
                if (Round.Phase == BoxingPhase.Results) { feedback.StopFeedback(); menu.Open(); }
                if (Round.Phase == BoxingPhase.Fighting || Round.Phase == BoxingPhase.Results) feedback.Ring();
                previousPhase = Round.Phase;
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
            // A guard contact stops the trajectory, not just the scoring for this attack.
            if (zone != ImpactZone.Head)
            {
                Opponent.Block(Vector3.Lerp(from, to, earliest));
                leftBlockedThisFrame = zone == ImpactZone.LeftGlove; rightBlockedThisFrame = zone == ImpactZone.RightGlove;
            }
            if (speed >= tuning.minimumImpactSpeed) Report(new BoxingImpact(zone, speed, false, Vector3.Lerp(from, to, earliest), tuning,
                zone == ImpactZone.Head ? ImpactSurface.Body : ImpactSurface.Glove));
        }
        private void ResolvePlayer(Vector3 to, Vector3 from, bool closed, ImpactZone side, ref bool contact, ref float cooldown, float dt)
        {
            if (Round.Phase != BoxingPhase.Fighting) return;
            float earliest = float.PositiveInfinity;
            Vector3 targetFrom = default, targetTo = default;
            var surface = ImpactSurface.Body;
            void Candidate(Vector3 a, Vector3 b, float radius, ImpactSurface material)
            {
                if (BoxingCollision.Sweep(from, to, tuning.gloveRadius, a, b, radius, out float t) && t < earliest)
                { earliest = t; targetFrom = a; targetTo = b; surface = material; }
            }
            // Earliest surface wins; a glove interception cannot also damage the body behind it.
            Candidate(oldEnemyLeft, Opponent.Left, tuning.gloveRadius, ImpactSurface.Glove);
            Candidate(oldEnemyRight, Opponent.Right, tuning.gloveRadius, ImpactSurface.Glove);
            Candidate(oldEnemyHead, Opponent.Head, tuning.enemyHeadRadius, ImpactSurface.Body);
            Candidate(oldEnemyBody, Opponent.Body, tuning.enemyBodyRadius, ImpactSurface.Body);
            bool hit = !float.IsPositiveInfinity(earliest);
            bool overlap = TouchingEnemy(to);
            // Hysteresis releases an existing contact; it must never create an early one.
            bool wasContact = contact; contact = hit || (wasContact && overlap);
            bool alreadyResolved = side == ImpactZone.LeftGlove ? leftBlockedThisFrame : rightBlockedThisFrame;
            if (!hit || wasContact || cooldown > 0 || !closed || alreadyResolved) return;
            float speed = BoxingCollision.RelativeSpeed(from, to, targetFrom, targetTo, dt);
            if (speed < tuning.minimumImpactSpeed) return;
            cooldown = tuning.hitCooldown;
            if (surface == ImpactSurface.Body) Opponent.React(tuning.Gain(speed));
            Report(new BoxingImpact(side, speed, true, Vector3.Lerp(from, to, earliest), tuning, surface));
        }
        private bool TouchingEnemy(Vector3 position) => Vector3.Distance(position, Opponent.Head) <= tuning.gloveRadius + tuning.enemyHeadRadius + 0.03f ||
            Vector3.Distance(position, Opponent.Body) <= tuning.gloveRadius + tuning.enemyBodyRadius + 0.03f ||
            Vector3.Distance(position, Opponent.Left) <= tuning.gloveRadius * 2 + 0.03f || Vector3.Distance(position, Opponent.Right) <= tuning.gloveRadius * 2 + 0.03f;
        private void Report(BoxingImpact impact)
        {
            if (Round.Phase != BoxingPhase.Fighting) return;
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
