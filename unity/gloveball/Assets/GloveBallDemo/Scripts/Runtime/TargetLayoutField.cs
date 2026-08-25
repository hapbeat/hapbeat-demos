using GloveBallDemo.Core;
using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>Owns the active target generation and its court-constrained seeded layouts.</summary>
    public sealed class TargetLayoutField : MonoBehaviour
    {
        [SerializeField] private TargetPanel[] _targets;
        [SerializeField] private Transform _player;
        [SerializeField] private int _layoutSeed = 20260824;
        [Header("Random target placement bounds (world space)")]
        [Tooltip("Left-most target centre in world-space X.")]
        [SerializeField] private float _minX = -3.7f;
        [Tooltip("Right-most target centre in world-space X.")]
        [SerializeField] private float _maxX = 3.7f;
        [InspectorName("Near Depth (Minimum World Z)")]
        [Tooltip("Closest allowed target depth. Increase this value to keep random targets farther from the player.")]
        [SerializeField] private float _minZ = 0f;
        [InspectorName("Far Depth (Maximum World Z)")]
        [Tooltip("Farthest allowed target depth in world-space Z.")]
        [SerializeField] private float _maxZ = 8.1f;
        [InspectorName("Minimum Height")]
        [Tooltip("Lowest target centre height above the arena floor, in world metres.")]
        [SerializeField] private float _minHeight = 0.7f;
        [InspectorName("Maximum Height")]
        [Tooltip("Highest target centre height above the arena floor, in world metres.")]
        [SerializeField] private float _maxHeight = 3.2f;
        [Tooltip("Minimum world-space distance between target centres.")]
        [Min(0f)] [SerializeField] private float _minimumSpacing = 2f;

        private int _activeTargetCount;
        private int _waveIndex;
        private int _generationIndex;
        private int _minimumTargets;
        private int _maximumTargets;

        public int TargetCount => _targets != null ? _targets.Length : 0;
        public int ActiveTargetCount => _activeTargetCount;

        public bool HasDistinctPositions
        {
            get
            {
                if (_targets == null || _activeTargetCount < 1) return false;
                for (var i = 0; i < _targets.Length; i++)
                {
                    if (_targets[i] == null || !_targets[i].gameObject.activeSelf) continue;
                    for (var j = i + 1; j < _targets.Length; j++)
                        if (_targets[j] != null && _targets[j].gameObject.activeSelf &&
                            Vector3.Distance(_targets[i].transform.position, _targets[j].transform.position) < _minimumSpacing)
                            return false;
                }
                return true;
            }
        }

        public void BeginWave(int waveIndex, int minimumTargets = 3, int maximumTargets = 4)
        {
            if (!IsWired()) return;
            foreach (var target in _targets)
            {
                if (target == null) continue;
                target.HitFlashCompleted -= OnHitFlashCompleted;
                target.HitFlashCompleted += OnHitFlashCompleted;
            }
            _waveIndex = waveIndex;
            _generationIndex = 0;
            _minimumTargets = Mathf.Clamp(minimumTargets, 1, _targets.Length);
            _maximumTargets = Mathf.Clamp(maximumTargets, _minimumTargets, _targets.Length);
            CreateGeneration();
        }

        /// <summary>Consumes a panel only after TargetPanel has completed its hit flash.</summary>
        public bool RegisterHit(TargetPanel panel)
        {
            if (panel == null || !panel.gameObject.activeSelf || _targets == null || System.Array.IndexOf(_targets, panel) < 0)
                return false;

            panel.gameObject.SetActive(false);
            _activeTargetCount--;
            if (_activeTargetCount <= 0)
            {
                _generationIndex++;
                CreateGeneration();
            }
            return true;
        }

        private void OnHitFlashCompleted(TargetPanel panel) => RegisterHit(panel);

        private void CreateGeneration()
        {
            var seed = unchecked(_layoutSeed + (_waveIndex * 7919) + (_generationIndex * 104729));
            var random = new System.Random(seed);
            var targetCount = random.Next(_minimumTargets, _maximumTargets + 1);
            var min = new Vector3(Mathf.Min(_minX, _maxX), Mathf.Min(_minHeight, _maxHeight), Mathf.Min(_minZ, _maxZ));
            var max = new Vector3(Mathf.Max(_minX, _maxX), Mathf.Max(_minHeight, _maxHeight), Mathf.Max(_minZ, _maxZ));
            var bounds = new Bounds((min + max) * .5f, max - min);
            var slots = new TargetLayoutPlanner(seed).Plan(_player.position, bounds, _minimumSpacing, targetCount);

            _activeTargetCount = targetCount;
            for (var i = 0; i < _targets.Length; i++)
            {
                var active = i < targetCount;
                if (active)
                {
                    _targets[i].transform.SetPositionAndRotation(slots[i].Position, slots[i].Rotation);
                    _targets[i].ResetForGeneration();
                }
                _targets[i].gameObject.SetActive(active);
            }
        }

        private bool IsWired()
        {
            if (_targets != null && _targets.Length > 0 && _player != null) return true;
            Debug.LogError("[TargetLayout] scene wiring requires the configured targets and player transform");
            return false;
        }
    }
}
