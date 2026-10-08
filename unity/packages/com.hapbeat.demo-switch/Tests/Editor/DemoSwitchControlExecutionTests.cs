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

        [Test]
        public void OutOfForegroundAnswersDiscoverAndQueryButRefusesSwitchAndControl()
        {
            var go=new GameObject("background runtime test");
            var settings=ScriptableObject.CreateInstance<DemoSwitchSettings>();
            var transport=new DemoSwitchUdpTransport();
            using(var receiver=new UdpClient(new IPEndPoint(IPAddress.Loopback,0)))
            try
            {
                var runtime=go.AddComponent<DemoSwitchRuntime>();
                typeof(DemoSwitchSettings).GetField("_currentDemoId",Private).SetValue(settings,"volley");
                typeof(DemoSwitchSettings).GetField("_allowUnsignedOnIsolatedLan",Private).SetValue(settings,true);
                typeof(DemoSwitchRuntime).GetField("_settings",Private).SetValue(runtime,settings);
                typeof(DemoSwitchRuntime).GetField("_transport",Private).SetValue(runtime,transport);
                typeof(DemoSwitchRuntime).GetField("_foreground",Private).SetValue(runtime,false);
                var handle=typeof(DemoSwitchRuntime).GetMethod("Handle",Private);
                var source=(IPEndPoint)receiver.Client.LocalEndPoint;
                receiver.Client.ReceiveTimeout=1000;
                string Exchange(string json)
                {
                    handle.Invoke(runtime,new object[]{new DemoSwitchDatagram(Encoding.UTF8.GetBytes(json),source)});
                    IPEndPoint from=null;
                    return Encoding.UTF8.GetString(receiver.Receive(ref from));
                }

                var here=DemoSwitchProtocol.ParseHere(Exchange("{\"version\":1,\"type\":\"DISCOVER\",\"controller_id\":\"remote-pixel\",\"nonce\":\"0123456789abcdef\"}"));
                Assert.That(here.Success,Is.True,here.ErrorMessage);
                Assert.That(here.Here.CurrentDemoId,Is.EqualTo("volley"));
                var state=DemoSwitchProtocol.ParseState(Exchange("{\"version\":1,\"type\":\"QUERY\",\"controller_id\":\"remote-pixel\",\"nonce\":\"0123456789abcdef\"}"));
                Assert.That(state.Success,Is.True,state.ErrorMessage);
                Assert.That(state.State.Foreground,Is.False);
                foreach(var json in new[]{
                    "{\"version\":1,\"type\":\"SWITCH\",\"controller_id\":\"remote-pixel\",\"seq\":1,\"demo_id\":\"handdemo\"}",
                    "{\"version\":1,\"type\":\"CONTROL\",\"controller_id\":\"remote-pixel\",\"seq\":2,\"demo_id\":\"volley\",\"action\":\"menu_open\",\"scene_id\":\"\"}"})
                {
                    var status=DemoSwitchProtocol.ParseStatus(Exchange(json));
                    Assert.That(status.Success,Is.True);
                    Assert.That(status.Status.Type,Is.EqualTo("FAILED"),json);
                    Assert.That(status.Status.Code,Is.EqualTo("not_allowed"),json);
                    Assert.That(status.Status.Message,Is.EqualTo("not in foreground"),json);
                }
                Assert.That(typeof(DemoSwitchRuntime).GetField("_controlBusy",Private).GetValue(runtime),Is.False);
            }
            finally{UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(settings);transport.Dispose();}
        }

        [Test]
        public void BindIsRetriedUntilThePreviousSocketIsReleased()
        {
            var blocker=new UdpClient(AddressFamily.InterNetwork){ExclusiveAddressUse=true};
            blocker.Client.Bind(new IPEndPoint(IPAddress.Any,0));
            var port=((IPEndPoint)blocker.Client.LocalEndPoint).Port;
            using(var transport=new DemoSwitchUdpTransport())
            try
            {
                var attempts=0;string result="unset";var retries=-1;
                string Bind()
                {
                    attempts++;
                    if(attempts==3)blocker.Dispose();
                    try{transport.Start(port);return null;}
                    catch(Exception exception){return exception.Message;}
                }
                Assert.That(Bind(),Is.Not.Null,"The port is still held by the previous socket.");
                var retry=DemoSwitchRuntime.RetryBind(Bind,(error,count)=>{result=error;retries=count;});
                while(retry.MoveNext())Assert.That(retry.Current,Is.InstanceOf<WaitForSecondsRealtime>());
                Assert.That(result,Is.Null);
                Assert.That(retries,Is.EqualTo(2));
                Assert.That(transport.IsRunning,Is.True);

                var failures=0;
                var giveUp=DemoSwitchRuntime.RetryBind(()=>{failures++;return "in use";},(error,count)=>{result=error;retries=count;});
                var waits=0;
                while(giveUp.MoveNext())waits++;
                Assert.That(failures,Is.EqualTo(DemoSwitchRuntime.BindRetryAttempts));
                Assert.That(waits*DemoSwitchRuntime.BindRetryIntervalSeconds,Is.EqualTo(5f).Within(0.001f));
                Assert.That(result,Is.EqualTo("in use"));
                Assert.That(retries,Is.EqualTo(DemoSwitchRuntime.BindRetryAttempts));
            }
            finally{blocker.Dispose();}
        }
    }
}
