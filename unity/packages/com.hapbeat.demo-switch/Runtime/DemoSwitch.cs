using System;
using UnityEngine;

namespace Hapbeat.DemoSwitch
{
    public static class DemoSwitch
    {
        public static event Action<string> BeforeSwitch;
        public static event Action LaunchContextDetected;

        public static bool ReturnToHub() => SwitchTo(DemoSwitchSettings.HubDemoId);

        public static bool SwitchTo(string demoId)
        {
            if (DemoSwitchRuntime.Instance == null)
            {
                Debug.LogError("[Demo Switch] No Resources/HapbeatDemoSwitchSettings asset was loaded.");
                return false;
            }

            return DemoSwitchRuntime.Instance.SwitchLocal(demoId);
        }

        internal static void NotifyBeforeSwitch(string demoId)
        {
            var handlers = BeforeSwitch;
            if (handlers == null) return;
            foreach (Action<string> handler in handlers.GetInvocationList())
            {
                try { handler(demoId); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        internal static void NotifyLaunchContextDetected()
        {
            var handlers = LaunchContextDetected;
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                try { handler(); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }
    }
}
