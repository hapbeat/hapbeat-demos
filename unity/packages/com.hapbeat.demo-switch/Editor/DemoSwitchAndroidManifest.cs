#if UNITY_ANDROID
using System;
using System.IO;
using UnityEditor.Android;
using UnityEngine;

namespace Hapbeat.DemoSwitch.Editor
{
    /// <summary>
    /// Android 11+ package visibility: the shared pause menu shows "Hub に戻る" only when the Hub is
    /// installed, so a demo with Pause Menu enabled declares a package query for the Hub's package
    /// (merged into the app manifest).
    /// </summary>
    public sealed class DemoSwitchAndroidManifest : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var settings = Resources.Load<DemoSwitchSettings>(DemoSwitchSettings.ResourceName);
            if (settings == null || !settings.PauseMenu || settings.CurrentDemoId == DemoSwitchSettings.HubDemoId) return;
            if (!settings.TryResolveTarget(DemoSwitchSettings.HubDemoId, out var hub)) return;
            var manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            File.WriteAllText(manifest, AddPackageQuery(File.ReadAllText(manifest), hub.PackageName, manifest));
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
