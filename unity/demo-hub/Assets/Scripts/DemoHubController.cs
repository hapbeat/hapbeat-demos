using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hapbeat.DemoSwitch;
using UnityEngine;
using UnityEngine.XR;

namespace Hapbeat.DemoHub
{
    /// <summary>Fixed UI strings; HubValidation checks every glyph against the bundled font.</summary>
    public static class HubText
    {
        public const string TopTitle = "体験するデモを選んでください";
        public const string NothingToShow = "管理画面で表示するプリセット／デモを選んでください";
        public const string Manage = "管理";
        public const string HoldFilled = "■";
        public const string HoldEmpty = "□";
        public const string StaffWaitingNote = "スタッフ待機モード（解除は「管理」を2秒長押し）";
        public const string AboutMinutes = "約{0}分";
        public const string Arrow = " → ";
        public const string NotInstalled = "（未インストール）";
        public const string NothingInstalled = "インストール済みのデモがプランにありません。";
        public const string LaunchFailed = "起動できませんでした: ";
        public const string Preset = "プリセット";
        public const string Tiles = "デモのタイル";
        public const string TilesHeading = "トップに表示するデモ（インストール済み）";
        public const string NoDemos = "インストール済みのデモがありません。";
        public const string ShowOnTopOn = "トップに表示：する";
        public const string ShowOnTopOff = "トップに表示：しない";
        public const string TileOn = "表示する";
        public const string TileOff = "表示しない";
        public const string Catalog = "カタログ（タップで追加）";
        public const string Plan = "プラン";
        public const string EmptyPlan = "プランが空です。左のカタログからデモを追加してください。";
        public const string Up = "↑";
        public const string Down = "↓";
        public const string Remove = "×";
        public const string PagePrevious = "▲";
        public const string PageNext = "▼";
        public const string RetryOn = "もう一度あり";
        public const string RetryOff = "もう一度なし";
        public const string HapticsUiOn = "触覚ボタン：表示する";
        public const string HapticsUiOff = "触覚ボタン：表示しない";
        public const string StaffWaitingOn = "スタッフ待機モード：ON";
        public const string StaffWaitingOff = "スタッフ待機モード：OFF";
        public const string Done = "完了";
        public const string SaveFailed = "保存できませんでした";
        public const string FinishedMessage = "体験は以上です。\nヘッドセットを外してください";
        public const string Restart = "最初から（同じプラン）";
        public const string BackToTop = "トップへ";
        public const string PlanFull = "プランは最大 32 件です";
        public const string DeviceAddress = "この端末: プレイヤー {0} / グループ {1}";
        public const string Unspecified = "指定なし";

        public static IEnumerable<string> All => typeof(HubText).GetFields()
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue());
    }

    /// <summary>
    /// Self-service launcher: the top screen starts a shown preset or a single demo tile, the manage
    /// screen (2 s long press) edits the three presets and what the top screen shows, and the finish
    /// screen follows a completed multi-step session. With staff waiting mode on, the top screen is
    /// the M5 waiting room instead. M5 SWITCH keeps working in every screen.
    /// </summary>
    public sealed class DemoHubController : MonoBehaviour
    {
        public enum HubScreen { Top, Manage, Finished }

        /// <summary>Manage-screen tab after the three presets.</summary>
        internal const int TilesTab = HubPlanStore.PresetCount;
        private const int CatalogPageSize = 5;
        private const int TileColumns = 3;
        private const int TilesPerPage = 6;
        private const float TopWidth = 640f;
        private const float PresetHeight = 66f;
        private const float TileWidth = 192f;
        private const float TileHeight = 76f;
        private const float Gap = 10f;
        private const float PlanTop = 150f;
        private const float PlanBottom = -138f;
        private static readonly Color Muted = new Color(0.7f, 0.8f, 0.9f);
        private static readonly Color Warning = new Color(1f, 0.6f, 0.45f);

        [Tooltip("Head-locked waiting labels; shown only on the top screen in staff waiting mode.")]
        [SerializeField] private GameObject _waitingMessage;

        private readonly List<int> _planPageStarts = new List<int> { 0 };
        private readonly HubPlan[] _presets = new HubPlan[HubPlanStore.PresetCount];
        private readonly HubHoldGesture _hold = new HubHoldGesture();
        private readonly HubPanelFollow _follow = new HubPanelFollow();
        private DemoSessionPanel _panel;
        private HubPlanStore _store;
        private IReadOnlyList<DemoSessionCatalogEntry> _catalog;
        private int _tab, _catalogPage, _planPage, _tilePage, _manageTilePage;
        private string _status = string.Empty;
        private bool _placed;
        private DemoDeviceAddress _deviceAddress;
        private DemoSessionButton _manageButton;

        public HubScreen Screen { get; private set; }
        public HubSettings Settings { get; private set; } = new HubSettings();
        /// <summary>The preset shown in the manage screen (the last preset while the tiles tab is open).</summary>
        public HubPlan EditedPlan => _presets[Mathf.Min(_tab, HubPlanStore.PresetCount - 1)];
        internal DemoSessionPanel Panel => _panel;
        internal DemoSessionButton ManageButton => _manageButton;
        internal int Tab => _tab;

        public HubPlan Preset(int number) => _presets[number - 1];

        private void Start()
        {
            _deviceAddress = DemoDeviceAddress.LoadForThisDevice();
            Initialize(HubCatalog.Load(), HubPlanStore.Default);
            Show(InitialScreen(DemoSession.Ticket));
        }

        internal void Initialize(IReadOnlyList<DemoSessionCatalogEntry> catalog, HubPlanStore store)
        {
            _catalog = catalog;
            _store = store;
            Settings = _store.LoadSettings();
            for (var number = 1; number <= HubPlanStore.PresetCount; number++) _presets[number - 1] = _store.LoadPreset(number);
        }

        /// <summary>
        /// A completed multi-step session shows the finish screen; a completed demo tile (one step)
        /// returns straight to the top screen, as does a cold start.
        /// </summary>
        internal static HubScreen InitialScreen(DemoSessionTicket ticket) =>
            ticket != null && ticket.IsFinished && ticket.Steps.Count > 1 ? HubScreen.Finished : HubScreen.Top;

        private void Update()
        {
            var camera = Camera.main;
            if (_panel != null && camera != null)
            {
                // The first pose may predate tracking; place once the headset reports a tracked pose, then follow lazily.
                if (!_placed) Place();
                else
                {
                    var head = camera.transform;
                    var pose = _follow.Step(new Pose(_panel.transform.position, _panel.transform.rotation), head.position, head.forward, head.up, Time.unscaledDeltaTime);
                    _panel.transform.SetPositionAndRotation(pose.position, pose.rotation);
                }
            }
            UpdateManageHold(Time.realtimeSinceStartup);
        }

        /// <summary>Long press on 管理: the button label shows the hold progress, 2 s opens the manage screen.</summary>
        internal void UpdateManageHold(float now)
        {
            if (Screen != HubScreen.Top || _manageButton == null) return;
            var fired = _hold.Update(_manageButton.Held, now);
            _manageButton.Label = HoldLabel(_hold.Progress);
            if (fired) Show(HubScreen.Manage);
        }

        internal static string HoldLabel(float progress)
        {
            if (progress <= 0f) return HubText.Manage;
            const int segments = 5;
            var filled = Mathf.Clamp(Mathf.FloorToInt(progress * segments), 0, segments);
            return string.Concat(Enumerable.Repeat(HubText.HoldFilled, filled)) + string.Concat(Enumerable.Repeat(HubText.HoldEmpty, segments - filled));
        }

        private static bool IsHeadTracked()
        {
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            return head.isValid && head.TryGetFeatureValue(CommonUsages.isTracked, out var tracked) && tracked;
        }

        internal void Show(HubScreen screen)
        {
            Screen = screen;
            _status = string.Empty;
            if (_panel == null) _panel = DemoSessionPanel.Create("Demo Session Hub", Vector2.one);
            if (_waitingMessage != null) _waitingMessage.SetActive(screen == HubScreen.Top && Settings.StaffWaiting);
            Rebuild();
            if (!_placed) Place();
            _panel.EnableInputAfter(0.4f);
        }

        private void Place()
        {
            var camera = Camera.main;
            if (camera == null) return;
            var head = camera.transform;
            var pose = HubPanelFollow.Target(head.position, head.forward, head.up);
            _panel.transform.SetPositionAndRotation(pose.position, pose.rotation);
            _placed = Application.isEditor || IsHeadTracked();
        }

        internal void Rebuild()
        {
            _panel.ClearContent();
            _manageButton = null;
            _hold.Reset();
            switch (Screen)
            {
                case HubScreen.Top:
                    if (Settings.StaffWaiting) BuildStaffWaiting();
                    else BuildTop();
                    break;
                case HubScreen.Manage: BuildManage(); break;
                case HubScreen.Finished: BuildFinished(); break;
            }
        }

        private void SetStatus(string status)
        {
            _status = status;
            Rebuild();
        }

        // ------------------------------------------------------------------ top

        private void BuildTop()
        {
            var items = HubTopItems.Decide(Settings, _presets, _catalog);
            var tilePages = Mathf.Max(1, Mathf.CeilToInt(items.Demos.Count / (float)TilesPerPage));
            _tilePage = Mathf.Clamp(_tilePage, 0, tilePages - 1);
            // Paged tiles always reserve full pages so paging never resizes the panel.
            var tileRows = tilePages > 1 ? TilesPerPage / TileColumns : Mathf.CeilToInt(items.Demos.Count / (float)TileColumns);
            var height = 20f + 40f + 26f + Gap
                + items.Presets.Count * (PresetHeight + Gap)
                + tileRows * (TileHeight + Gap)
                + (tilePages > 1 ? 44f : 0f)
                + (items.IsEmpty ? 90f : 0f)
                + 52f + 16f;
            _panel.Resize(new Vector2(TopWidth, height));
            var y = height * 0.5f - 20f - 20f;
            _panel.AddText(new Vector2(0, y), new Vector2(TopWidth - 40, 40), HubText.TopTitle, 28, Color.white);
            y -= 20f + 13f;
            _panel.AddText(new Vector2(0, y), new Vector2(TopWidth - 40, 26), DeviceAddressLine(_deviceAddress), 16, Muted);
            y -= 13f + Gap;

            foreach (var number in items.Presets)
            {
                y -= PresetHeight * 0.5f;
                var preset = number;
                _panel.AddButton(new Vector2(0, y), new Vector2(TopWidth - 40, PresetHeight), PresetLabel(number), 20, () => StartPreset(preset));
                y -= PresetHeight * 0.5f + Gap;
            }

            for (var row = 0; row < tileRows; row++)
            {
                y -= TileHeight * 0.5f;
                for (var column = 0; column < TileColumns; column++)
                {
                    var index = _tilePage * TilesPerPage + row * TileColumns + column;
                    if (index >= items.Demos.Count) break;
                    var entry = items.Demos[index];
                    _panel.AddButton(new Vector2((column - 1) * (TileWidth + Gap), y), new Vector2(TileWidth, TileHeight), TileLabel(entry.Descriptor), 20, () => StartDemo(entry));
                }
                y -= TileHeight * 0.5f + Gap;
            }
            if (tilePages > 1)
            {
                y -= 22f;
                PageControls(0, y, _tilePage, tilePages, page => { _tilePage = page; Rebuild(); });
                y -= 22f;
            }

            if (items.IsEmpty)
            {
                y -= 45f;
                _panel.AddText(new Vector2(0, y), new Vector2(TopWidth - 80, 80), HubText.NothingToShow, 22, Color.white);
                y -= 45f;
            }

            // Footer: status (launch errors) and the long-press manage button.
            y -= 26f;
            _panel.AddText(new Vector2(-70, y), new Vector2(TopWidth - 180, 44), _status, 15, Warning, TextAnchor.MiddleLeft);
            _manageButton = _panel.AddButton(new Vector2(TopWidth * 0.5f - 20f - 50f, y), new Vector2(100, 44), HubText.Manage, 18, null);
        }

        /// <summary>M5 waiting room: only the head-locked labels, this device's address and the manage button.</summary>
        private void BuildStaffWaiting()
        {
            _panel.Resize(new Vector2(520, 110));
            _panel.AddText(new Vector2(0, 28), new Vector2(480, 26), DeviceAddressLine(_deviceAddress), 16, Muted);
            _panel.AddText(new Vector2(-60, -22), new Vector2(360, 44), HubText.StaffWaitingNote, 15, Muted, TextAnchor.MiddleLeft);
            _manageButton = _panel.AddButton(new Vector2(190, -22), new Vector2(100, 44), HubText.Manage, 18, null);
        }

        /// <summary>"プリセット 1　約8分" over the step summary.</summary>
        private string PresetLabel(int number)
        {
            var plan = _presets[number - 1];
            var minutes = plan.Minutes(_catalog);
            var heading = HubText.Preset + " " + number + (minutes > 0 ? "　" + MinutesText(minutes) : string.Empty);
            return heading + "\n" + plan.Summary(_catalog);
        }

        private static string TileLabel(DemoSessionDescriptor descriptor) =>
            descriptor.Title.Ja + (descriptor.Minutes > 0 ? "\n" + MinutesText(descriptor.Minutes.Value) : string.Empty);

        private static string MinutesText(double minutes) =>
            string.Format(CultureInfo.InvariantCulture, HubText.AboutMinutes, minutes.ToString("0.#", CultureInfo.InvariantCulture));

        /// <summary>This device's hapbeat-device.json; a missing or invalid file shows both axes as unspecified.</summary>
        internal static string DeviceAddressLine(DemoDeviceAddress address) =>
            string.Format(CultureInfo.InvariantCulture, HubText.DeviceAddress,
                Axis(address == null ? DemoDeviceAddress.Unspecified : address.Player),
                Axis(address == null ? DemoDeviceAddress.Unspecified : address.Group));

        private static string Axis(int value) =>
            value == DemoDeviceAddress.Unspecified ? HubText.Unspecified : value.ToString(CultureInfo.InvariantCulture);

        internal void StartPreset(int number) => Launch(_presets[number - 1].BuildTicket(_catalog, Finish(), DemoSessionTicket.NewSessionId(), Settings.HapticsUi));

        /// <summary>A demo tile is a one-step session: descriptor defaults, retry, finish = this Hub.</summary>
        internal void StartDemo(DemoSessionCatalogEntry entry) =>
            Launch(HubPlan.Single(entry.Descriptor).BuildTicket(_catalog, Finish(), DemoSessionTicket.NewSessionId(), Settings.HapticsUi));

        private static DemoSessionComponent Finish() =>
            DemoSessionCatalog.TryGetOwnComponent(out var finish) ? finish : new DemoSessionComponent(HubIdentity.PackageName, HubIdentity.ActivityName);

        private void Launch(DemoSessionTicket ticket)
        {
            if (ticket == null) { SetStatus(HubText.NothingInstalled); return; }
            if (!DemoSession.LaunchTicket(ticket, out var error)) SetStatus(HubText.LaunchFailed + error);
        }

        // --------------------------------------------------------------- manage

        private void BuildManage()
        {
            _panel.Resize(new Vector2(780, 540));
            // Tabs: presets 1-3 and the demo tiles; a preset tab adds its top-screen toggle.
            for (var tab = 0; tab <= TilesTab; tab++)
            {
                var index = tab;
                var label = tab == TilesTab ? HubText.Tiles : HubText.Preset + " " + (tab + 1);
                _panel.AddButton(new Vector2(-315 + tab * 132, 238), new Vector2(126, 44), label, 18, () => SelectTab(index)).Highlighted = tab == _tab;
            }
            if (_tab == TilesTab) BuildTilesTab();
            else
            {
                var number = _tab + 1;
                var visible = Settings.VisiblePresets.Contains(number);
                _panel.AddButton(new Vector2(270, 238), new Vector2(220, 44), visible ? HubText.ShowOnTopOn : HubText.ShowOnTopOff, 18, () =>
                {
                    if (!Settings.VisiblePresets.Remove(number)) Settings.VisiblePresets.Add(number);
                    SettingsChanged();
                }).Highlighted = visible;
                BuildPresetTab();
            }

            // Footer: settings shared by every launch, then done.
            _panel.AddButton(new Vector2(-235, -188), new Vector2(290, 46), Settings.HapticsUi ? HubText.HapticsUiOn : HubText.HapticsUiOff, 18,
                () => { Settings.HapticsUi = !Settings.HapticsUi; SettingsChanged(); }).Highlighted = Settings.HapticsUi;
            _panel.AddButton(new Vector2(75, -188), new Vector2(290, 46), Settings.StaffWaiting ? HubText.StaffWaitingOn : HubText.StaffWaitingOff, 18,
                () => { Settings.StaffWaiting = !Settings.StaffWaiting; SettingsChanged(); }).Highlighted = Settings.StaffWaiting;
            _panel.AddButton(new Vector2(310, -188), new Vector2(120, 46), HubText.Done, 20, () => Show(HubScreen.Top));
            _panel.AddText(new Vector2(0, -232), new Vector2(740, 24), _status, 15, Warning);
        }

        internal void SelectTab(int tab)
        {
            _tab = Mathf.Clamp(tab, 0, TilesTab);
            _planPage = 0;
            _status = string.Empty;
            Rebuild();
        }

        private void BuildPresetTab()
        {
            var plan = EditedPlan;
            // Catalog column.
            _panel.AddText(new Vector2(-270, 196), new Vector2(220, 34), HubText.Catalog, 17, Muted);
            var catalogPages = Mathf.Max(1, Mathf.CeilToInt(_catalog.Count / (float)CatalogPageSize));
            _catalogPage = Mathf.Clamp(_catalogPage, 0, catalogPages - 1);
            for (var slot = 0; slot < CatalogPageSize; slot++)
            {
                var index = _catalogPage * CatalogPageSize + slot;
                if (index >= _catalog.Count) break;
                var entry = _catalog[index];
                _panel.AddButton(new Vector2(-270, 149 - slot * 56), new Vector2(220, 48), entry.Descriptor.Title.Ja, 19, () =>
                {
                    if (!plan.Add(entry.Descriptor)) { SetStatus(HubText.PlanFull); return; }
                    _planPage = int.MaxValue; // Show the appended step.
                    PlanChanged();
                });
            }
            if (catalogPages > 1) PageControls(-270, PlanBottom, _catalogPage, catalogPages, page => { _catalogPage = page; Rebuild(); });

            // Plan column.
            _panel.AddText(new Vector2(60, 196), new Vector2(300, 34), HubText.Plan, 17, Muted, TextAnchor.MiddleLeft);
            if (plan.Steps.Count == 0)
            {
                _panel.AddText(new Vector2(130, PlanTop - 40f), new Vector2(460, 60), HubText.EmptyPlan, 18, Muted, TextAnchor.UpperLeft);
                return;
            }
            LayoutPlanPages();
            _planPage = Mathf.Clamp(_planPage, 0, _planPageStarts.Count - 1);
            var y = PlanTop;
            var end = _planPage + 1 < _planPageStarts.Count ? _planPageStarts[_planPage + 1] : plan.Steps.Count;
            for (var index = _planPageStarts[_planPage]; index < end; index++) y = PlanRow(index, y);
            if (_planPageStarts.Count > 1) PageControls(300, 196, _planPage, _planPageStarts.Count, page => { _planPage = page; Rebuild(); });
        }

        private void BuildTilesTab()
        {
            _panel.AddText(new Vector2(0, 196), new Vector2(740, 34), HubText.TilesHeading, 17, Muted);
            if (_catalog.Count == 0)
            {
                _panel.AddText(new Vector2(0, 120), new Vector2(700, 40), HubText.NoDemos, 19, Color.white);
                return;
            }
            var pages = Mathf.Max(1, Mathf.CeilToInt(_catalog.Count / (float)CatalogPageSize));
            _manageTilePage = Mathf.Clamp(_manageTilePage, 0, pages - 1);
            for (var slot = 0; slot < CatalogPageSize; slot++)
            {
                var index = _manageTilePage * CatalogPageSize + slot;
                if (index >= _catalog.Count) break;
                var descriptor = _catalog[index].Descriptor;
                var y = 149 - slot * 56;
                var minutes = descriptor.Minutes > 0 ? "（" + MinutesText(descriptor.Minutes.Value) + "）" : string.Empty;
                _panel.AddText(new Vector2(-110, y), new Vector2(440, 44), descriptor.Title.Ja + minutes, 19, Color.white, TextAnchor.MiddleLeft);
                var visible = Settings.VisibleDemos.Contains(descriptor.DemoId);
                _panel.AddButton(new Vector2(240, y), new Vector2(200, 46), visible ? HubText.TileOn : HubText.TileOff, 18, () =>
                {
                    if (!Settings.VisibleDemos.Remove(descriptor.DemoId)) Settings.VisibleDemos.Add(descriptor.DemoId);
                    SettingsChanged();
                }).Highlighted = visible;
            }
            if (pages > 1) PageControls(0, PlanBottom, _manageTilePage, pages, page => { _manageTilePage = page; Rebuild(); });
        }

        private void PlanChanged()
        {
            _status = _store.Save(EditedPlan, HubPlanStore.PresetSlot(_tab + 1)) ? string.Empty : HubText.Preset + " " + (_tab + 1) + HubText.SaveFailed;
            Rebuild();
        }

        private void SettingsChanged()
        {
            _status = _store.SaveSettings(Settings) ? string.Empty : HubText.SaveFailed;
            Rebuild();
        }

        /// <summary>Greedy pagination: rows grow with their option chip lines.</summary>
        private void LayoutPlanPages()
        {
            _planPageStarts.Clear();
            _planPageStarts.Add(0);
            var y = PlanTop;
            for (var index = 0; index < EditedPlan.Steps.Count; index++)
            {
                var height = RowHeight(index);
                if (y - height < PlanBottom && index > _planPageStarts[_planPageStarts.Count - 1])
                {
                    _planPageStarts.Add(index);
                    y = PlanTop;
                }
                y -= height;
            }
        }

        private int ChipCount(int index)
        {
            var step = EditedPlan.Steps[index];
            var entry = HubPlan.Find(_catalog, step.DemoId);
            return (entry == null ? 0 : HubPlan.VisibleOptions(step, entry.Descriptor).Count) + 1;
        }

        private float RowHeight(int index) => 48f + Mathf.CeilToInt(ChipCount(index) / 3f) * 44f + 10f;

        private float PlanRow(int index, float top)
        {
            var plan = EditedPlan;
            var step = plan.Steps[index];
            var entry = HubPlan.Find(_catalog, step.DemoId);
            var title = entry == null ? step.DemoId + HubText.NotInstalled : entry.Descriptor.Title.Ja;
            var y = top - 22f;
            _panel.AddText(new Vector2(0, y), new Vector2(260, 40), (index + 1) + ". " + title, 19, Color.white, TextAnchor.MiddleLeft);
            var i = index;
            _panel.AddButton(new Vector2(190, y), new Vector2(50, 40), HubText.Up, 20, () => { plan.MoveUp(i); PlanChanged(); }).Interactable = index > 0;
            _panel.AddButton(new Vector2(250, y), new Vector2(50, 40), HubText.Down, 20, () => { plan.MoveDown(i); PlanChanged(); }).Interactable = index < plan.Steps.Count - 1;
            _panel.AddButton(new Vector2(320, y), new Vector2(60, 40), HubText.Remove, 22, () => { plan.Remove(i); PlanChanged(); });

            var chips = new List<(string label, System.Action press, bool highlighted)>();
            if (entry != null)
                foreach (var option in HubPlan.VisibleOptions(step, entry.Descriptor))
                {
                    var id = option.Id;
                    chips.Add((option.Label.Ja + "：" + option.Find(HubPlan.Value(step, option)).Label.Ja,
                        () => { plan.CycleOption(i, entry.Descriptor, id); PlanChanged(); }, false));
                }
            chips.Add((step.Retry ? HubText.RetryOn : HubText.RetryOff, () => { plan.ToggleRetry(i); PlanChanged(); }, step.Retry));
            for (var chip = 0; chip < chips.Count; chip++)
            {
                var position = new Vector2(-75 + (chip % 3) * 160, top - 48f - (chip / 3) * 44f - 20f);
                var button = _panel.AddButton(position, new Vector2(152, 38), chips[chip].label, 16, chips[chip].press);
                button.Highlighted = chips[chip].highlighted;
            }
            return top - RowHeight(index);
        }

        private void PageControls(float x, float y, int page, int pages, System.Action<int> select)
        {
            _panel.AddButton(new Vector2(x - 70, y), new Vector2(46, 34), HubText.PagePrevious, 16, () => select(page - 1)).Interactable = page > 0;
            _panel.AddText(new Vector2(x, y), new Vector2(80, 34), (page + 1) + " / " + pages, 16, Muted);
            _panel.AddButton(new Vector2(x + 70, y), new Vector2(46, 34), HubText.PageNext, 16, () => select(page + 1)).Interactable = page < pages - 1;
        }

        // --------------------------------------------------------------- finish

        private void BuildFinished()
        {
            _panel.Resize(new Vector2(540, 300));
            _panel.AddText(new Vector2(0, 70), new Vector2(500, 110), HubText.FinishedMessage, 30, Color.white);
            _panel.AddButton(new Vector2(0, -40), new Vector2(380, 64), HubText.Restart, 24, RestartFinishedSession);
            _panel.AddButton(new Vector2(0, -105), new Vector2(160, 44), HubText.BackToTop, 18, () => Show(HubScreen.Top));
            _panel.AddText(new Vector2(0, -140), new Vector2(500, 24), _status, 15, Warning);
        }

        /// <summary>Same steps as the completed ticket, new session ID, index 0, the current haptics UI setting.</summary>
        internal void RestartFinishedSession()
        {
            var finished = DemoSession.Ticket;
            if (finished == null) { Show(HubScreen.Top); return; }
            var ticket = finished.WithIndex(0, Settings.HapticsUi).WithSession(DemoSessionTicket.NewSessionId());
            if (!DemoSession.LaunchTicket(ticket, out var error)) SetStatus(HubText.LaunchFailed + error);
        }

        private void OnDestroy()
        {
            if (_panel == null) return;
            if (Application.isPlaying) Destroy(_panel.gameObject);
            else DestroyImmediate(_panel.gameObject);
        }
    }

    public static class HubIdentity
    {
        public const string PackageName = "jp.hapbeat.demohub";
        public const string ActivityName = "com.unity3d.player.UnityPlayerGameActivity";
    }
}
