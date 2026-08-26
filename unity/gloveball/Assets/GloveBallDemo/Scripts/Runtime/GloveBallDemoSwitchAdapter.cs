using Hapbeat;
using Hapbeat.DemoSwitch;
using UnityEngine;

namespace GloveBallDemo.Runtime
{
    internal static class GloveBallDemoSwitchAdapter
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            DemoSwitch.BeforeSwitch -= StopHaptics;
            DemoSwitch.BeforeSwitch += StopHaptics;
        }

        private static void StopHaptics(string nextDemoId)
        {
            HapbeatManager.Instance?.StopAll();
        }
    }
}
