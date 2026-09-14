using UnityEngine;
using UnityEngine.UI;

namespace Hapbeat.Boxing
{
    [DefaultExecutionOrder(-100)]
    public sealed class BoxingMenu : MonoBehaviour
    {
        public BoxingGame game;
        public BoxingInput input;
        public Transform panel;
        public Text title, hint;
        public Text[] rows;
        public Image dwellBar;
        public bool IsOpen { get; private set; }
        public int Selection { get; private set; }
        public bool UsesGaze => input.mode == BoxingInputMode.Hands;
        private bool navigationReady = true;
        private float gazeTime, openTime;
        private int gazeSelection = -1;
        private bool gazeLatched;
        private void OnEnable() { if (input != null) input.Recentered += RepositionAfterRecenter; }
        private void OnDisable() { if (input != null) input.Recentered -= RepositionAfterRecenter; }
        private void RepositionAfterRecenter() { if (IsOpen) Open(); }
        private void Start() { input.Recentered -= RepositionAfterRecenter; input.Recentered += RepositionAfterRecenter; Open(); }
        public void Open()
        {
            IsOpen = true; Selection = 0; openTime = Time.unscaledTime;
            gazeTime = 0; gazeLatched = false; gazeSelection = -1;
            SetDwellProgress(0);
            panel.gameObject.SetActive(true);
            var camera = input.headCamera.transform;
            Vector3 forward = Vector3.ProjectOnPlane(camera.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.1f) forward = Vector3.forward;
            panel.position = camera.position + forward * 0.9f + Vector3.down * 0.03f;
            panel.localScale = Vector3.one * 0.001f;
            panel.rotation = Quaternion.LookRotation(forward);
            game.ResetHistory(); game.feedback.StopFeedback(); Refresh();
        }
        public void Close() { IsOpen = false; panel.gameObject.SetActive(false); game.ResetHistory(); }
        private void Update()
        {
            if (input.MenuPressed) { if (IsOpen && game.Round.Phase != BoxingPhase.Ready && game.Round.Phase != BoxingPhase.Results) Close(); else Open(); }
            if (!IsOpen) return;
            if (Mathf.Abs(input.Navigate) < 0.25f) navigationReady = true;
            if (navigationReady && Mathf.Abs(input.Navigate) > 0.6f)
            {
                Selection = (Selection + (input.Navigate > 0 ? rows.Length - 1 : 1)) % rows.Length;
                navigationReady = false; gazeTime = 0;
            }
            if (input.ConfirmPressed) { gazeLatched = true; Activate(Selection); }
            if (!IsOpen) return;
            // Gaze dwell provides a controller-free menu and needs no select/pinch gesture.
            var camera = input.headCamera.transform;
            var plane = new Plane(panel.forward, panel.position);
            int hovered = -1;
            if (UsesGaze && plane.Raycast(new Ray(camera.position, camera.forward), out float distance) && distance < 3)
            {
                Vector3 point = camera.position + camera.forward * distance;
                for (int i = 0; i < rows.Length; i++)
                {
                    Vector3 local = rows[i].rectTransform.InverseTransformPoint(point);
                    if (rows[i].rectTransform.rect.Contains(new Vector2(local.x, local.y))) { hovered = i; break; }
                }
            }
            if (hovered != gazeSelection) { gazeTime = 0; gazeLatched = false; gazeSelection = hovered; }
            if (hovered >= 0 && Time.unscaledTime - openTime > 0.8f)
            {
                Selection = hovered;
                if (!gazeLatched)
                {
                    gazeTime += Time.unscaledDeltaTime;
                    if (gazeTime >= 1.5f) { gazeLatched = true; Activate(hovered); }
                }
            }
            else gazeTime = 0;
            SetDwellProgress(gazeTime / 1.5f);
            Refresh();
        }
        private void SetDwellProgress(float progress)
        {
            if (dwellBar == null) return;
            // A sprite-less Unity Image ignores Filled mode; size its visible geometry instead.
            dwellBar.enabled = progress > 0;
            dwellBar.rectTransform.localScale = new Vector3(Mathf.Clamp01(progress), 1, 1);
        }
        public void Activate(int index)
        {
            switch (index)
            {
                case 0:
                    if (game.Round.Phase == BoxingPhase.Ready || game.Round.Phase == BoxingPhase.Results) game.StartRound(); else Close();
                    break;
                case 1: game.StartRound(); break;
                case 2: input.SelectMode((BoxingInputMode)(((int)input.mode + 1) % (Application.isEditor ? 3 : 2))); game.ResetHistory(); break;
                case 3: game.tuning.impactMode = game.tuning.impactMode == ImpactMode.Continuous ? ImpactMode.WeakHard : ImpactMode.Continuous; break;
                case 4: input.Recenter(); Open(); break;
                case 5: game.feedback.hapticsEnabled = !game.feedback.hapticsEnabled; if (!game.feedback.hapticsEnabled) game.feedback.StopFeedback(); break;
                case 6: game.feedback.soundEnabled = !game.feedback.soundEnabled; if (!game.feedback.soundEnabled) { game.feedback.audioSource.Stop(); game.feedback.bellSource.Stop(); } break;
            }
        }
        private void Refresh()
        {
            title.text = game.Round.Phase == BoxingPhase.Results ?
                (game.Round.PlayerHealth <= 0 ? "KO - OPPONENT WINS" : game.Round.EnemyHealth <= 0 ? "KO - YOU WIN" : "ROUND COMPLETE") + "\n" + game.Round.Score + " POINTS" : "HAPBEAT\nBOXING";
            string[] labels = {game.Round.Phase == BoxingPhase.Ready || game.Round.Phase == BoxingPhase.Results ? "START " + game.tuning.roundSeconds.ToString("0") + "s ROUND" : "RESUME", "RESTART ROUND",
                "INPUT: " + input.mode, "IMPACT: " + game.tuning.impactMode, "RECENTER", "HAPTICS: " + (game.feedback.hapticsEnabled ? "ON" : "OFF"), "SOUND: " + (game.feedback.soundEnabled ? "ON" : "OFF")};
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i].text = (i == Selection ? ">  " : "   ") + labels[i];
                rows[i].color = i == Selection ? new Color(0.25f, 0.95f, 1) : new Color(0.8f, 0.85f, 0.92f);
            }
            hint.text = (UsesGaze ? "LOOK AT AN OPTION FOR 1.5s\nHANDS: OPEN BOTH FOR 1.2s TO PAUSE" :
                input.mode == BoxingInputMode.Desktop ? "ARROWS: SELECT   ENTER: CONFIRM\nESC: PAUSE   Q / E: PUNCH   SPACE: GUARD" :
                "EITHER STICK: SELECT   A / X: CONFIRM\nMENU / B / Y: PAUSE") + "\nCLEAR YOUR PLAY AREA - DO NOT HIT REAL OBJECTS";
        }
    }
}
