using UnityEngine;
using UnityEngine.UI;

namespace Hapbeat.Boxing
{
    public sealed class BoxingPresentation : MonoBehaviour
    {
        public Transform leftGlove, rightGlove;
        public Transform enemyHead, enemyTorso, enemyHip, enemyLeftGlove, enemyRightGlove;
        public Transform[] enemyArms, enemyLegs;
        public Text timerText, scoreText, cueText, statusText, impactText;
        public Image impactBorder;
        public Transform hitBurst;
        public Image PlayerHealthBar { get; private set; }
        public Image EnemyHealthBar { get; private set; }
        private Text playerHealthText, enemyHealthText;
        private float flashTime;
        private string lastImpact = "";
        public void Flash(BoxingImpact impact)
        {
            flashTime = 0.28f;
            lastImpact = (impact.attack ? "HIT" : impact.zone == ImpactZone.Head ? "HEAD HIT" : "BLOCK") +
                "  " + (impact.hard ? "HARD" : "SOFT") + "  " + impact.relativeSpeed.ToString("0.0") + " m/s";
            if (hitBurst != null) { hitBurst.position = impact.point; hitBurst.gameObject.SetActive(true); }
            if (impactBorder != null) impactBorder.color = impact.zone == ImpactZone.Head ? new Color(1, 0.12f, 0.06f, 0.24f) : new Color(0.1f, 0.85f, 1, 0.13f);
        }
        public void Render(BoxingGame game, BoxerPose pose, bool valid)
        {
            bool menuOpen = game.menu != null && game.menu.IsOpen;
            timerText.transform.parent.gameObject.SetActive(!menuOpen);
            leftGlove.gameObject.SetActive(pose.valid && !menuOpen); rightGlove.gameObject.SetActive(pose.valid && !menuOpen);
            enemyHead.parent.gameObject.SetActive(!menuOpen);
            if (pose.valid)
            {
                leftGlove.SetPositionAndRotation(pose.left, pose.leftRotation);
                rightGlove.SetPositionAndRotation(pose.right, pose.rightRotation);
            }
            var enemy = game.Opponent;
            enemyHead.position = enemy.Head; enemyTorso.position = enemy.Body;
            enemyHip.position = enemy.Root + Vector3.up * (enemy.Head.y - 0.79f);
            enemyLeftGlove.SetPositionAndRotation(enemy.Left, Quaternion.LookRotation((pose.head - enemy.Left).normalized));
            enemyRightGlove.SetPositionAndRotation(enemy.Right, Quaternion.LookRotation((pose.head - enemy.Right).normalized));
            DrawArm(enemyArms[0], enemyArms[1], enemy.Body + new Vector3(-0.23f, 0.19f, 0), enemy.Left, -1);
            DrawArm(enemyArms[2], enemyArms[3], enemy.Body + new Vector3(0.23f, 0.19f, 0), enemy.Right, 1);
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1 : 1;
                Vector3 hip = enemyHip.position + Vector3.right * (side * 0.13f);
                Vector3 foot = enemy.Root + new Vector3(side * 0.22f, 0.08f, side * 0.16f);
                Vector3 knee = Vector3.Lerp(hip, foot, 0.52f) + Vector3.back * 0.12f;
                Segment(enemyLegs[i * 3], hip, knee, 0.105f);
                Segment(enemyLegs[i * 3 + 1], knee, foot, 0.075f);
                enemyLegs[i * 3 + 2].position = foot + Vector3.back * 0.07f;
            }
            timerText.text = game.Round.Phase == BoxingPhase.Countdown ? Mathf.CeilToInt(game.Round.Countdown).ToString() :
                game.Round.Phase == BoxingPhase.Ready ? game.tuning.roundSeconds.ToString("0") + " SECOND ROUND" : Mathf.CeilToInt(game.Round.TimeLeft).ToString("00") + "s";
            scoreText.text = "SCORE " + game.Round.Score + "     HIT " + game.Round.Hits + "     BLOCK " + game.Round.Blocks + "     DODGE " + game.Round.Dodges;
            RenderHealth(game.Round);
            statusText.text = game.input.mode + "  |  " + (game.feedback.CanSend ? "HAPBEAT " + (Hapbeat.HapbeatManager.Instance != null ? Hapbeat.HapbeatManager.Instance.AliveDeviceCount : 0) + " DEVICE(S)" : "HAPTICS OFF") +
                "  |  " + (game.Round.Phase == BoxingPhase.Results ? (game.Round.PlayerHealth <= 0 ? "KO - OPPONENT WINS" : game.Round.EnemyHealth <= 0 ? "KO - YOU WIN" : "ROUND COMPLETE") : game.Paused ? game.PauseReason : game.tuning.impactMode.ToString());
            flashTime = Mathf.Max(0, flashTime - Time.unscaledDeltaTime);
            impactText.text = flashTime > 0 ? lastImpact : "";
            if (hitBurst != null)
            {
                hitBurst.gameObject.SetActive(flashTime > 0);
                hitBurst.localScale = Vector3.one * (0.06f + (0.28f - flashTime) * 1.4f);
            }
            if (impactBorder != null) { var c = impactBorder.color; c.a = Mathf.Min(c.a, flashTime * 0.6f); impactBorder.color = c; }
        }
        public void RenderHealth(BoxingRound round)
        {
            cueText.enabled = false;
            if (PlayerHealthBar == null)
            {
                PlayerHealthBar = CreateHealthBar("YOU", -285, new Color(0.15f, 0.85f, 1), out playerHealthText);
                EnemyHealthBar = CreateHealthBar("OPPONENT", 285, new Color(1, 0.25f, 0.15f), out enemyHealthText);
            }
            PlayerHealthBar.rectTransform.anchorMax = new Vector2(round.PlayerHealth / round.MaximumHealth, 1);
            EnemyHealthBar.rectTransform.anchorMax = new Vector2(round.EnemyHealth / round.MaximumHealth, 1);
            playerHealthText.text = "YOU  " + Mathf.CeilToInt(round.PlayerHealth) + " / " + Mathf.CeilToInt(round.MaximumHealth);
            enemyHealthText.text = "OPPONENT  " + Mathf.CeilToInt(round.EnemyHealth) + " / " + Mathf.CeilToInt(round.MaximumHealth);
        }
        private Image CreateHealthBar(string name, float x, Color color, out Text label)
        {
            var root = new GameObject(name + " HP", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(cueText.transform, false); root.anchoredPosition = new Vector2(x, 0); root.sizeDelta = new Vector2(520, 45);
            label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(root, false); label.font = cueText.font; label.fontSize = 20;
            label.alignment = TextAnchor.MiddleCenter; label.color = Color.white; label.raycastTarget = false;
            label.rectTransform.sizeDelta = new Vector2(520, 24); label.rectTransform.anchoredPosition = new Vector2(0, 12);
            var background = new GameObject("Track", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            background.transform.SetParent(root, false); background.color = new Color(0.08f, 0.1f, 0.14f); background.raycastTarget = false;
            background.rectTransform.sizeDelta = new Vector2(520, 16); background.rectTransform.anchoredPosition = new Vector2(0, -12);
            var fill = new GameObject("Health", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            fill.transform.SetParent(background.transform, false); fill.color = color; fill.raycastTarget = false;
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            return fill;
        }
        public static void Segment(Transform segment, Vector3 a, Vector3 b, float radius)
        {
            var delta = b - a;
            segment.position = (a + b) * 0.5f;
            segment.rotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
            segment.localScale = new Vector3(radius * 2, delta.magnitude * 0.5f, radius * 2);
        }
        private static void DrawArm(Transform upper, Transform lower, Vector3 shoulder, Vector3 hand, float side)
        {
            // Enemy-only articulated limbs; player's collision authority remains the three tracked points.
            Vector3 mid = (shoulder + hand) * 0.5f + new Vector3(side * 0.12f, -0.16f, 0.03f);
            Segment(upper, shoulder, mid, 0.075f); Segment(lower, mid, hand, 0.065f);
        }
    }
}
