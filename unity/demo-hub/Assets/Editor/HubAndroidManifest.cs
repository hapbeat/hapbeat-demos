using System;
using System.IO;
using UnityEditor.Android;

namespace Hapbeat.DemoHub.Editor
{
    /// <summary>
    /// Android 11+ package visibility: the Hub enumerates launcher activities and reads other APKs'
    /// assets, so its manifest declares a MAIN/LAUNCHER intent query (merged into the app manifest).
    /// </summary>
    public sealed class HubAndroidManifest : IPostGenerateGradleAndroidProject
    {
        public const string Queries = "<queries><intent><action android:name=\"android.intent.action.MAIN\" /><category android:name=\"android.intent.category.LAUNCHER\" /></intent></queries>";

        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            var text = File.ReadAllText(manifest);
            if (text.Contains("android.intent.category.LAUNCHER\" /></intent></queries>")) return;
            var application = text.IndexOf("<application", StringComparison.Ordinal);
            if (application < 0) throw new InvalidOperationException("No <application> in " + manifest);
            File.WriteAllText(manifest, text.Insert(application, Queries + "\n  "));
        }
    }
}
