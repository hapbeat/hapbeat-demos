using System.Collections.Generic;
using NUnit.Framework;

namespace Hapbeat.DemoSwitch.Tests
{
    public sealed class DemoSwitchPolicyTests
    {
        [Test]
        public void AllowlistResolvesOnlyLogicalDemoIdsConfiguredLocally()
        {
            var settings = UnityEngine.ScriptableObject.CreateInstance<DemoSwitchSettings>();
            settings.SetTargetsForTests(new List<DemoSwitchTarget>
            {
                new DemoSwitchTarget("boxing", "com.hapbeat.boxing", "com.hapbeat.boxing.MainActivity")
            });

            Assert.That(settings.TryResolveTarget("boxing", out var target), Is.True);
            Assert.That(target.PackageName, Is.EqualTo("com.hapbeat.boxing"));
            Assert.That(settings.TryResolveTarget("com.hapbeat.boxing", out _), Is.False);
        }

        [Test]
        public void SequenceGuardRejectsDuplicateAndOlderCommandsPerController()
        {
            var store = new MemorySequenceStore();
            var guard = new DemoSwitchSequenceGuard(store);

            Assert.That(guard.TryAccept("m5-main", 42), Is.True);
            Assert.That(guard.TryAccept("m5-main", 42), Is.False);
            Assert.That(guard.TryAccept("m5-main", 41), Is.False);
            Assert.That(guard.TryAccept("m5-backup", 1), Is.True);
        }

        [Test]
        public void LaunchContextSequenceAdvancesStoredMaximumWithoutReacceptingIt()
        {
            var store = new MemorySequenceStore();
            var guard = new DemoSwitchSequenceGuard(store);

            guard.AdvanceTo("m5-main", 42);
            guard.AdvanceTo("m5-main", 41);

            Assert.That(store.TryGet("m5-main", out var stored), Is.True);
            Assert.That(stored, Is.EqualTo(42));
            Assert.That(guard.TryAccept("m5-main", 42), Is.False);
            Assert.That(guard.TryAccept("m5-main", 43), Is.True);
        }

        private sealed class MemorySequenceStore : IDemoSwitchSequenceStore
        {
            private readonly Dictionary<string, long> _sequences = new Dictionary<string, long>();

            public bool TryGet(string controllerId, out long sequence) => _sequences.TryGetValue(controllerId, out sequence);
            public void Set(string controllerId, long sequence) => _sequences[controllerId] = sequence;
        }
    }
}
