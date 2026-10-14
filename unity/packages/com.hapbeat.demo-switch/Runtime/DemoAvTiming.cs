using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Hapbeat.DemoSwitch
{
    /// <summary>The sound / haptics offset of this device, provided by the Hapbeat SDK bridge (HapbeatAvSync).</summary>
    public interface IDemoAvTimingProvider
    {
        /// <summary>ms: + = the haptic later than the sound, − = the sound later. Setting it saves it on this device.</summary>
        float OffsetMs { get; set; }
        bool HasSavedOffset { get; }
        void ClearSavedOffset();
        /// <summary>Starts / stops the test pattern: a click and a short haptic pulse every second, moved by the offset.</summary>
        void StartPattern();
        void StopPattern();
    }

    /// <summary>
    /// Matching sound and haptics on this device (DEC-093): the player hears a click and feels a pulse every second
    /// and moves the offset until both come at once — a person is the sensor. The value is saved per app on the
    /// device; <see cref="ModelDefaults"/> holds per-model values found that way, used until an app saves its own.
    /// Opened from the shared pause menu, or by a demo with its own menu (<see cref="OpenPanel"/>).
    /// </summary>
    public static class DemoAvTiming
    {
        /// <summary>
        /// Offsets (ms) set by feel on real units, keyed by the normalized model name the Hub shows
        /// (<see cref="DeviceModel"/>, e.g. "Oculus Quest 3S"). Add a model after setting it on one unit.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, float> ModelDefaults = new Dictionary<string, float>();

        /// <summary>This device's model as the Hub's status reports it.</summary>
        public static string DeviceModel => DemoSwitchProtocol.NormalizeDeviceModel(SystemInfo.deviceModel);

        /// <summary>The value of this model in <see cref="ModelDefaults"/>, if any.</summary>
        public static float? ModelDefaultMs => DeviceModel != null && ModelDefaults.TryGetValue(DeviceModel, out var ms) ? ms : (float?)null;

        /// <summary>Set by the SDK bridge (only when com.hapbeat.sdk is installed).</summary>
        public static IDemoAvTimingProvider Provider { get; set; }

        public static bool IsAvailable => Provider != null;

        private static DemoAvTimingPanel _panel;
        public static bool IsOpen => _panel != null;
        internal static DemoAvTimingPanel Panel => _panel;

        /// <summary>
        /// Opens the adjustment panel in front of the HMD (the test pattern runs while it is open); 戻る closes it and
        /// runs <paramref name="onClosed"/>. False without the SDK.
        /// </summary>
        public static bool OpenPanel(Action onClosed = null)
        {
            if (Provider == null) return false;
            if (_panel == null) _panel = DemoAvTimingPanel.Create(Provider, () => { ClosePanel(); onClosed?.Invoke(); });
            return true;
        }

        /// <summary>Closes the panel (and its test pattern) without running the open call's onClosed.</summary>
        public static void ClosePanel()
        {
            if (_panel == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(_panel.gameObject);
            else UnityEngine.Object.DestroyImmediate(_panel.gameObject);
            _panel = null;
        }

        /// <summary>"+12 ms" style, and what it does.</summary>
        internal static string Describe(float ms)
        {
            var rounded = Mathf.RoundToInt(ms);
            if (rounded > 0) return $"+{rounded} ms（振動を {rounded} ms 遅らせる）";
            if (rounded < 0) return $"−{-rounded} ms（音を {-rounded} ms 遅らせる）";
            return "0 ms（補正なし）";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _panel = null; }
    }

    /// <summary>
    /// "音と振動のタイミング" panel: the value, a bar (−150..+150 ms shown), −10 / −1 / +1 / +10, 既定に戻す and 戻る.
    /// Placed in front of the HMD like the pause panel; the test pattern runs while it exists. Every press saves the value.
    /// </summary>
    internal sealed class DemoAvTimingPanel : MonoBehaviour
    {
        private const float Width = 560f;
        private const float Height = 600f;
        private const float BarRangeMs = 150f;
        private const float BarWidth = 440f;
        private DemoSessionPanel _panel;
        private IDemoAvTimingProvider _provider;
        private Text _value;
        private Text _source;
        private Image _marker;

        internal DemoSessionPanel Panel => _panel;
        internal string ValueText => _value.text;

        public static DemoAvTimingPanel Create(IDemoAvTimingProvider provider, Action onBack)
        {
            var panel = DemoSessionPanel.Create("Hapbeat Demo AV Timing", new Vector2(Width, Height));
            var timing = panel.gameObject.AddComponent<DemoAvTimingPanel>();
            timing._panel = panel;
            timing._provider = provider;
            var dim = new Color(0.6f, 0.7f, 0.8f);
            var y = Height * 0.5f - 52f;
            panel.AddText(new Vector2(0, y), new Vector2(Width - 40, 52), "音と振動のタイミング", 32, Color.white);
            y -= 92f;
            panel.AddText(new Vector2(0, y), new Vector2(Width - 60, 110),
                "1 秒ごとにクリック音と短い振動が出ます。同時に感じられるところまで合わせてください。\n振動が先に感じたら「＋」、音が先に感じたら「−」。", 18, new Color(0.85f, 0.9f, 0.95f));
            y -= 86f;
            timing._value = panel.AddText(new Vector2(0, y), new Vector2(Width - 40, 44), string.Empty, 26, Color.white);
            y -= 40f;
            timing._source = panel.AddText(new Vector2(0, y), new Vector2(Width - 40, 30), string.Empty, 16, dim);
            y -= 40f;
            panel.AddRect(new Vector2(0, y), new Vector2(BarWidth, 6), new Color(0.3f, 0.36f, 0.45f));
            panel.AddRect(new Vector2(0, y), new Vector2(2, 22), new Color(0.6f, 0.66f, 0.75f));
            timing._marker = panel.AddRect(new Vector2(0, y), new Vector2(8, 30), new Color(0.55f, 0.9f, 1f));
            panel.AddText(new Vector2(-BarWidth * 0.5f + 50, y - 26), new Vector2(140, 24), "− 音を遅く", 14, dim);
            panel.AddText(new Vector2(BarWidth * 0.5f - 50, y - 26), new Vector2(140, 24), "振動を遅く ＋", 14, dim);
            y -= 82f;
            var steps = new[] { -10, -1, 1, 10 };
            const float gap = 12f;
            var buttonWidth = (Width - 60 - gap * (steps.Length - 1)) / steps.Length;
            for (var index = 0; index < steps.Length; index++)
            {
                var step = steps[index];
                var x = -Width * 0.5f + 30 + buttonWidth * 0.5f + index * (buttonWidth + gap);
                panel.AddButton(new Vector2(x, y), new Vector2(buttonWidth, 64), step > 0 ? "+" + step : "−" + -step, 26, () => timing.Nudge(step));
            }
            y -= 84f;
            var half = (Width - 60 - gap) * 0.5f;
            panel.AddButton(new Vector2(-half * 0.5f - gap * 0.5f, y), new Vector2(half, 60), "既定に戻す", 22, timing.ResetToDefault);
            panel.AddButton(new Vector2(half * 0.5f + gap * 0.5f, y), new Vector2(half, 60), "戻る", 22, onBack);
            timing.Refresh();
            var camera = Camera.main;
            if (camera != null) panel.PlaceInFront(camera.transform, DemoPausePanel.Distance, DemoPausePanel.Drop);
            panel.EnableInputAfter(DemoPausePanel.InputDelaySeconds);
            provider.StartPattern();
            return timing;
        }

        internal void Nudge(int ms)
        {
            _provider.OffsetMs = Mathf.Round(_provider.OffsetMs + ms);
            Refresh();
        }

        internal void ResetToDefault()
        {
            _provider.ClearSavedOffset();
            Refresh();
        }

        internal void Refresh()
        {
            var ms = _provider.OffsetMs;
            _value.text = DemoAvTiming.Describe(ms);
            var model = DemoAvTiming.DeviceModel ?? "?";
            _source.text = _provider.HasSavedOffset ? $"この端末に保存済み（{model}）"
                : DemoAvTiming.ModelDefaultMs.HasValue ? $"機種の既定値（{model}）" : $"既定値（{model} の値は未登録）";
            var x = Mathf.Clamp(ms, -BarRangeMs, BarRangeMs) / BarRangeMs * BarWidth * 0.5f;
            _marker.rectTransform.anchoredPosition = new Vector2(x, _marker.rectTransform.anchoredPosition.y);
        }

        /// <summary>A recenter: in front of the HMD again.</summary>
        internal void PlaceInFront()
        {
            var camera = Camera.main;
            if (camera != null) _panel.PlaceInFront(camera.transform, DemoPausePanel.Distance, DemoPausePanel.Drop);
        }

        private void OnDestroy() => _provider?.StopPattern();
    }
}
