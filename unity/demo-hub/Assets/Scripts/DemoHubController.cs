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
        public const string AboutMinutes = "目安 約{0}分";
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
        public const string PlanHeading = "プラン（{0} / {1}）";
        public const string EmptyPlan = "プランが空です。左のカタログからデモを追加してください。";
        public const string StepHeading = "選んだ回の設定";
        public const string StepHint = "プランの行の名前を押すと、その回のモードや「もう一度」をここで変えられます。";
        public const string Up = "上へ";
        public const string Down = "下へ";
        public const string Remove = "削除";
        public const string RetryOn = "もう一度あり";
        public const string RetryOff = "もう一度なし";
        public const string HapticsUiOn = "触覚ボタン：表示する";
        public const string HapticsUiOff = "触覚ボタン：表示しない";
        public const string StaffWaitingOn = "スタッフ待機モード：ON";
        public const string StaffWaitingOff = "スタッフ待機モード：OFF";
        public const string HandStyleGhost = "手の見た目：ゴースト";
        public const string HandStyleSkin = "手の見た目：肌";
        public const string Recenter = "手前に移動";
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
    /// screen (2 s long press) edits the three presets, what the top screen shows and the shared settings,
    /// and the finish screen follows a completed multi-step session. With staff waiting mode on, the top
    /// screen is the M5 waiting room instead. M5 SWITCH keeps working in every screen.
    /// Every screen shows everything at once (no pages) on one world-space panel that is placed once and
    /// then stays put; 手前に移動 places it in front of the head again.
    /// </summary>
    public sealed class DemoHubController : MonoBehaviour
    {
        public enum HubScreen { Top, Manage, Finished }

        /// <summary>Manage-screen tab after the three presets.</summary>
        internal const int TilesTab = HubPlanStore.PresetCount;
        /// <summary>Plan rows per column; two columns hold the 32-step maximum.</summary>
        internal const int RowsPerColumn = 16;
        internal const int PlanColumns = 2;
        /// <summary>Smallest pokable height (mm) of every manage-screen row and button.</summary>
        internal const float RowHeight = 40f;
        private const float RowGap = 3f;
        private const float Margin = 16f;
        private const float ColumnGap = 12f;
        private const float ButtonGap = 4f;
        private const float CatalogWidth = 200f;
        private const float StepTitleWidth = 220f;
        private const float StepButtonWidth = 56f;
        private const float PlanColumnWidth = StepTitleWidth + 3f * (ButtonGap + StepButtonWidth);
        private const float EditorWidth = 210f;
        private const float TabWidth = 128f;
        private const float BarHeight = 44f;
        private const float HeadingHeight = 26f;
        private const float StatusHeight = 22f;
        private const float RecenterWidth = 104f;
        private const float TileLabelWidth = 380f;
        private const float TileToggleWidth = 150f;
        private const int TileColumns = 3;
        private const float TopWidth = 640f;
        private const float PresetHeight = 66f;
        private const float TileWidth = 192f;
        private const float TileHeight = 76f;
        private const float TopGap = 10f;
        private const float RowFlashSeconds = 0.5f;
        private static readonly Color Muted = new Color(0.7f, 0.8f, 0.9f);
        private static readonly Color Warning = new Color(1f, 0.6f, 0.45f);

        [Tooltip("Head-locked waiting labels; shown only on the top screen in staff waiting mode.")]
        [SerializeField] private GameObject _waitingMessage;

        private readonly HubPlan[] _presets = new HubPlan[HubPlanStore.PresetCount];
        private readonly HubHoldGesture _hold = new HubHoldGesture();
        private DemoSessionPanel _panel;
        private HubPlanStore _store;
        private IReadOnlyList<DemoSessionCatalogEntry> _catalog;
        private int _tab;
        private int _selectedStep = -1;
        private int _flashStep = -1;
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
        /// <summary>Plan row whose options the manage screen edits (-1: none).</summary>
        internal int SelectedStep => _selectedStep;
        /// <summary>The panel has its final place (head tracking was valid); it moves again only on 手前に移動.</summary>
        internal bool Placed => _placed;

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
            ApplyHandStyle();
        }

        /// <summary>The Hub's own shared hands follow the manage-screen choice (ticket hand_style is for the demos).</summary>
        private void ApplyHandStyle()
        {
            var hands = DemoHands.Instance;
            if (hands != null) hands.SetStyle(Settings.HandStyle);
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
            TickPlacement(camera != null ? camera.transform : null, Application.isEditor || IsHeadTracked());
            UpdateManageHold(Time.realtimeSinceStartup);
        }

        /// <summary>
        /// Until the panel is placed for good, keeps it in front of <paramref name="head"/>; the first frame
        /// with <paramref name="tracked"/> fixes it there (the first poses may predate tracking).
        /// </summary>
        internal void TickPlacement(Transform head, bool tracked)
        {
            if (_placed || _panel == null || head == null) return;
            PlaceInFrontOf(head);
            _placed = tracked;
        }

        /// <summary>手前に移動: the only move after the first placement.</summary>
        internal void PlaceInFrontOf(Transform head)
        {
            var pose = HubPanelPlacement.Target(head.position, head.forward, head.up);
            _panel.transform.SetPositionAndRotation(pose.position, pose.rotation);
        }

        private void Recenter()
        {
            var camera = Camera.main;
            if (camera != null) PlaceInFrontOf(camera.transform);
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
            var camera = Camera.main;
            TickPlacement(camera != null ? camera.transform : null, false);
            _panel.EnableInputAfter(0.4f);
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

        /// <summary>The one 手前に移動 button, in the panel's top-right corner on every screen.</summary>
        private void AddRecenterButton(Vector2 panelSize)
        {
            var centre = new Vector2(panelSize.x * 0.5f - Margin - RecenterWidth * 0.5f, panelSize.y * 0.5f - Margin - RowHeight * 0.5f);
            _panel.AddButton(centre, new Vector2(RecenterWidth, RowHeight), HubText.Recenter, 15, Recenter);
        }

        // ------------------------------------------------------------------ top

        private void BuildTop()
        {
            var items = HubTopItems.Decide(Settings, _presets, _catalog);
            var tileRows = Mathf.CeilToInt(items.Demos.Count / (float)TileColumns);
            var height = Margin + RowHeight + 4f + 24f + TopGap
                + items.Presets.Count * (PresetHeight + TopGap)
                + tileRows * (TileHeight + TopGap)
                + (items.IsEmpty ? 90f : 0f)
                + BarHeight + Margin;
            var size = new Vector2(TopWidth, height);
            _panel.Resize(size);
            AddRecenterButton(size);
            var y = height * 0.5f - Margin - RowHeight * 0.5f;
            // The title stays clear of the corner button on both sides, so it remains centred.
            _panel.AddText(new Vector2(0, y), new Vector2(TopWidth - 2f * (Margin + RecenterWidth + 8f), RowHeight), HubText.TopTitle, 25, Color.white);
            y -= RowHeight * 0.5f + 4f + 12f;
            _panel.AddText(new Vector2(0, y), new Vector2(TopWidth - 40, 24), DeviceAddressLine(_deviceAddress), 16, Muted);
            y -= 12f + TopGap;

            foreach (var number in items.Presets)
            {
                y -= PresetHeight * 0.5f;
                var preset = number;
                _panel.AddButton(new Vector2(0, y), new Vector2(TopWidth - 40, PresetHeight), PresetLabel(number), 20, () => StartPreset(preset));
                y -= PresetHeight * 0.5f + TopGap;
            }

            for (var row = 0; row < tileRows; row++)
            {
                y -= TileHeight * 0.5f;
                for (var column = 0; column < TileColumns; column++)
                {
                    var index = row * TileColumns + column;
                    if (index >= items.Demos.Count) break;
                    var entry = items.Demos[index];
                    _panel.AddButton(new Vector2((column - 1) * (TileWidth + TopGap), y), new Vector2(TileWidth, TileHeight), TileLabel(entry), 20, () => StartDemo(entry));
                }
                y -= TileHeight * 0.5f + TopGap;
            }

            if (items.IsEmpty)
            {
                y -= 45f;
                _panel.AddText(new Vector2(0, y), new Vector2(TopWidth - 80, 80), HubText.NothingToShow, 22, Color.white);
                y -= 45f;
            }

            // Footer: status (launch errors) and the long-press manage button.
            y -= BarHeight * 0.5f;
            _panel.AddText(new Vector2(-70, y), new Vector2(TopWidth - 180, BarHeight), _status, 15, Warning, TextAnchor.MiddleLeft);
            _manageButton = _panel.AddButton(new Vector2(TopWidth * 0.5f - 20f - 50f, y), new Vector2(100, BarHeight), HubText.Manage, 18, null);
        }

        /// <summary>M5 waiting room: only the head-locked labels, this device's address, 手前に移動 and the manage button.</summary>
        private void BuildStaffWaiting()
        {
            var size = new Vector2(600, 120);
            _panel.Resize(size);
            AddRecenterButton(size);
            _panel.AddText(new Vector2(-70, 28), new Vector2(420, 26), DeviceAddressLine(_deviceAddress), 16, Muted, TextAnchor.MiddleLeft);
            _panel.AddText(new Vector2(-70, -28), new Vector2(420, 44), HubText.StaffWaitingNote, 15, Muted, TextAnchor.MiddleLeft);
            _manageButton = _panel.AddButton(new Vector2(232, -28), new Vector2(104, BarHeight), HubText.Manage, 18, null);
        }

        /// <summary>"プリセット 1　目安 約8分" over the step summary.</summary>
        private string PresetLabel(int number)
        {
            var plan = _presets[number - 1];
            var minutes = plan.Minutes(_catalog);
            var heading = HubText.Preset + " " + number + (minutes > 0 ? "　" + MinutesText(minutes) : string.Empty);
            return heading + "\n" + plan.Summary(_catalog);
        }

        /// <summary>"Volley" over "目安 約3分": the application name and the descriptor's estimated minutes.</summary>
        internal static string TileLabel(DemoSessionCatalogEntry entry) =>
            entry.DisplayName + (entry.Descriptor.Minutes > 0 ? "\n" + MinutesText(entry.Descriptor.Minutes.Value) : string.Empty);

        private static string MinutesText(double minutes) =>
            string.Format(CultureInfo.InvariantCulture, HubText.AboutMinutes, minutes.ToString("0.#", CultureInfo.InvariantCulture));

        /// <summary>This device's hapbeat-device.json; a missing or invalid file shows both axes as unspecified.</summary>
        internal static string DeviceAddressLine(DemoDeviceAddress address) =>
            string.Format(CultureInfo.InvariantCulture, HubText.DeviceAddress,
                Axis(address == null ? DemoDeviceAddress.Unspecified : address.Player),
                Axis(address == null ? DemoDeviceAddress.Unspecified : address.Group));

        private static string Axis(int value) =>
            value == DemoDeviceAddress.Unspecified ? HubText.Unspecified : value.ToString(CultureInfo.InvariantCulture);

        internal void StartPreset(int number) =>
            Launch(_presets[number - 1].BuildTicket(_catalog, Finish(), DemoSessionTicket.NewSessionId(), Settings.HapticsUi, Settings.HandStyle));

        /// <summary>A demo tile is a one-step session: descriptor defaults, retry, finish = this Hub.</summary>
        internal void StartDemo(DemoSessionCatalogEntry entry) =>
            Launch(HubPlan.Single(entry.Descriptor).BuildTicket(_catalog, Finish(), DemoSessionTicket.NewSessionId(), Settings.HapticsUi, Settings.HandStyle));

        private static DemoSessionComponent Finish() =>
            DemoSessionCatalog.TryGetOwnComponent(out var finish) ? finish : new DemoSessionComponent(HubIdentity.PackageName, HubIdentity.ActivityName);

        private void Launch(DemoSessionTicket ticket)
        {
            if (ticket == null) { SetStatus(HubText.NothingInstalled); return; }
            if (!DemoSession.LaunchTicket(ticket, out var error)) SetStatus(HubText.LaunchFailed + error);
        }

        // --------------------------------------------------------------- manage

        /// <summary>Catalog buttons per column match the plan rows; more demos add catalog columns.</summary>
        private int CatalogColumns => Mathf.Max(1, Mathf.CeilToInt(_catalog.Count / (float)RowsPerColumn));

        /// <summary>
        /// One size for every tab (switching tabs never resizes the panel): catalog column(s), two plan
        /// columns of 16 rows, the selected step's settings column, and the footer.
        /// </summary>
        internal Vector2 ManageSize => new Vector2(
            Margin + CatalogColumns * (CatalogWidth + ColumnGap) + PlanColumns * PlanColumnWidth + (PlanColumns - 1) * ColumnGap + ColumnGap + EditorWidth + Margin,
            Margin + BarHeight + 8f + HeadingHeight + GridHeight + 8f + BarHeight + 4f + StatusHeight + Margin);

        private static float GridHeight => RowsPerColumn * (RowHeight + RowGap) - RowGap;

        private float GridTop => ManageSize.y * 0.5f - Margin - BarHeight - 8f - HeadingHeight;

        private static float RowCentreY(float gridTop, int row) => gridTop - row * (RowHeight + RowGap) - RowHeight * 0.5f;

        private void BuildManage()
        {
            var size = ManageSize;
            _panel.Resize(size);
            var left = -size.x * 0.5f + Margin;
            var barY = size.y * 0.5f - Margin - BarHeight * 0.5f;
            // Tabs: presets 1-3 and the demo tiles; a preset tab adds its top-screen toggle.
            for (var tab = 0; tab <= TilesTab; tab++)
            {
                var index = tab;
                var label = tab == TilesTab ? HubText.Tiles : HubText.Preset + " " + (tab + 1);
                _panel.AddButton(new Vector2(left + TabWidth * 0.5f + tab * (TabWidth + ButtonGap), barY), new Vector2(TabWidth, BarHeight), label, 18, () => SelectTab(index)).Highlighted = tab == _tab;
            }
            AddRecenterButton(size);
            if (_tab == TilesTab) BuildTilesTab(size);
            else
            {
                var number = _tab + 1;
                var visible = Settings.VisiblePresets.Contains(number);
                var toggleX = left + (TilesTab + 1) * (TabWidth + ButtonGap) + 12f + 110f;
                _panel.AddButton(new Vector2(toggleX, barY), new Vector2(220, BarHeight), visible ? HubText.ShowOnTopOn : HubText.ShowOnTopOff, 18, () =>
                {
                    if (!Settings.VisiblePresets.Remove(number)) Settings.VisiblePresets.Add(number);
                    SettingsChanged();
                }).Highlighted = visible;
                BuildPresetTab(size);
            }

            // Footer: settings shared by every launch, then done; the status line below never moves them.
            var footerY = -size.y * 0.5f + Margin + StatusHeight + 4f + BarHeight * 0.5f;
            var x = left;
            DemoSessionButton Footer(float width, string label, System.Action press)
            {
                var button = _panel.AddButton(new Vector2(x + width * 0.5f, footerY), new Vector2(width, BarHeight), label, 18, press);
                x += width + 8f;
                return button;
            }
            Footer(260, Settings.HapticsUi ? HubText.HapticsUiOn : HubText.HapticsUiOff,
                () => { Settings.HapticsUi = !Settings.HapticsUi; SettingsChanged(); }).Highlighted = Settings.HapticsUi;
            Footer(260, Settings.StaffWaiting ? HubText.StaffWaitingOn : HubText.StaffWaitingOff,
                () => { Settings.StaffWaiting = !Settings.StaffWaiting; SettingsChanged(); }).Highlighted = Settings.StaffWaiting;
            Footer(260, Settings.HandStyle == DemoHandStyle.Skin ? HubText.HandStyleSkin : HubText.HandStyleGhost, () =>
            {
                Settings.HandStyle = Settings.HandStyle == DemoHandStyle.Skin ? DemoHandStyle.Ghost : DemoHandStyle.Skin;
                ApplyHandStyle();
                SettingsChanged();
            });
            _panel.AddButton(new Vector2(size.x * 0.5f - Margin - 60f, footerY), new Vector2(120, BarHeight), HubText.Done, 20, () => Show(HubScreen.Top));
            _panel.AddText(new Vector2(0, -size.y * 0.5f + Margin + StatusHeight * 0.5f), new Vector2(size.x - 2f * Margin, StatusHeight), _status, 15, Warning);
        }

        internal void SelectTab(int tab)
        {
            _tab = Mathf.Clamp(tab, 0, TilesTab);
            _selectedStep = -1;
            _status = string.Empty;
            Rebuild();
        }

        internal void SelectStep(int index)
        {
            _selectedStep = index >= 0 && index < EditedPlan.Steps.Count && index != _selectedStep ? index : -1;
            Rebuild();
        }

        private void BuildPresetTab(Vector2 size)
        {
            var plan = EditedPlan;
            var left = -size.x * 0.5f + Margin;
            var headingY = size.y * 0.5f - Margin - BarHeight - 8f - HeadingHeight * 0.5f;
            var gridTop = GridTop;

            // Catalog column(s): tap to append.
            var catalogWidth = CatalogColumns * (CatalogWidth + ColumnGap) - ColumnGap;
            _panel.AddText(new Vector2(left + catalogWidth * 0.5f, headingY), new Vector2(catalogWidth, HeadingHeight), HubText.Catalog, 16, Muted, TextAnchor.MiddleLeft);
            for (var index = 0; index < _catalog.Count; index++)
            {
                var entry = _catalog[index];
                var centre = new Vector2(left + (index / RowsPerColumn) * (CatalogWidth + ColumnGap) + CatalogWidth * 0.5f, RowCentreY(gridTop, index % RowsPerColumn));
                _panel.AddButton(centre, new Vector2(CatalogWidth, RowHeight), entry.DisplayName, 17, () =>
                {
                    if (!plan.Add(entry.Descriptor)) { SetStatus(HubText.PlanFull); return; }
                    _selectedStep = _flashStep = plan.Steps.Count - 1;
                    PlanChanged();
                });
            }

            // Plan: two columns of 16 numbered rows, each with 上へ / 下へ / 削除.
            var planLeft = left + catalogWidth + ColumnGap;
            var planWidth = PlanColumns * PlanColumnWidth + (PlanColumns - 1) * ColumnGap;
            _panel.AddText(new Vector2(planLeft + planWidth * 0.5f, headingY), new Vector2(planWidth, HeadingHeight),
                string.Format(CultureInfo.InvariantCulture, HubText.PlanHeading, plan.Steps.Count, HubPlan.MaxSteps), 16, Muted, TextAnchor.MiddleLeft);
            if (plan.Steps.Count == 0)
                _panel.AddText(new Vector2(planLeft + planWidth * 0.5f, gridTop - 40f), new Vector2(planWidth, 80), HubText.EmptyPlan, 18, Muted, TextAnchor.UpperLeft);
            for (var index = 0; index < plan.Steps.Count; index++)
                PlanRow(index, planLeft + (index / RowsPerColumn) * (PlanColumnWidth + ColumnGap), RowCentreY(gridTop, index % RowsPerColumn));
            _flashStep = -1;

            // The selected step's options and retry.
            var editorX = planLeft + planWidth + ColumnGap + EditorWidth * 0.5f;
            _panel.AddText(new Vector2(editorX, headingY), new Vector2(EditorWidth, HeadingHeight), HubText.StepHeading, 16, Muted, TextAnchor.MiddleLeft);
            BuildStepEditor(editorX, gridTop);
        }

        private string StepTitle(int index)
        {
            var step = EditedPlan.Steps[index];
            var entry = HubPlan.Find(_catalog, step.DemoId);
            return (index + 1) + ". " + (entry == null ? step.DemoId + HubText.NotInstalled : HubPlan.Title(step, entry));
        }

        private void PlanRow(int index, float left, float y)
        {
            var plan = EditedPlan;
            var i = index;
            var title = _panel.AddButton(new Vector2(left + StepTitleWidth * 0.5f, y), new Vector2(StepTitleWidth, RowHeight), StepTitle(index), 16, () => SelectStep(i));
            title.Text.alignment = TextAnchor.MiddleLeft;
            title.Highlighted = index == _selectedStep;
            var x = left + StepTitleWidth + ButtonGap + StepButtonWidth * 0.5f;
            var up = _panel.AddButton(new Vector2(x, y), new Vector2(StepButtonWidth, RowHeight), HubText.Up, 16, () => Move(i, i - 1));
            up.Interactable = index > 0;
            x += StepButtonWidth + ButtonGap;
            var down = _panel.AddButton(new Vector2(x, y), new Vector2(StepButtonWidth, RowHeight), HubText.Down, 16, () => Move(i, i + 1));
            down.Interactable = index < plan.Steps.Count - 1;
            x += StepButtonWidth + ButtonGap;
            _panel.AddButton(new Vector2(x, y), new Vector2(StepButtonWidth, RowHeight), HubText.Remove, 16, () => RemoveStep(i));
            if (index != _flashStep) return;
            title.FlashFor(RowFlashSeconds);
        }

        /// <summary>Swaps a step with its neighbour; the selection follows the moved step, which flashes.</summary>
        internal void Move(int from, int to)
        {
            var plan = EditedPlan;
            if (!(to < from ? plan.MoveUp(from) : plan.MoveDown(from))) return;
            if (_selectedStep == from) _selectedStep = to;
            else if (_selectedStep == to) _selectedStep = from;
            _flashStep = to;
            PlanChanged();
        }

        internal void RemoveStep(int index)
        {
            if (!EditedPlan.Remove(index)) return;
            if (_selectedStep == index) _selectedStep = -1;
            else if (_selectedStep > index) _selectedStep--;
            PlanChanged();
        }

        private void BuildStepEditor(float x, float gridTop)
        {
            var plan = EditedPlan;
            if (_selectedStep < 0 || _selectedStep >= plan.Steps.Count)
            {
                _panel.AddText(new Vector2(x, gridTop - 60f), new Vector2(EditorWidth, 120), HubText.StepHint, 15, Muted, TextAnchor.UpperLeft);
                return;
            }
            var i = _selectedStep;
            var step = plan.Steps[i];
            var entry = HubPlan.Find(_catalog, step.DemoId);
            _panel.AddText(new Vector2(x, RowCentreY(gridTop, 0)), new Vector2(EditorWidth, RowHeight), StepTitle(i), 16, Color.white, TextAnchor.MiddleLeft);
            var row = 1;
            if (entry != null)
                foreach (var option in HubPlan.VisibleOptions(step, entry.Descriptor))
                {
                    var id = option.Id;
                    _panel.AddButton(new Vector2(x, RowCentreY(gridTop, row++)), new Vector2(EditorWidth, RowHeight),
                        option.Label.Ja + "：" + option.Find(HubPlan.Value(step, option)).Label.Ja, 16,
                        () => { plan.CycleOption(i, entry.Descriptor, id); PlanChanged(); });
                }
            _panel.AddButton(new Vector2(x, RowCentreY(gridTop, row)), new Vector2(EditorWidth, RowHeight), step.Retry ? HubText.RetryOn : HubText.RetryOff, 16,
                () => { plan.ToggleRetry(i); PlanChanged(); }).Highlighted = step.Retry;
        }

        /// <summary>Every installed demo with its top-screen toggle, in columns of 16 rows.</summary>
        private void BuildTilesTab(Vector2 size)
        {
            var left = -size.x * 0.5f + Margin;
            var headingY = size.y * 0.5f - Margin - BarHeight - 8f - HeadingHeight * 0.5f;
            _panel.AddText(new Vector2(0, headingY), new Vector2(size.x - 2f * Margin, HeadingHeight), HubText.TilesHeading, 16, Muted, TextAnchor.MiddleLeft);
            if (_catalog.Count == 0)
            {
                _panel.AddText(new Vector2(0, GridTop - 40f), new Vector2(size.x - 2f * Margin, 40), HubText.NoDemos, 19, Color.white);
                return;
            }
            const float columnWidth = TileLabelWidth + ButtonGap + TileToggleWidth + 2f * ColumnGap;
            for (var index = 0; index < _catalog.Count; index++)
            {
                var entry = _catalog[index];
                var columnLeft = left + (index / RowsPerColumn) * columnWidth;
                var y = RowCentreY(GridTop, index % RowsPerColumn);
                var minutes = entry.Descriptor.Minutes > 0 ? "（" + MinutesText(entry.Descriptor.Minutes.Value) + "）" : string.Empty;
                _panel.AddText(new Vector2(columnLeft + TileLabelWidth * 0.5f, y), new Vector2(TileLabelWidth, RowHeight), entry.DisplayName + minutes, 17, Color.white, TextAnchor.MiddleLeft);
                var visible = Settings.VisibleDemos.Contains(entry.Descriptor.DemoId);
                _panel.AddButton(new Vector2(columnLeft + TileLabelWidth + ButtonGap + TileToggleWidth * 0.5f, y), new Vector2(TileToggleWidth, RowHeight),
                    visible ? HubText.TileOn : HubText.TileOff, 17, () =>
                    {
                        if (!Settings.VisibleDemos.Remove(entry.Descriptor.DemoId)) Settings.VisibleDemos.Add(entry.Descriptor.DemoId);
                        SettingsChanged();
                    }).Highlighted = visible;
            }
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

        // --------------------------------------------------------------- finish

        private void BuildFinished()
        {
            var size = new Vector2(540, 340);
            _panel.Resize(size);
            AddRecenterButton(size);
            _panel.AddText(new Vector2(0, 50), new Vector2(500, 110), HubText.FinishedMessage, 30, Color.white);
            _panel.AddButton(new Vector2(0, -60), new Vector2(380, 64), HubText.Restart, 24, RestartFinishedSession);
            _panel.AddButton(new Vector2(0, -125), new Vector2(160, 44), HubText.BackToTop, 18, () => Show(HubScreen.Top));
            _panel.AddText(new Vector2(0, -160), new Vector2(500, 24), _status, 15, Warning);
        }

        /// <summary>Same steps as the completed ticket, new session ID, index 0, the current haptics UI and hand settings.</summary>
        internal void RestartFinishedSession()
        {
            var finished = DemoSession.Ticket;
            if (finished == null) { Show(HubScreen.Top); return; }
            var ticket = finished.WithIndex(0, Settings.HapticsUi).WithSession(DemoSessionTicket.NewSessionId()).WithHandStyle(Settings.HandStyle);
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
