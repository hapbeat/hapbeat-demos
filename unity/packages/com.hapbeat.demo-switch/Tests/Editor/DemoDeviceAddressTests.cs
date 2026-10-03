using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hapbeat.DemoSwitch.Tests
{
    public sealed class DemoDeviceAddressTests
    {
        string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "hapbeat-device-address-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            DemoDeviceAddress.PathOverride = null;
            Directory.Delete(_directory, true);
        }

        string Write(string text)
        {
            var path = Path.Combine(_directory, DemoDeviceAddress.FileName);
            File.WriteAllText(path, text);
            return path;
        }

        [Test]
        public void ParsesBothAxes()
        {
            Assert.IsTrue(DemoDeviceAddress.TryParse(@"{""version"":1,""player"":1,""group"":2}", out var address, out var error), error);
            Assert.AreEqual(1, address.Player);
            Assert.AreEqual(2, address.Group);
        }

        [Test]
        public void ParsesBoundsAndUnspecified()
        {
            Assert.IsTrue(DemoDeviceAddress.TryParse(@"{""group"":99,""player"":-1,""version"":1}", out var address, out var error), error);
            Assert.AreEqual(DemoDeviceAddress.Unspecified, address.Player);
            Assert.AreEqual(99, address.Group);
        }

        [TestCase("")]
        [TestCase("[]")]
        [TestCase("not json")]
        [TestCase(@"{""version"":2,""player"":1,""group"":1}")]
        [TestCase(@"{""version"":1,""player"":1}")]
        [TestCase(@"{""version"":1,""group"":1}")]
        [TestCase(@"{""player"":1,""group"":1}")]
        [TestCase(@"{""version"":1,""player"":1,""group"":1,""extra"":0}")]
        [TestCase(@"{""version"":1,""player"":0,""group"":1}")]
        [TestCase(@"{""version"":1,""player"":100,""group"":1}")]
        [TestCase(@"{""version"":1,""player"":-2,""group"":1}")]
        [TestCase(@"{""version"":1,""player"":1,""group"":""2""}")]
        [TestCase(@"{""version"":1,""player"":1.5,""group"":2}")]
        [TestCase(@"{""version"":1,""player"":null,""group"":2}")]
        [TestCase(@"{""version"":1,""player"":99999999999999999999,""group"":2}")]
        [TestCase(@"{""version"":1,""player"":1,""player"":2,""group"":2}")]
        [TestCase(@"{""version"":1,""player"":1,""group"":2} {}")]
        [TestCase(@"{""version"":1,""player"":1,""group"":2 /* c */}")]
        public void RejectsInvalid(string json)
        {
            Assert.IsFalse(DemoDeviceAddress.TryParse(json, out var address, out var error));
            Assert.IsNull(address);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void RejectsMoreThan1024Bytes()
        {
            var json = @"{""version"":1,""player"":1,""group"":2}";
            json += new string(' ', DemoDeviceAddress.MaxBytes - json.Length + 1);
            Assert.IsFalse(DemoDeviceAddress.TryParse(json, out _, out var error));
            StringAssert.Contains("1024", error);
            DemoDeviceAddress.PathOverride = Write(json);
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Demo Device Address\] Ignored .*1024"));
            Assert.IsNull(DemoDeviceAddress.LoadForThisDevice());
        }

        [Test]
        public void MissingFileDoesNothingSilently()
        {
            DemoDeviceAddress.PathOverride = Path.Combine(_directory, DemoDeviceAddress.FileName);
            Assert.IsNull(DemoDeviceAddress.LoadForThisDevice());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void InvalidFileWarnsAndDoesNothing()
        {
            DemoDeviceAddress.PathOverride = Write(@"{""version"":1,""player"":0,""group"":1}");
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Demo Device Address\] Ignored .*player"));
            Assert.IsNull(DemoDeviceAddress.LoadForThisDevice());
        }

        [Test]
        public void InvalidUtf8FileWarns()
        {
            var path = Path.Combine(_directory, DemoDeviceAddress.FileName);
            File.WriteAllBytes(path, new byte[] { 0x7b, 0xff, 0x7d });
            DemoDeviceAddress.PathOverride = path;
            LogAssert.Expect(LogType.Warning, new Regex(@"\[Demo Device Address\] Ignored"));
            Assert.IsNull(DemoDeviceAddress.LoadForThisDevice());
        }

        [Test]
        public void LoadsFileWithBomAndRecordsSource()
        {
            var path = Path.Combine(_directory, DemoDeviceAddress.FileName);
            File.WriteAllText(path, @"{""version"":1,""player"":3,""group"":-1}", new System.Text.UTF8Encoding(true));
            DemoDeviceAddress.PathOverride = path;
            var address = DemoDeviceAddress.LoadForThisDevice();
            Assert.IsNotNull(address);
            Assert.AreEqual(3, address.Player);
            Assert.AreEqual(DemoDeviceAddress.Unspecified, address.Group);
            Assert.AreEqual(path, address.Source);
        }

        [Test]
        public void EditorReadsNothingWithoutEnvironmentVariable()
        {
            if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(DemoDeviceAddress.PathEnvironmentVariable)))
                Assert.Ignore(DemoDeviceAddress.PathEnvironmentVariable + " is set in this environment.");
            Assert.IsNull(DemoDeviceAddress.ResolvePath());
            Assert.IsNull(DemoDeviceAddress.LoadForThisDevice());
        }

        [Test]
        public void UnspecifiedAxisKeepsCurrentValue()
        {
            new DemoDeviceAddress(-1, 2).Resolve(5, 6, out var player, out var group);
            Assert.AreEqual(5, player);
            Assert.AreEqual(2, group);
            new DemoDeviceAddress(4, -1).Resolve(-1, 6, out player, out group);
            Assert.AreEqual(4, player);
            Assert.AreEqual(6, group);
            new DemoDeviceAddress(-1, -1).Resolve(-1, -1, out player, out group);
            Assert.AreEqual(-1, player);
            Assert.AreEqual(-1, group);
        }

        [Test]
        public void LogLineFormat()
        {
            Assert.AreEqual("HAPBEAT_DEVICE_ADDRESS player=1 group=-1 source=/sdcard/x/hapbeat-device.json",
                DemoDeviceAddress.FormatLog(1, -1, "/sdcard/x/hapbeat-device.json"));
        }
    }
}
