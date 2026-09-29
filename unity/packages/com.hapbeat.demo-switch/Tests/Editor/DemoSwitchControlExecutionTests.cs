using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace Hapbeat.DemoSwitch.Tests
{
    public sealed class DemoSwitchControlExecutionTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        sealed class Adapter : IDemoAppControls
        {
            public bool Fail;
            public int Step;
            public bool CanExecuteControl(string action,string scene) => true;
            public IEnumerator ExecuteControl(string action,string scene)
            {
                Step=1;
                yield return null;
                if(Fail)throw new InvalidOperationException("test failure");
                Step=2;
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TerminalStatusWaitsForExecutionAndBusyAlwaysClears(bool fail)
        {
            var go=new GameObject("loopback control test");
            var settings=ScriptableObject.CreateInstance<DemoSwitchSettings>();
            var transport=new DemoSwitchUdpTransport();
            using(var receiver=new UdpClient(new IPEndPoint(IPAddress.Loopback,0)))
            try
            {
                var runtime=go.AddComponent<DemoSwitchRuntime>();
                typeof(DemoSwitchSettings).GetField("_currentDemoId",Private).SetValue(settings,"volley");
                typeof(DemoSwitchRuntime).GetField("_settings",Private).SetValue(runtime,settings);
                typeof(DemoSwitchRuntime).GetField("_transport",Private).SetValue(runtime,transport);
                typeof(DemoSwitchRuntime).GetField("_controlBusy",Private).SetValue(runtime,true);
                var adapter=new Adapter{Fail=fail};
                var command=new DemoSwitchCommand("test-controller",1,"volley","","menu_open","");
                var operation=(IEnumerator)typeof(DemoSwitchRuntime).GetMethod("ExecuteControl",Private)
                    .Invoke(runtime,new object[]{adapter,command,(IPEndPoint)receiver.Client.LocalEndPoint});
                Assert.That(operation.MoveNext(),Is.True);
                Assert.That(adapter.Step,Is.EqualTo(1));Assert.That(receiver.Available,Is.Zero);
                Assert.That(operation.MoveNext(),Is.True);
                Assert.That(receiver.Available,Is.Zero,"READY must wait for the initialization frame.");
                Assert.That(operation.MoveNext(),Is.False);
                Assert.That(typeof(DemoSwitchRuntime).GetField("_controlBusy",Private).GetValue(runtime),Is.False);
                receiver.Client.ReceiveTimeout=1000;IPEndPoint source=null;
                var result=DemoSwitchProtocol.ParseStatus(Encoding.UTF8.GetString(receiver.Receive(ref source)));
                Assert.That(result.Success,Is.True);
                Assert.That(result.Status.Type,Is.EqualTo(fail ? "FAILED" : "READY"));
            }
            finally{UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(settings);transport.Dispose();}
        }
    }
}
