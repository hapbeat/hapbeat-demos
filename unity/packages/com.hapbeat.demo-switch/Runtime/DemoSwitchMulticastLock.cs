using UnityEngine;

namespace Hapbeat.DemoSwitch
{
    /// <summary>
    /// Android <c>WifiManager.MulticastLock</c>, held while the 7710 listener runs so the Wi-Fi driver does not
    /// filter the broadcast <c>DISCOVER</c> (contracts: Transport and lifecycle). Needs
    /// <c>CHANGE_WIFI_MULTICAST_STATE</c>, which <c>DemoSwitchAndroidManifest</c> adds. No-op elsewhere.
    /// </summary>
    internal sealed class DemoSwitchMulticastLock
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _lock;
#endif

        public void Acquire()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_lock != null) return;
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var context = activity.Call<AndroidJavaObject>("getApplicationContext"))
                using (var wifi = context.Call<AndroidJavaObject>("getSystemService", "wifi"))
                {
                    var multicastLock = wifi.Call<AndroidJavaObject>("createMulticastLock", "hapbeat-demo-switch");
                    multicastLock.Call("setReferenceCounted", false);
                    multicastLock.Call("acquire");
                    _lock = multicastLock;
                }
                Debug.Log("[Demo Switch] Wi-Fi multicast lock acquired.");
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[Demo Switch] Could not acquire the Wi-Fi multicast lock (broadcast DISCOVER may be dropped): " + exception.Message);
            }
#endif
        }

        public void Release()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_lock == null) return;
            try
            {
                if (_lock.Call<bool>("isHeld")) _lock.Call("release");
                Debug.Log("[Demo Switch] Wi-Fi multicast lock released.");
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[Demo Switch] Could not release the Wi-Fi multicast lock: " + exception.Message);
            }
            finally
            {
                _lock.Dispose();
                _lock = null;
            }
#endif
        }
    }
}
