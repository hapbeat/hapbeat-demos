using System;
using System.Net;
using UnityEngine;

namespace Hapbeat.DemoSwitch
{
    internal readonly struct DemoSwitchLaunchContext
    {
        public DemoSwitchLaunchContext(string controllerId, long sequence, string demoId, string controllerHost, int controllerPort)
        {
            ControllerId = controllerId;
            Sequence = sequence;
            DemoId = demoId;
            ControllerHost = controllerHost;
            ControllerPort = controllerPort;
        }

        public string ControllerId { get; }
        public long Sequence { get; }
        public string DemoId { get; }
        public string ControllerHost { get; }
        public int ControllerPort { get; }
        public IPEndPoint ControllerEndpoint => new IPEndPoint(IPAddress.Parse(ControllerHost), ControllerPort);
    }

    internal interface IDemoSwitchLaunchAdapter
    {
        bool TryLaunch(DemoSwitchTarget target, DemoSwitchLaunchContext? context, out string error);
        bool TryReadLaunchContext(out DemoSwitchLaunchContext context);
    }

    internal static class DemoSwitchLaunchAdapter
    {
        public static IDemoSwitchLaunchAdapter Create()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidDemoSwitchLaunchAdapter();
#else
            return new SafeDemoSwitchLaunchAdapter();
#endif
        }
    }

    internal sealed class SafeDemoSwitchLaunchAdapter : IDemoSwitchLaunchAdapter
    {
        public bool TryLaunch(DemoSwitchTarget target, DemoSwitchLaunchContext? context, out string error)
        {
            error = "Application launch is supported only by an Android player build.";
            Debug.LogWarning("[Demo Switch] " + error);
            return false;
        }

        public bool TryReadLaunchContext(out DemoSwitchLaunchContext context)
        {
            context = default;
            return false;
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    internal sealed class AndroidDemoSwitchLaunchAdapter : IDemoSwitchLaunchAdapter
    {
        private const string ControllerId = "com.hapbeat.demo_switch.controller_id";
        private const string Sequence = "com.hapbeat.demo_switch.seq";
        private const string DemoId = "com.hapbeat.demo_switch.demo_id";
        private const string ControllerHost = "com.hapbeat.demo_switch.controller_host";
        private const string ControllerPort = "com.hapbeat.demo_switch.controller_port";

        public bool TryLaunch(DemoSwitchTarget target, DemoSwitchLaunchContext? context, out string error)
        {
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = new AndroidJavaObject("android.content.Intent"))
                {
                    intent.Call<AndroidJavaObject>("setClassName", target.PackageName, target.ActivityName);
                    // FLAG_ACTIVITY_NEW_TASK | FLAG_ACTIVITY_CLEAR_TASK: create a fresh target task so
                    // a second trip through the demo carousel receives the new launch extras.
                    intent.Call<AndroidJavaObject>("addFlags", 0x10008000);
                    if (context.HasValue)
                    {
                        var value = context.Value;
                        intent.Call<AndroidJavaObject>("putExtra", ControllerId, value.ControllerId);
                        intent.Call<AndroidJavaObject>("putExtra", Sequence, value.Sequence);
                        intent.Call<AndroidJavaObject>("putExtra", DemoId, value.DemoId);
                        intent.Call<AndroidJavaObject>("putExtra", ControllerHost, value.ControllerHost);
                        intent.Call<AndroidJavaObject>("putExtra", ControllerPort, value.ControllerPort);
                    }
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

        public bool TryReadLaunchContext(out DemoSwitchLaunchContext context)
        {
            context = default;
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
                {
                    try
                    {
                        if (!intent.Call<bool>("hasExtra", ControllerId)) return false;
                        var controllerId = intent.Call<string>("getStringExtra", ControllerId);
                        var sequence = intent.Call<long>("getLongExtra", Sequence, 0L);
                        var demoId = intent.Call<string>("getStringExtra", DemoId);
                        var host = intent.Call<string>("getStringExtra", ControllerHost);
                        var port = intent.Call<int>("getIntExtra", ControllerPort, 0);
                        if (!DemoSwitchProtocol.IsIdentifier(controllerId) || !DemoSwitchProtocol.IsIdentifier(demoId) ||
                            sequence < 1 || sequence > DemoSwitchProtocol.MaxSequence || !IPAddress.TryParse(host, out _) || port < 1 || port > 65535)
                            return false;
                        context = new DemoSwitchLaunchContext(controllerId, sequence, demoId, host, port);
                        return true;
                    }
                    finally
                    {
                        intent.Call("removeExtra", ControllerId);
                        intent.Call("removeExtra", Sequence);
                        intent.Call("removeExtra", DemoId);
                        intent.Call("removeExtra", ControllerHost);
                        intent.Call("removeExtra", ControllerPort);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Demo Switch] Could not read launch context: " + exception.Message);
                return false;
            }
        }
    }
#endif
}
