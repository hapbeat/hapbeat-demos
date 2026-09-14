using GloveBallDemo.Core;
using UnityEngine;

namespace GloveBallDemo.Runtime
{
    public enum DemoPlayMode { Waves, EndlessRandom }

    /// <summary>
    /// Owns the run: ticks the phase machine, turns the launch planner's orders into shots, and
    /// funnels every gameplay outcome into the score model, the scoreboard and the haptic relay.
    /// </summary>
    public class DemoGameController : MonoBehaviour
    {
        [SerializeField] private BallLauncher[] _launchers;
        [SerializeField] private BallPool _pool;
        [SerializeField] private PlayerHitZone _hitZone;
        [SerializeField] private ScoreboardPresenter _scoreboard;
        [SerializeField] private TargetLayoutField _targetLayout;
        [Header("Play Mode")]
        [SerializeField] private DemoPlayMode _playMode = DemoPlayMode.Waves;
        [SerializeField] private int _launchSeed = 20260820;
        [Header("Rounds")]
        [Min(1)] [SerializeField] private int _roundCount = 3;
        [Min(1f)] [SerializeField] private float _roundDuration = 30f;
        [Min(0f)] [SerializeField] private float _roundIntermission = 3f;
        [Range(1, TargetLayoutPlanner.MaximumTargetCount)] [SerializeField] private int _targetMinCount = 3;
        [Range(1, TargetLayoutPlanner.MaximumTargetCount)] [SerializeField] private int _targetMaxCount = 4;
        [Tooltip("Min/max launch interval in the first and final round; intermediate rounds interpolate.")]
        [SerializeField] private Vector2 _firstRoundLaunchInterval = new Vector2(1.25f, 1.65f);
        [SerializeField] private Vector2 _finalRoundLaunchInterval = new Vector2(0.55f, 0.85f);
        [Tooltip("Min/max ball speed in metres per second in the first and final round.")]
        [SerializeField] private Vector2 _firstRoundBallSpeed = new Vector2(13f, 15f);
        [SerializeField] private Vector2 _finalRoundBallSpeed = new Vector2(17f, 21f);
        [Tooltip("Height above the floor the launchers aim at, in metres.")]
        [SerializeField] private float _aimHeight = 1.25f;
        [Tooltip("Extra height for lobbed shots so they drop in from above.")]
        [SerializeField] private float _lobExtraHeight = 0.3f;
        [Header("Endless Random")]
        [SerializeField] private int _endlessSeed = 20260825;
        [Tooltip("Seconds between random endless shots.")]
        [SerializeField] private float _endlessMinInterval = 0.45f;
        [SerializeField] private float _endlessMaxInterval = 1.25f;
        [Tooltip("Metres per second for random endless shots.")]
        [SerializeField] private float _endlessMinSpeed = 13f;
        [SerializeField] private float _endlessMaxSpeed = 19f;
        [SerializeField] private int _endlessMaxActiveBalls = 16;
        [SerializeField] private bool _logProgress = true;

        public static DemoGameController Instance { get; private set; }

        private GamePhaseMachine _phase;
        private LaunchPlanner _planner;
        private EndlessLaunchPlanner _endlessPlanner;
        private ScoreModel _score;
        private DemoPlayMode _activePlayMode;

        // Run statistics, surfaced so batch smoke runs can prove the loop actually turned over.
        public int LaunchedCount { get; private set; }
        public int CaughtCount { get; private set; }
        public int ThrownCount { get; private set; }
        public int PlayerHitCount { get; private set; }
        public int TargetHitCount { get; private set; }
        public int LauncherHitCount { get; private set; }

        public GamePhaseMachine Phase => _phase;
        public ScoreModel Score => _score;
        public BallPool Pool => _pool;
        public DemoPlayMode PlayMode => _playMode;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Demo] a second DemoGameController was destroyed");
                Destroy(this);
                return;
            }

            Instance = this;
            _planner = new LaunchPlanner(_launchSeed);
            _score = new ScoreModel();
            ResetWavePhase();
            _activePlayMode = _playMode;
            CreateEndlessPlanner();
        }

        private void Start()
        {
            if (_launchers != null && _pool != null)
            {
                foreach (var launcher in _launchers)
                {
                    if (launcher != null)
                    {
                        launcher.Bind(_pool);
                    }
                }
            }

            if (_scoreboard != null)
            {
                _scoreboard.Refresh(_score, _phase, _playMode);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (_activePlayMode != _playMode)
            {
                ReinitializeRun();
            }

            var dt = Time.deltaTime;
            if (_playMode == DemoPlayMode.Waves)
            {
                _phase.Tick(dt);
                if (_phase.Phase == GamePhase.Wave)
                {
                    TickWave(dt);
                }
            }
            else
            {
                TickEndless(dt);
            }

            if (_scoreboard != null)
            {
                _scoreboard.Refresh(_score, _phase, _playMode);
            }
        }

        private void TickWave(float deltaTime)
        {
            if (_pool == null || _launchers == null || _launchers.Length == 0)
            {
                return;
            }

            if (!_planner.Tick(deltaTime, _pool.ActiveCount, out var order))
            {
                return;
            }

            var launcher = PickLauncher(order.LauncherIndex);
            if (launcher == null)
            {
                return;
            }

            launcher.RequestFire(BuildAimPoint(order), order.Speed, order.Aim);
        }

        private void TickEndless(float deltaTime)
        {
            if (_pool == null || _launchers == null || _launchers.Length == 0 || _endlessPlanner == null)
            {
                return;
            }

            if (!_endlessPlanner.Tick(deltaTime, _pool.ActiveCount, _endlessMaxActiveBalls, out var order))
            {
                return;
            }

            var launcher = PickLauncher(order.LauncherIndex);
            if (launcher != null && launcher.RequestFire(BuildAimPoint(order), order.Speed, order.Aim))
            {
                _endlessPlanner.Commit();
            }
        }

        /// <summary>Falls forward through the launcher list when the planned one is stunned or busy.</summary>
        private BallLauncher PickLauncher(int preferredIndex)
        {
            for (var i = 0; i < _launchers.Length; i++)
            {
                var launcher = _launchers[(preferredIndex + i) % _launchers.Length];
                if (launcher != null && launcher.CanFire)
                {
                    return launcher;
                }
            }

            return null;
        }

        private Vector3 BuildAimPoint(LaunchOrder order)
        {
            var basePoint = _hitZone != null ? _hitZone.AimPoint : transform.position;
            basePoint.y = _aimHeight;

            // Both spatial offsets use the player's horizontal frame, so yaw rotates the whole
            // incoming pattern rather than leaving a hidden world-space bias.
            var forward = Vector3.forward;
            var right = Vector3.right;
            if (_hitZone != null && _hitZone.Head != null)
            {
                var headForward = _hitZone.Head.forward;
                headForward.y = 0f;
                if (headForward.sqrMagnitude > 1e-4f)
                {
                    forward = headForward.normalized;
                }

                var headRight = _hitZone.Head.right;
                headRight.y = 0f;
                if (headRight.sqrMagnitude > 1e-4f)
                {
                    right = headRight.normalized;
                }
            }

            var point = LaunchAimPoint.Apply(basePoint, forward, right, order.LateralOffset, order.DepthOffset);
            if (order.Aim == AimKind.Lob)
            {
                point.y += _lobExtraHeight;
            }

            return point;
        }

        private void OnPhaseChanged(GamePhase previous, GamePhase next)
        {
            if (_playMode != DemoPlayMode.Waves)
            {
                return;
            }
            switch (next)
            {
                case GamePhase.Countdown:
                    ResetRun();
                    break;
                case GamePhase.Wave:
                    _planner.BeginWave(GetRoundDefinition(_phase.WaveIndex), _launchers?.Length ?? 0);
                    _targetLayout?.BeginWave(_phase.WaveIndex, _targetMinCount, _targetMaxCount);
                    HapticEventRelay.Report(DemoHapticEvent.WaveStarted, PlayerPosition);
                    break;
                case GamePhase.Interlude:
                    HapticEventRelay.Report(DemoHapticEvent.WaveCleared, PlayerPosition);
                    break;
                case GamePhase.Result:
                    HapticEventRelay.Report(DemoHapticEvent.GameOver, PlayerPosition);
                    ClearCourt("result");
                    break;
            }

            if (_logProgress)
            {
                Debug.Log(
                    $"[Demo] phase {previous} -> {next} (wave {_phase.WaveIndex + 1}) " +
                    $"launched={LaunchedCount} caught={CaughtCount} thrown={ThrownCount} " +
                    $"playerHits={PlayerHitCount} targetHits={TargetHitCount} launcherHits={LauncherHitCount} " +
                    $"score={_score.Score} " +
                    $"pool(active={(_pool != null ? _pool.ActiveCount : -1)}, taken={(_pool != null ? _pool.TotalTaken : -1)}, returned={(_pool != null ? _pool.TotalReturned : -1)})");
            }
        }

        private void ResetRun()
        {
            _score.Reset();
            LaunchedCount = 0;
            CaughtCount = 0;
            ThrownCount = 0;
            PlayerHitCount = 0;
            TargetHitCount = 0;
            LauncherHitCount = 0;
            ClearCourt("new-run");

            if (_launchers == null)
            {
                return;
            }

            foreach (var launcher in _launchers)
            {
                if (launcher != null)
                {
                    launcher.ResetLauncher();
                }
            }
        }

        public void SetPlayMode(DemoPlayMode mode)
        {
            _playMode = mode;
            ReinitializeRun();
        }

        public void RestartRun() => ReinitializeRun();

        private void ReinitializeRun()
        {
            _activePlayMode = _playMode;
            ResetRun();
            if (_playMode == DemoPlayMode.Waves)
            {
                ResetWavePhase();
                _planner = new LaunchPlanner(_launchSeed);
            }
            else
            {
                CreateEndlessPlanner();
                _targetLayout?.BeginWave(-1, _targetMinCount, _targetMaxCount);
            }
        }

        private void ResetWavePhase()
        {
            if (_phase != null)
            {
                _phase.PhaseChanged -= OnPhaseChanged;
            }

            var roundCount = Mathf.Max(1, _roundCount);
            _phase = new GamePhaseMachine(roundCount, _ => Mathf.Max(1f, _roundDuration), Mathf.Max(0f, _roundIntermission));
            _phase.PhaseChanged += OnPhaseChanged;
        }

        private WaveDefinition GetRoundDefinition(int roundIndex)
        {
            var denominator = Mathf.Max(1, _roundCount - 1);
            var t = Mathf.Clamp01(roundIndex / (float)denominator);
            var interval = Vector2.Lerp(_firstRoundLaunchInterval, _finalRoundLaunchInterval, t);
            var speed = Vector2.Lerp(_firstRoundBallSpeed, _finalRoundBallSpeed, t);
            var feel = Resources.Load<BallFeelSettings>("BallFeelSettings");
            if (feel != null) speed *= Mathf.Lerp(feel.FirstRoundSpeedMultiplier, feel.FinalRoundSpeedMultiplier, t);
            return new WaveDefinition(
                Mathf.Max(1f, _roundDuration),
                Mathf.Max(0.05f, Mathf.Min(interval.x, interval.y)),
                Mathf.Max(0.05f, Mathf.Max(interval.x, interval.y)),
                Mathf.Max(0.1f, Mathf.Min(speed.x, speed.y)),
                Mathf.Max(0.1f, Mathf.Max(speed.x, speed.y)),
                Mathf.RoundToInt(Mathf.Lerp(5f, 9f, t)),
                _launchers != null ? _launchers.Length : 3,
                Mathf.Lerp(0.70f, 0.60f, t));
        }

        private void CreateEndlessPlanner()
        {
            _endlessPlanner = new EndlessLaunchPlanner(
                _endlessSeed,
                _endlessMinInterval,
                _endlessMaxInterval,
                _endlessMinSpeed,
                _endlessMaxSpeed);
            _endlessPlanner.Begin(_launchers != null ? _launchers.Length : 0);
        }

        private void ClearCourt(string reason)
        {
            if (_pool != null)
            {
                _pool.ReturnAll(reason);
            }
        }

        private Vector3 PlayerPosition => _hitZone != null ? _hitZone.AimPoint : transform.position;

        // ------------------------------------------------------- gameplay events

        public void OnBallLaunched(BallLauncher launcher, Ball ball, AimKind aim)
        {
            LaunchedCount++;
            if (_logProgress)
            {
                Debug.Log($"[Demo] launch #{LaunchedCount} from {launcher.name} aim={aim} active={_pool.ActiveCount}");
            }
        }

        public void OnBallCaught(GloveController glove, Ball ball)
        {
            CaughtCount++;
            _score.RegisterCatch();
        }

        public void OnBallThrown(GloveController glove, Ball ball, float speed)
        {
            ThrownCount++;
            if (_logProgress)
            {
                Debug.Log(
                    $"[Demo] throw #{ThrownCount} from {ball.transform.position} " +
                    $"v={ball.Body.linearVelocity} speed={speed:0.0}");
            }

        }

        public void OnPlayerHit(Ball ball)
        {
            PlayerHitCount++;
            _score.RegisterPlayerHit();
            HapticEventRelay.ReportBallImpact(ball, DemoHapticEvent.BodyCollide, ball.transform.position);
        }

        public void OnTargetHit(TargetPanel panel, Ball ball)
        {
            TargetHitCount++;
            _score.RegisterTargetHit();
            HapticEventRelay.Report(DemoHapticEvent.TargetHit, ball.transform.position);
        }

        public void OnLauncherHit(BallLauncher launcher, Ball ball)
        {
            LauncherHitCount++;
            _score.RegisterLauncherHit();
            HapticEventRelay.Report(DemoHapticEvent.LauncherHit, ball.transform.position);

            if (launcher.RegisterReturnHit())
            {
                HapticEventRelay.Report(DemoHapticEvent.LauncherStunned, launcher.MuzzlePosition);
            }
        }
    }
}
