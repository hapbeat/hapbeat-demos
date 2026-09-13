using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>Physical panel that reports one thrown-ball hit, flashes, then yields to its layout.</summary>
    public class TargetPanel : MonoBehaviour
    {
        [SerializeField] private Renderer _renderer;
        [SerializeField] private Color _idleColor = new Color(0.15f, 0.45f, 0.75f);
        [SerializeField] private Color _hitColor = new Color(1f, 0.85f, 0.25f);
        [Min(0f)] [SerializeField] private float _flashDuration = 0.2f;
        [Header("Hit volume")]
        [Tooltip("Pass-through trigger used for thrown-ball hit detection.")]
        [SerializeField] private MeshCollider _hitVolume;
        [Tooltip("Local offset of the hit trigger from the target centre.")]
        [SerializeField] private Vector3 _hitVolumeOffset = Vector3.zero;
        [Tooltip("Local scale of the cylinder trigger. X/Z control radius; Y controls depth.")]
        [SerializeField] private Vector3 _hitVolumeScale = new Vector3(0.7f, 0.1f, 0.7f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private MaterialPropertyBlock _block;
        private float _flashTimer;
        private bool _hitPending;
        private bool _flashCompleted;

        public event System.Action<TargetPanel> HitFlashCompleted;

        private void Awake()
        {
            if (_renderer == null)
            {
                _renderer = GetComponentInChildren<Renderer>();
            }

            ApplyHitVolumeSettings();

            ApplyColor(_idleColor);
        }

        private void Update()
        {
            TickFlash(Time.deltaTime);
        }

        private void TickFlash(float deltaTime)
        {
            if (!_hitPending)
            {
                return;
            }

            _flashTimer -= Mathf.Max(0f, deltaTime);
            if (_flashTimer > 0f || _flashCompleted) return;
            _flashCompleted = true;
            HitFlashCompleted?.Invoke(this);
        }

        private void OnTriggerEnter(Collider other)
        {
            TryRegisterHit(other.GetComponentInParent<Ball>());
        }

        private bool TryRegisterHit(Ball ball)
        {
            if (_hitPending || ball == null || ball.State != BallState.Thrown) return false;
            _hitPending = true;
            _flashCompleted = false;
            _flashTimer = _flashDuration;
            ApplyColor(_hitColor);
            DemoGameController.Instance?.OnTargetHit(this, ball);
            if (_flashTimer <= 0f)
            {
                _flashCompleted = true;
                HitFlashCompleted?.Invoke(this);
            }
            return true;
        }

        public void ResetForGeneration()
        {
            _hitPending = false;
            _flashCompleted = false;
            _flashTimer = 0f;
            ApplyColor(_idleColor);
        }

        private void OnValidate() => ApplyHitVolumeSettings();

        private void ApplyHitVolumeSettings()
        {
            if (_hitVolume == null) _hitVolume = GetComponentInChildren<MeshCollider>(true);
            if (_hitVolume == null) return;
            var hitTransform = _hitVolume.transform;
            if ((hitTransform.localPosition - _hitVolumeOffset).sqrMagnitude > 1e-8f)
                hitTransform.localPosition = _hitVolumeOffset;
            if ((hitTransform.localScale - _hitVolumeScale).sqrMagnitude > 1e-8f)
                hitTransform.localScale = _hitVolumeScale;
        }

        private void ApplyColor(Color color)
        {
            // Layout generation can reset an inactive target before its first Awake.
            if (_renderer == null) _renderer = GetComponentInChildren<Renderer>(true);
            if (_renderer == null)
            {
                return;
            }

            _block ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            _renderer.SetPropertyBlock(_block);
        }
    }
}
