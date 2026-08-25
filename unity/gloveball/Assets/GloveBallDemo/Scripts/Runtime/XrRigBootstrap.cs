using UnityEngine;
using UnityEngine.InputSystem;

namespace GloveBallDemo.Runtime
{
    /// <summary>
    /// Enables the demo's input actions at startup. The rig itself is built by
    /// DemoSceneBuilder; this only makes sure the actions are live, and reports whether an
    /// HMD is present so a desk run is obviously a desk run in the log.
    /// </summary>
    public class XrRigBootstrap : MonoBehaviour
    {
        [SerializeField] private InputActionAsset _actions;
        [SerializeField] private Transform _head;

        private void Awake()
        {
            if (_actions == null)
            {
                Debug.LogWarning("[XrRig] no InputActionAsset assigned; pose tracking will stay at the authored transform");
                return;
            }

            _actions.Enable();
        }

        private void Start()
        {
            var hmd = InputSystem.GetDevice<UnityEngine.InputSystem.XR.XRHMD>();
            Debug.Log(hmd != null
                ? $"[XrRig] HMD detected: {hmd.displayName}"
                : "[XrRig] no HMD present; the rig stays at its authored transform (expected for batch runs)");

            if (_head == null)
            {
                Debug.LogWarning("[XrRig] no head transform assigned");
            }
        }
    }
}
