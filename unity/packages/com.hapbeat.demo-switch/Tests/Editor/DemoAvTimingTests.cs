using NUnit.Framework;

namespace Hapbeat.DemoSwitch.Tests
{
    /// <summary>The sound / haptics timing panel (DEC-093) with a fake provider: no audio, no haptics, no SDK.</summary>
    public sealed class DemoAvTimingTests
    {
        private sealed class FakeProvider : IDemoAvTimingProvider
        {
            public float? Saved;
            public float OffsetMs { get => Saved ?? 0f; set => Saved = value; }
            public bool HasSavedOffset => Saved.HasValue;
            public void ClearSavedOffset() => Saved = null;
            public int Starts, Stops;
            public void StartPattern() => Starts++;
            public void StopPattern() => Stops++;
        }

        private FakeProvider _provider;

        [SetUp]
        public void SetUp()
        {
            DemoPause.ResetForTests();
            _provider = new FakeProvider();
            DemoAvTiming.Provider = _provider;
        }

        [TearDown]
        public void TearDown()
        {
            DemoPause.ResetForTests();
            DemoAvTiming.ClosePanel();
            DemoAvTiming.Provider = null;
        }

        [Test]
        public void Panel_RunsThePatternWhileOpen_AndEveryStepSavesTheValue()
        {
            Assert.That(DemoAvTiming.OpenPanel(), Is.True);
            Assert.That(_provider.Starts, Is.EqualTo(1));
            var panel = DemoAvTiming.Panel;
            panel.Nudge(10); panel.Nudge(1); panel.Nudge(1);
            Assert.That(_provider.Saved, Is.EqualTo(12f));
            Assert.That(panel.ValueText, Does.StartWith("+12 ms"));
            panel.Nudge(-10); panel.Nudge(-10); panel.Nudge(-1);
            Assert.That(panel.ValueText, Does.StartWith("−9 ms"));
            panel.ResetToDefault();
            Assert.That(_provider.HasSavedOffset, Is.False);
            Assert.That(panel.ValueText, Does.StartWith("0 ms"));
            DemoAvTiming.ClosePanel();
            Assert.That(DemoAvTiming.IsOpen, Is.False);
        }

        [Test]
        public void PauseMenu_OffersTiming_WhenTheSdkProvidesIt_AndComesBackOnBack()
        {
            DemoPause.Configure(null);
            DemoPause.Pause();
            var pause = DemoPause.Panel;
            Assert.That(pause.TimingButton, Is.Not.Null);
            DemoPause.OpenTimingFromPause();
            Assert.That(DemoAvTiming.IsOpen, Is.True);
            Assert.That(pause.gameObject.activeSelf, Is.False, "the pause panel steps aside");
            DemoAvTiming.ClosePanel();
            DemoPause.Resume();
            Assert.That(DemoPause.IsPaused, Is.False);

            DemoAvTiming.Provider = null;
            DemoPause.Pause();
            Assert.That(DemoPause.Panel.TimingButton, Is.Null, "without the SDK there is no timing button");
        }

        [Test]
        public void Resume_ClosesAnOpenTimingPanel()
        {
            DemoPause.Configure(null);
            DemoPause.Pause();
            DemoPause.OpenTimingFromPause();
            DemoPause.Resume();
            Assert.That(DemoAvTiming.IsOpen, Is.False);
        }

        [Test]
        public void Describe_SaysWhichSideWaits()
        {
            Assert.That(DemoAvTiming.Describe(25f), Is.EqualTo("+25 ms（振動を 25 ms 遅らせる）"));
            Assert.That(DemoAvTiming.Describe(-7.4f), Is.EqualTo("−7 ms（音を 7 ms 遅らせる）"));
            Assert.That(DemoAvTiming.Describe(0.2f), Is.EqualTo("0 ms（補正なし）"));
        }
    }
}
