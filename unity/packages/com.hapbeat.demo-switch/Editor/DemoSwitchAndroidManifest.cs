#if UNITY_ANDROID
using System;
using System.IO;
using UnityEditor.Android;
using UnityEngine;

namespace Hapbeat.DemoSwitch.Editor
{
    /// <summary>
    /// Generated-manifest additions: <c>CHANGE_WIFI_MULTICAST_STATE</c> while the receiver is enabled (the
    /// 7710 listener holds a <c>WifiManager.MulticastLock</c> so broadcast DISCOVER is not filtered), and,
    /// for Android 11+ package visibility, a package query for the Hub's package when a demo has Pause Menu
    /// enabled (the shared pause menu shows "Hub に戻る" only when the Hub is installed).
    /// </summary>
    public sealed class DemoSwitchAndroidManifest : IPostGenerateGradleAndroidProject
    {
        internal const string MulticastPermission = "android.permission.CHANGE_WIFI_MULTICAST_STATE";

        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var settings = Resources.Load<DemoSwitchSettings>(DemoSwitchSettings.ResourceName);
            if (settings == null) return;
            var manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            var text = File.ReadAllText(manifest);
            if (settings.ReceiverEnabled) text = AddPermission(text, MulticastPermission, manifest);
            if (settings.PauseMenu && settings.CurrentDemoId != DemoSwitchSettings.HubDemoId
                && settings.TryResolveTarget(DemoSwitchSettings.HubDemoId, out var hub))
                text = AddPackageQuery(text, hub.PackageName, manifest);
            File.WriteAllText(manifest, text);
        }

        internal static string AddPermission(string manifest, string permission, string sourceName = "manifest")
        {
            var element = "<uses-permission android:name=\"" + permission + "\" />";
            if (manifest.Contains("android:name=\"" + permission + "\"")) return manifest;
            var application = manifest.IndexOf("<application", StringComparison.Ordinal);
            if (application < 0) throw new InvalidOperationException("No <application> in " + sourceName);
            return manifest.Insert(application, element + "\n  ");
        }

        internal static string AddPackageQuery(string manifest, string packageName, string sourceName = "manifest")
        {
            var query = "<queries><package android:name=\"" + packageName + "\" /></queries>";
            if (manifest.Contains(query)) return manifest;
            var application = manifest.IndexOf("<application", StringComparison.Ordinal);
            if (application < 0) throw new InvalidOperationException("No <application> in " + sourceName);
            return manifest.Insert(application, query + "\n  ");
        }
    }
}
#endif
