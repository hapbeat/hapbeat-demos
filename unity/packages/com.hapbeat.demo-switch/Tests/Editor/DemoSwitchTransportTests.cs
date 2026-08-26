using System.Net;
using System.Text;
using NUnit.Framework;

namespace Hapbeat.DemoSwitch.Tests
{
    public sealed class DemoSwitchTransportTests
    {
        [Test]
        public void InboxBoundsQueuedDatagramsAndReportsDrops()
        {
            var inbox = new BoundedDatagramQueue(2);
            var endpoint = new IPEndPoint(IPAddress.Loopback, 7711);

            Assert.That(inbox.TryEnqueue(new DemoSwitchDatagram(Encoding.UTF8.GetBytes("1"), endpoint)), Is.True);
            Assert.That(inbox.TryEnqueue(new DemoSwitchDatagram(Encoding.UTF8.GetBytes("2"), endpoint)), Is.True);
            Assert.That(inbox.TryEnqueue(new DemoSwitchDatagram(Encoding.UTF8.GetBytes("3"), endpoint)), Is.False);
            Assert.That(inbox.TakeDroppedCount(), Is.EqualTo(1));
            Assert.That(inbox.TakeDroppedCount(), Is.Zero);
        }

        [Test]
        public void FrameDrainProcessesAtMostConfiguredMaximum()
        {
            var inbox = new BoundedDatagramQueue(16);
            var endpoint = new IPEndPoint(IPAddress.Loopback, 7711);
            for (var index = 0; index < 10; index++)
                inbox.TryEnqueue(new DemoSwitchDatagram(new[] { (byte)index }, endpoint));
            var handled = 0;

            var drained = DemoSwitchFrameDrain.Drain(inbox, 8, _ => handled++);

            Assert.That(drained, Is.EqualTo(8));
            Assert.That(handled, Is.EqualTo(8));
            Assert.That(inbox.Count, Is.EqualTo(2));
        }

        [Test]
        public void TransportCanStartAndStopRepeatedlyWithoutSocketRace()
        {
            using (var transport = new DemoSwitchUdpTransport())
            {
                for (var index = 0; index < 64; index++)
                {
                    transport.Start(0);
                    transport.Stop();
                }
                Assert.That(transport.IsRunning, Is.False);
            }
        }
    }
}
