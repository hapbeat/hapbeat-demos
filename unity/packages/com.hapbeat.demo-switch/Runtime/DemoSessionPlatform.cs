using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Hapbeat.DemoSwitch
{
    /// <summary>Installed demo whose APK carries a valid descriptor, resolved through PackageManager.</summary>
    public sealed class DemoSessionCatalogEntry
    {
        public DemoSessionCatalogEntry(DemoSessionDescriptor descriptor, string packageName, string activityName, string appLabel = null)
        {
            Descriptor = descriptor;
            PackageName = packageName;
            ActivityName = activityName;
            AppLabel = string.IsNullOrWhiteSpace(appLabel) ? null : appLabel.Trim();
        }

        public DemoSessionDescriptor Descriptor { get; }
        public string PackageName { get; }
        public string ActivityName { get; }
        /// <summary>PackageManager label of the launcher activity (the name in Quest's library), or null when unknown.</summary>
        public string AppLabel { get; }
        /// <summary>The application name for lists and step titles: <see cref="AppLabel"/>, else the descriptor's Japanese title.</summary>
        public string DisplayName => AppLabel ?? Descriptor.Title.Ja;
    }

    /// <summary>One MAIN/LAUNCHER activity with its PackageManager label (null when it could not be read).</summary>
    internal sealed class DemoSessionLauncherActivity
    {
        public DemoSessionLauncherActivity(DemoSessionComponent component, string label)
        {
            Component = component;
            Label = label;
        }

        public DemoSessionComponent Component { get; }
        public string Label { get; }
    }

    internal interface IDemoSessionPlatform
    {
        /// <summary>Reads a String extra (the ticket, the Hub's start request) from the current Intent and removes it.</summary>
        bool TryTakeStringExtra(string name, out string value);
        bool TryLaunch(string packageName, string activityName, string ticketJson, out string error);
        void FinishTask();
        bool TryReadOwnAsset(string name, out string text, out string error);
        bool TryGetOwnComponent(out DemoSessionComponent component);
        IReadOnlyList<DemoSessionLauncherActivity> ListLauncherActivities();
        bool TryReadPackageAsset(string packageName, string name, out string text, out string error);
        /// <summary>Whether PackageManager can see <paramref name="packageName"/> (Android 11+ needs a matching manifest query).</summary>
        bool IsPackageInstalled(string packageName);
    }

    internal static class DemoSessionPlatform
    {
        public const string UnityActivity = "com.unity3d.player.UnityPlayerGameActivity";

        public static IDemoSessionPlatform Create()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidDemoSessionPlatform();
#else
            return new SafeDemoSessionPlatform();
#endif
        }
    }

    /// <summary>Editor and non-Android players: never start or finish another application.</summary>
    internal sealed class SafeDemoSessionPlatform : IDemoSessionPlatform
    {
        public bool TryTakeStringExtra(string name, out string value)
        {
            value = null;
            return false;
        }

        public bool TryLaunch(string packageName, string activityName, string ticketJson, out string error)
        {
            error = "Application launch is supported only by an Android player build.";
            Debug.LogWarning("[Demo Session] " + error + " Target: " + packageName + "/" + activityName);
            return false;
        }

        public void FinishTask() => Debug.Log("[Demo Session] Task finish is skipped outside an Android player.");

        public bool TryReadOwnAsset(string name, out string text, out string error)
        {
            text = null;
            var path = Path.Combine(Application.streamingAssetsPath, name);
            try
            {
                if (!File.Exists(path)) { error = "Not found: " + path; return false; }
                if (new FileInfo(path).Length > DemoSessionTicket.MaxBytes) { error = "Asset exceeds 16384 bytes."; return false; }
                text = File.ReadAllText(path, new UTF8Encoding(false, true));
                error = null;
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is DecoderFallbackException)
            {
                error = exception.Message;
                return false;
            }
        }

        public bool TryGetOwnComponent(out DemoSessionComponent component)
        {
            component = new DemoSessionComponent(Application.identifier, DemoSessionPlatform.UnityActivity);
            return DemoSessionJson.IsJavaName(Application.identifier);
        }

        public IReadOnlyList<DemoSessionLauncherActivity> ListLauncherActivities() => Array.Empty<DemoSessionLauncherActivity>();

        public bool TryReadPackageAsset(string packageName, string name, out string text, out string error)
        {
            text = null;
            error = "Reading another package is supported only by an Android player build.";
            return false;
        }

        public bool IsPackageInstalled(string packageName) => false;
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    internal sealed class AndroidDemoSessionPlatform : IDemoSessionPlatform
    {
        public bool TryTakeStringExtra(string name, out string value)
        {
            value = null;
            try
            {
                // UnityPlayerGameActivity.onNewIntent calls setIntent, so this is also a re-delivered Intent (singleTask).
                using (var activity = CurrentActivity())
                using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
                {
                    if (intent == null || !intent.Call<bool>("hasExtra", name)) return false;
                    try { value = intent.Call<string>("getStringExtra", name); }
                    finally { intent.Call("removeExtra", name); }
                    return value != null;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Demo Session] Could not read the Intent extra " + name + ": " + exception.Message);
                return false;
            }
        }

        public bool TryLaunch(string packageName, string activityName, string ticketJson, out string error)
        {
            try
            {
                using (var activity = CurrentActivity())
                using (var intent = new AndroidJavaObject("android.content.Intent"))
                {
                    intent.Call<AndroidJavaObject>("setClassName", packageName, activityName);
                    // FLAG_ACTIVITY_NEW_TASK | FLAG_ACTIVITY_CLEAR_TASK: every runtime cold-starts and reads its ticket.
                    intent.Call<AndroidJavaObject>("addFlags", 0x10008000);
                    intent.Call<AndroidJavaObject>("putExtra", DemoSession.TicketExtra, ticketJson);
                    activity.Call("startActivity", intent);
                }
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public void FinishTask()
        {
            using (var activity = CurrentActivity()) activity.Call("finishAndRemoveTask");
        }

        public bool TryReadOwnAsset(string name, out string text, out string error)
        {
            try
            {
                using (var activity = CurrentActivity())
                using (var assets = activity.Call<AndroidJavaObject>("getAssets"))
                    return TryReadAsset(assets, name, out text, out error);
            }
            catch (Exception exception)
            {
                text = null;
                error = exception.Message;
                return false;
            }
        }

        public bool TryGetOwnComponent(out DemoSessionComponent component)
        {
            component = null;
            try
            {
                using (var activity = CurrentActivity())
                using (var name = activity.Call<AndroidJavaObject>("getComponentName"))
                    component = new DemoSessionComponent(name.Call<string>("getPackageName"), name.Call<string>("getClassName"));
                return DemoSessionJson.IsJavaName(component.PackageName) && DemoSessionJson.IsJavaName(component.ActivityName);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Demo Session] Could not resolve this activity: " + exception.Message);
                return false;
            }
        }

        public IReadOnlyList<DemoSessionLauncherActivity> ListLauncherActivities()
        {
            var result = new List<DemoSessionLauncherActivity>();
            try
            {
                using (var activity = CurrentActivity())
                using (var manager = activity.Call<AndroidJavaObject>("getPackageManager"))
                using (var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.MAIN"))
                {
                    intent.Call<AndroidJavaObject>("addCategory", "android.intent.category.LAUNCHER");
                    using (var list = manager.Call<AndroidJavaObject>("queryIntentActivities", intent, 0))
                    {
                        var count = list.Call<int>("size");
                        for (var index = 0; index < count; index++)
                        {
                            using (var resolve = list.Call<AndroidJavaObject>("get", index))
                            using (var info = resolve.Get<AndroidJavaObject>("activityInfo"))
                            {
                                var package = info.Get<string>("packageName");
                                var name = info.Get<string>("name");
                                if (DemoSessionJson.IsJavaName(package) && DemoSessionJson.IsJavaName(name))
                                    result.Add(new DemoSessionLauncherActivity(new DemoSessionComponent(package, name), LoadLabel(resolve, manager)));
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Demo Session] Could not list launcher activities: " + exception.Message);
            }
            return result;
        }

        public bool TryReadPackageAsset(string packageName, string name, out string text, out string error)
        {
            try
            {
                using (var activity = CurrentActivity())
                using (var context = activity.Call<AndroidJavaObject>("createPackageContext", packageName, 0))
                using (var assets = context.Call<AndroidJavaObject>("getAssets"))
                    return TryReadAsset(assets, name, out text, out error);
            }
            catch (Exception exception)
            {
                text = null;
                error = exception.Message;
                return false;
            }
        }

        public bool IsPackageInstalled(string packageName)
        {
            try
            {
                using (var activity = CurrentActivity())
                using (var manager = activity.Call<AndroidJavaObject>("getPackageManager"))
                using (manager.Call<AndroidJavaObject>("getPackageInfo", packageName, 0))
                    return true;
            }
            catch (AndroidJavaException)
            {
                // PackageManager.NameNotFoundException: not installed, or not visible to this package.
                return false;
            }
        }

        /// <summary>ResolveInfo.loadLabel: the name shown in Quest's library (activity label, else application label).</summary>
        private static string LoadLabel(AndroidJavaObject resolve, AndroidJavaObject manager)
        {
            try
            {
                using (var label = resolve.Call<AndroidJavaObject>("loadLabel", manager))
                    return label?.Call<string>("toString");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Demo Session] Could not read an application label: " + exception.Message);
                return null;
            }
        }

        private static bool TryReadAsset(AndroidJavaObject assets, string name, out string text, out string error)
        {
            text = null;
            using (var stream = assets.Call<AndroidJavaObject>("open", name))
            {
                try
                {
                    // AssetInputStream.available() reports the remaining asset length.
                    if (stream.Call<int>("available") > DemoSessionTicket.MaxBytes) { error = "Asset exceeds 16384 bytes."; return false; }
                    using (var scanner = new AndroidJavaObject("java.util.Scanner", stream, "UTF-8"))
                    using (var delimited = scanner.Call<AndroidJavaObject>("useDelimiter", "\\A"))
                        text = scanner.Call<bool>("hasNext") ? scanner.Call<string>("next") : string.Empty;
                }
                finally
                {
                    stream.Call("close");
                }
            }
            error = null;
            return true;
        }

        private static AndroidJavaObject CurrentActivity()
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        }
    }
#endif
}
