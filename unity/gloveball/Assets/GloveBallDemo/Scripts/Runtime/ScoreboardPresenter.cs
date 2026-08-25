using GloveBallDemo.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloveBallDemo.Runtime
{
    /// <summary>
    /// World-space readout above the enemy court. English only: the demo runs at public
    /// exhibitions with international visitors.
    /// </summary>
    public class ScoreboardPresenter : MonoBehaviour
    {
        [SerializeField] private Text _scoreText;
        [SerializeField] private Text _comboText;
        [SerializeField] private Text _waveText;
        [SerializeField] private Text _messageText;

        public void Refresh(ScoreModel score, GamePhaseMachine phase, DemoPlayMode playMode)
        {
            if (score == null)
            {
                return;
            }

            SetText(_scoreText, $"SCORE  {score.Score:0000000}");
            SetText(_comboText, score.Combo > 0 ? $"COMBO {score.Combo}  x{score.Multiplier}" : "COMBO -");
            if (playMode == DemoPlayMode.EndlessRandom)
            {
                SetText(_waveText, "ENDLESS RANDOM");
                SetText(_messageText, "GRIP HOLD / PHYSICAL RELEASE  •  TRIGGER CHARGE / RELEASE");
                return;
            }

            if (phase == null)
            {
                return;
            }

            SetText(_waveText, $"ROUND {phase.WaveIndex + 1}/{phase.WaveCount}   {Mathf.Max(0f, phase.PhaseRemaining):00.0}s");
            SetText(_messageText, BuildMessage(score, phase));
        }

        private static string BuildMessage(ScoreModel score, GamePhaseMachine phase)
        {
            switch (phase.Phase)
            {
                case GamePhase.Countdown:
                    return $"READY?  {Mathf.CeilToInt(Mathf.Max(0f, phase.PhaseRemaining))}";
                case GamePhase.Wave:
                    return $"ROUND {phase.WaveIndex + 1}  •  THROW AT BULLSEYES";
                case GamePhase.Interlude:
                    return "ROUND CLEAR!";
                case GamePhase.Result:
                    return $"COMPLETE!   {score.Score}";
                default:
                    return string.Empty;
            }
        }

        private static void SetText(Text target, string value)
        {
            if (target != null && target.text != value)
            {
                target.text = value;
            }
        }
    }
}
