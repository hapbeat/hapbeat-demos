using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace GloveBallDemo.Runtime
{
    /// <summary>
    /// Chooses a real OpenXR HMD when one is present, otherwise starts the keyboard/mouse
    /// XR Device Simulator. The simulator begins inactive so its fake HMD cannot win the generic
    /// Input System bindings before Horizon Link has registered the real device.
    /// </summary>
    [DefaultExecutionOrder(-31000)]
    public sealed class RealHmdSimulatorGate : MonoBehaviour
    {
        [SerializeField] private GameObject _simulator;

#if UNITY_EDITOR
        private const float HmdRegistrationTimeoutSeconds = 2f;

        /// <summary>Called by DemoSceneBuilder; generated scenes must not hand-wire this field.</summary>
        public void EditorSetup(GameObject simulator)
        {
            _simulator = simulator;
            if (_simulator != null)
            {
                _simulator.SetActive(false);
            }
        }

        private IEnumerator Start()
        {
            if (_simulator == null)
            {
                Debug.LogError("[XrRig] RealHmdSimulatorGate has no simulator assigned");
                yield break;
            }

            // Input devices can appear a few frames after the OpenXR loader completes. Because
            // the simulator is still inactive, any XRHMD observed here is necessarily hardware.
            var deadline = Time.realtimeSinceStartup + HmdRegistrationTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                var realHmd = InputSystem.GetDevice<XRHMD>();
                if (realHmd != null)
                {
                    Debug.Log($"[XrRig] real HMD detected: {realHmd.displayName}; " +
                              "keyboard/mouse XR Device Simulator remains off");
                    yield break;
                }

                yield return null;
            }

            _simulator.SetActive(true);
            Debug.Log("[XrRig] no real HMD detected; keyboard/mouse XR Device Simulator enabled");
        }
#endif
    }
}
