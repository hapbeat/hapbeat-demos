using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Hapbeat.DemoSwitch
{
    internal interface IDemoSwitchSequenceStore
    {
        bool TryGet(string controllerId, out long sequence);
        void Set(string controllerId, long sequence);
    }

    internal sealed class DemoSwitchSequenceGuard
    {
        private readonly IDemoSwitchSequenceStore _store;

        public DemoSwitchSequenceGuard(IDemoSwitchSequenceStore store) => _store = store;

        public bool TryAccept(string controllerId, long sequence)
        {
            if (_store.TryGet(controllerId, out var previous) && sequence <= previous) return false;
            _store.Set(controllerId, sequence);
            return true;
        }

        public void AdvanceTo(string controllerId, long sequence)
        {
            if (_store.TryGet(controllerId, out var previous) && previous >= sequence) return;
            _store.Set(controllerId, sequence);
        }
    }

    internal sealed class PlayerPrefsSequenceStore : IDemoSwitchSequenceStore
    {
        public bool TryGet(string controllerId, out long sequence) =>
            long.TryParse(PlayerPrefs.GetString(Key(controllerId), string.Empty), out sequence);

        public void Set(string controllerId, long sequence)
        {
            PlayerPrefs.SetString(Key(controllerId), sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
            PlayerPrefs.Save();
        }

        private static string Key(string controllerId)
        {
            using (var sha = SHA256.Create())
            {
                var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(controllerId));
                var builder = new StringBuilder(digest.Length * 2);
                foreach (var value in digest) builder.Append(value.ToString("x2"));
                return "hapbeat.demo-switch.seq." + builder;
            }
        }
    }
}
