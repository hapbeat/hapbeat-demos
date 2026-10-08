using System;
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
        public const string StaffWaitingNote = "スタッフ待機モード（解除は「管理」を1秒長押し）";
        public const string Arrow = " → ";
        public const string NotInstalled = "（未インストール）";
        public const string NothingInstalled = "インストール済みのデモがプランにありません。";
        public const string LaunchFailed = "起動できませんでした: ";
        public const string Preset = "プリセット";
        public const string PresetNamed = "プリセット {0}：{1}";
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
        public const string Up = "▲";
        public const string Down = "▼";
        public const string Grip = "≡";
        public const string Remove = "削除";
        public const string RetryOn = "もう一度あり";
        public const string RetryOff = "もう一度なし";
        public const string HapticsUiOn = "触覚ボタン：表示する";
        public const string HapticsUiOff = "触覚ボタン：表示しない";
        public const string StaffWaitingOn = "スタッフ待機モード：ON";
        public const string StaffWaitingOff = "スタッフ待機モード：OFF";
        public const string HandStyleGhost = "手の見た目：ゴースト";
        public const string HandStyleSkin = "手の見た目：肌";
        public const string RecenterUiOn = "視線リセットボタン：表示する";
        public const string RecenterUiOff = "視線リセットボタン：表示しない";
        public const string Done = "完了";
        public const string SaveFailed = "保存できませんでした";
        public const string FinishedMessage = "体験は以上です。\nヘッドセットを外してください";
        public const string Restart = "最初から（同じプラン）";
        public const string BackToTop = "トップへ";
        public const string PlanFull = "プランは最大 32 件です";
        public const string DeviceAddress = "この端末: プレイヤー {0} / グループ {1}";
        public const string Unspecified = "指定なし";
        public const string StartRejected = "外部からの開始を受け付けませんでした: ";
        public const string StartNotInstalled = "未インストールです: ";
        public const string StartPresetEmpty = "プリセット {0} にインストール済みのデモがありません";
        public const string StartStep = "ステップ {0}: ";

        public static IEnumerable<string> All => typeof(HubText).GetFields()
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue());
    }

    /// <summary>
    /// Self-service launcher: the top screen starts a shown preset or a single demo tile, the manage
    /// screen (1 s long press) edits the three presets, what the top screen shows and the shared settings,
    /// and the finish screen follows a completed multi-step session. With staff waiting mode on, the top
    /// screen is the M5 waiting room instead. M5 SWITCH keeps working in every screen.
    /// Every screen shows everything at once (no pages) on one world-space panel that is placed once and
    /// then stays put; the package's 視線をリセット button (shown when the manage screen turns it on) and a
    /// system recenter place it in front of the head again. A controller reads and changes the presets and the Hub-wide settings,
    /// starts sessions and leaves the manage or finish screen over Demo Switch (<see cref="IDemoSwitchHubHost"/>); only
    /// `hub_top` is accepted while the manage screen is open.
    /// </summary>
    public sealed class DemoHubController : MonoBehaviour, IDemoSwitchHubHost
    {
        public enum HubScreen { Top, Manage, Finished }

        /// <summary>Manage-screen tab after the three presets.</summary>
        internal const int TilesTab = HubPlanStore.PresetCount;
        /// <summary>Plan rows per column; a second column holds steps 17-32.</summary>
        internal const int RowsPerColumn = 16;
        /// <summary>Smallest pokable height (mm) of every manage-screen row and button.</summary>
        internal const float RowHeight = 40f;
        /// <summary>Rows the step column needs for its hint when no step is selected.</summary>
        private const int HintRows = 3;
        private const float RowGap = 3f;
        private const float Margin = 16f;
        private const float ColumnGap = 12f;
        private const float ButtonGap = 4f;
        private const float CatalogWidth = 200f;
        private const float GripWidth = 36f;
        private const float StepTitleWidth = 220f;
        private const float MoveButtonWidth = 44f;
        private const float RemoveButtonWidth = 56f;
        internal const float PlanColumnWidth = GripWidth + ButtonGap + StepTitleWidth + 2f * (ButtonGap + MoveButtonWidth) + ButtonGap + RemoveButtonWidth;
        private const float EditorWidth = 210f;
        private const float TabWidth = 128f;
        private const float BarHeight = 44f;
        private const float HeadingHeight = 26f;
        private const float StatusHeight = 22f;
        private const float FooterButtonWidth = 190f;
        private const float DoneWidth = 120f;
        private const float FooterGap = 8f;
        /// <summary>Long press on a row's grip before it can be dragged.</summary>
        internal const float DragHoldSeconds = 0.5f;
        private const float IndicatorHeight = 4f;
        /// <summary>Glyph raster density: about the eye buffer's at 0.6 m on Quest (text has no mipmaps).</summary>
        internal const float GlyphPixelsPerMillimetre = 2.5f;
        private const float TileLabelWidth = 380f;
        private const float TileToggleWidth = 150f;
        private const int TileColumns = 3;
        private const float TopWidth = 640f;
        private const float PresetHeight = 66f;
        private const float TileWidth = 192f;
        private const float TileHeight = 56f;
        private const float TopGap = 10f;
        private const float RowFlashSeconds = 0.5f;
        private static readonly Color Muted = new Color(0.7f, 0.8f, 0.9f);
        private static readonly Color Warning = new Color(1f, 0.6f, 0.45f);
        private static readonly Color Indicator = new Color(0.55f, 0.9f, 1f, 1f);

        [Tooltip("Head-locked waiting labels; shown only on the top screen in staff waiting mode.")]
        [SerializeField] private GameObject _waitingMessage;

        private readonly HubPlan[] _presets = new HubPlan[HubPlanStore.PresetCount];
        private readonly HubHoldGesture _hold = new HubHoldGesture();
        private readonly HubHoldGesture _dragHold = new HubHoldGesture(DragHoldSeconds);
        private readonly List<DemoSessionButton> _grips = new List<DemoSessionButton>();
        private DemoSessionPanel _panel;
        private HubScreen? _builtScreen;
        private int _dragCandidate = -1;
        private int _dragFrom = -1;
        private int _dropSlot = -1;
        private UnityEngine.UI.Image _dropIndicator;
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
        /// <summary>Plan row being dragged (-1: none).</summary>
        internal int DragFrom => _dragFrom;
        /// <summary>Insertion slot under the dragged row's pointer (0 = before step 1, n = after the last).</summary>
        internal int DropSlot => _dropSlot;
        internal IReadOnlyList<DemoSessionButton> Grips => _grips;
        internal int Tab => _tab;
        /// <summary>Plan row whose options the manage screen edits (-1: none).</summary>
        internal int SelectedStep => _selectedStep;
        /// <summary>The panel has its final place (head tracking was valid); it moves again only on 視線をリセット or a system recenter.</summary>
        internal bool Placed => _placed;

        public HubPlan Preset(int number) => _presets[number - 1];

        private void Start()
        {
            _deviceAddress = DemoDeviceAddress.LoadForThisDevice();
            Initialize(HubCatalog.Load(), HubPlanStore.Default);
            DemoRecenter.Recentered += Recenter;
            DemoSwitchHub.RegisterHost(this);
            // An external start goes straight to its first demo; the top screen only shows when it is rejected.
            if (!TakeExternalStart()) Show(InitialScreen(DemoSession.Ticket));
        }

        /// <summary>
        /// A `singleTask` Hub already running gets the new Intent through onNewIntent (UnityPlayerGameActivity sets it
        /// as the current Intent) and is paused and resumed meanwhile, so the start extra is read on every return.
        /// </summary>
        private void OnApplicationPause(bool paused)
        {
            if (!paused && _catalog != null) TakeExternalStart();
        }

        /// <summary>Reads and removes the Intent's start extra (<see cref="HubStartRequest.Extra"/>) and starts it.</summary>
        private bool TakeExternalStart()
        {
            if (!DemoSession.TryTakeLaunchExtra(HubStartRequest.Extra, out var json)) return false;
            // Started from outside: the catalog may have changed since the Hub was opened.
            if (_builtScreen.HasValue) _catalog = HubCatalog.Load();
            StartExternal(json);
            return true;
        }

        /// <summary>
        /// Starts the request in <paramref name="json"/> like a top-screen start. A rejected request (invalid, or nothing
        /// installed) and a failed launch open the top screen with the reason on its status line.
        /// </summary>
        internal void StartExternal(string json)
        {
            if (!HubStartRequest.TryParse(json, out var request, out var error))
            {
                Debug.LogWarning("[Demo Hub] External start rejected: " + error);
                ShowTopWithStatus(HubText.StartRejected + error);
                return;
            }
            var ticket = request.BuildTicket(_presets, _catalog, Finish(), DemoSessionTicket.NewSessionId(), Settings, out error);
            if (ticket == null)
            {
                Debug.LogWarning("[Demo Hub] External start rejected: " + error);
                ShowTopWithStatus(HubText.StartRejected + error);
                return;
            }
            Debug.Log("[Demo Hub] External start: " + (request.Steps != null ? "plan of " + request.Steps.Count + " steps"
                : request.DemoId ?? "preset " + request.Preset));
            if (!DemoSession.LaunchTicket(ticket, out error, LaunchFailed)) LaunchFailed(error);
        }

        private void ShowTopWithStatus(string status)
        {
            if (_panel == null || Screen != HubScreen.Top) Show(HubScreen.Top);
            SetStatus(status);
        }

        internal void Initialize(IReadOnlyList<DemoSessionCatalogEntry> catalog, HubPlanStore store)
        {
            _catalog = catalog;
            _store = store;
            Settings = _store.LoadSettings();
            for (var number = 1; number <= HubPlanStore.PresetCount; number++) _presets[number - 1] = _store.LoadPreset(number);
            ApplyHandStyle();
            ApplyRecenterUi();
        }

        /// <summary>The Hub's own 視線をリセット button follows the manage-screen choice, like every launch's `recenter_ui`.</summary>
        private void ApplyRecenterUi() => DemoSession.SetRecenterUiVisible(Settings.RecenterUi);

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
            UpdateDrag(Time.realtimeSinceStartup);
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

        /// <summary>視線をリセット and a system recenter: the only moves after the first placement.</summary>
        internal void PlaceInFrontOf(Transform head)
        {
            // The participant's front, not where a head looking down at the 視線をリセット button points.
            var pose = HubPanelPlacement.Target(head.position, DemoRecenter.Front(head), head.up);
            _panel.transform.SetPositionAndRotation(pose.position, pose.rotation);
        }

        /// <summary>視線をリセット (the package's button or CONTROL `recenter`) and a system recenter (<see cref="DemoRecenter.Recentered"/>).</summary>
        private void Recenter()
        {
            var camera = Camera.main;
            if (camera != null && _panel != null) PlaceInFrontOf(camera.transform);
        }

        /// <summary>Long press on 管理: the button label shows the hold progress, 1 s opens the manage screen.</summary>
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
            if (_panel == null)
            {
                _panel = DemoSessionPanel.Create("Demo Session Hub", Vector2.one);
                _panel.GlyphPixelsPerMillimetre = GlyphPixelsPerMillimetre;
            }
            if (_waitingMessage != null) _waitingMessage.SetActive(screen == HubScreen.Top && Settings.StaffWaiting);
            Rebuild();
            var camera = Camera.main;
            TickPlacement(camera != null ? camera.transform : null, false);
            _panel.EnableInputAfter(0.4f);
        }

        internal void Rebuild()
        {
            var previousSize = _panel.Size;
            var sameScreen = _builtScreen == Screen;
            _panel.ClearContent();
            _manageButton = null;
            _hold.Reset();
            _grips.Clear();
            _dropIndicator = null;
            _dragCandidate = _dragFrom = _dropSlot = -1;
            _dragHold.Reset();
            switch (Screen)
            {
                case HubScreen.Top:
                    if (Settings.StaffWaiting) BuildStaffWaiting();
                    else BuildTop();
                    break;
                case HubScreen.Manage: BuildManage(); break;
                case HubScreen.Finished: BuildFinished(); break;
            }
            // Within the manage screen a size change keeps the top-left corner (tabs, catalog) where it was.
            if (sameScreen && Screen == HubScreen.Manage) KeepTopLeft(previousSize, _panel.Size);
            _builtScreen = Screen;
        }

        private void KeepTopLeft(Vector2 previous, Vector2 size)
        {
            if (previous == size) return;
            var t = _panel.transform;
            // Canvas units are millimetres; +X is the viewer's right and +Y up on the panel.
            t.position += (t.right * (size.x - previous.x) * 0.5f + t.up * (previous.y - size.y) * 0.5f) * 0.001f;
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
            var tileRows = Mathf.CeilToInt(items.Demos.Count / (float)TileColumns);
            var height = Margin + RowHeight + 4f + 24f + TopGap
                + items.Presets.Count * (PresetHeight + TopGap)
                + tileRows * (TileHeight + TopGap)
                + (items.IsEmpty ? 90f : 0f)
                + BarHeight + Margin;
            var size = new Vector2(TopWidth, height);
            _panel.Resize(size);
            var y = height * 0.5f - Margin - RowHeight * 0.5f;
            _panel.AddText(new Vector2(0, y), new Vector2(TopWidth - 40, RowHeight), HubText.TopTitle, 25, Color.white);
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
                    _panel.AddButton(new Vector2((column - 1) * (TileWidth + TopGap), y), new Vector2(TileWidth, TileHeight), entry.DisplayName, 20, () => StartDemo(entry));
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

        /// <summary>M5 waiting room: only the head-locked labels, this device's address and the manage button.</summary>
        private void BuildStaffWaiting()
        {
            var size = new Vector2(600, 120);
            _panel.Resize(size);
            _panel.AddText(new Vector2(-70, 28), new Vector2(420, 26), DeviceAddressLine(_deviceAddress), 16, Muted, TextAnchor.MiddleLeft);
            _panel.AddText(new Vector2(-70, -28), new Vector2(420, 44), HubText.StaffWaitingNote, 15, Muted, TextAnchor.MiddleLeft);
            _manageButton = _panel.AddButton(new Vector2(232, -28), new Vector2(104, BarHeight), HubText.Manage, 18, null);
        }

        /// <summary>"プリセット 1" (or "プリセット 1：名前" when it has a name) over the step summary.</summary>
        internal string PresetLabel(int number)
        {
            var name = _presets[number - 1].Name;
            var title = string.IsNullOrEmpty(name) ? HubText.Preset + " " + number
                : string.Format(CultureInfo.InvariantCulture, HubText.PresetNamed, number, name);
            return title + "\n" + _presets[number - 1].Summary(_catalog);
        }

        /// <summary>This device's hapbeat-device.json; a missing or invalid file shows both axes as unspecified.</summary>
        internal static string DeviceAddressLine(DemoDeviceAddress address) =>
            string.Format(CultureInfo.InvariantCulture, HubText.DeviceAddress,
                Axis(address == null ? DemoDeviceAddress.Unspecified : address.Player),
                Axis(address == null ? DemoDeviceAddress.Unspecified : address.Group));

        private static string Axis(int value) =>
            value == DemoDeviceAddress.Unspecified ? HubText.Unspecified : value.ToString(CultureInfo.InvariantCulture);

        internal void StartPreset(int number) => TryStartPreset(number, null, out _);

        /// <summary>A demo tile is a one-step session: descriptor defaults, retry, finish = this Hub.</summary>
        internal void StartDemo(DemoSessionCatalogEntry entry) =>
            Launch(HubPlan.Single(entry.Descriptor).BuildTicket(_catalog, Finish(), DemoSessionTicket.NewSessionId(), Settings.HapticsUi, Settings.HandStyle, Settings.RecenterUi));

        private static DemoSessionComponent Finish() =>
            DemoSessionCatalog.TryGetOwnComponent(out var finish) ? finish : new DemoSessionComponent(HubIdentity.PackageName, HubIdentity.ActivityName);

        private void Launch(DemoSessionTicket ticket) => TryLaunch(ticket, null, out _);

        /// <summary>
        /// Starts <paramref name="ticket"/>; a failure shows on the status line. <paramref name="onFailed"/> also receives
        /// the error when the started application does not come to the front.
        /// </summary>
        private bool TryLaunch(DemoSessionTicket ticket, Action<string> onFailed, out string error)
        {
            if (ticket == null)
            {
                error = HubText.NothingInstalled;
                SetStatus(error);
                return false;
            }
            if (DemoSession.LaunchTicket(ticket, out error, failure => { LaunchFailed(failure); onFailed?.Invoke(failure); })) return true;
            LaunchFailed(error);
            return false;
        }

        /// <summary>
        /// A start that failed, or whose application did not come to the front: the Hub stays with the error (on the top
        /// screen when an external start left it without one).
        /// </summary>
        private void LaunchFailed(string error)
        {
            if (_panel == null) Show(HubScreen.Top);
            SetStatus(HubText.LaunchFailed + error);
        }

        // --------------------------------------------------------------- manage

        /// <summary>Catalog buttons per column match the plan rows; more demos add catalog columns.</summary>
        private int CatalogColumns => Mathf.Max(1, Mathf.CeilToInt(_catalog.Count / (float)RowsPerColumn));

        /// <summary>Plan columns: a second one only past 16 steps (the widest of the three presets, so tabs keep the width).</summary>
        private int PlanColumns => _presets.Any(p => p != null && p.Steps.Count > RowsPerColumn) ? 2 : 1;

        /// <summary>
        /// Width: the catalog column(s), the plan column(s) and the selected step's column, or the footer
        /// when wider; the same on every tab. Height: as many rows as the tab's longest column needs
        /// (catalog or plan, at most 16, or the step settings), so no empty rows above the footer.
        /// </summary>
        internal Vector2 ManageSize => new Vector2(
            Mathf.Max(Margin + CatalogColumns * (CatalogWidth + ColumnGap) + PlanColumns * PlanColumnWidth + (PlanColumns - 1) * ColumnGap + ColumnGap + EditorWidth + Margin,
                Margin + 4f * (FooterButtonWidth + FooterGap) + DoneWidth + Margin,
                Margin + CatalogColumns * TilesColumnWidth - 2f * ColumnGap + Margin),
            Margin + BarHeight + 8f + HeadingHeight + GridHeight(GridRows) + 8f + BarHeight + 4f + StatusHeight + Margin);

        /// <summary>Rows of the current tab: its longest column.</summary>
        internal int GridRows
        {
            get
            {
                var catalogRows = Mathf.Min(_catalog.Count, RowsPerColumn);
                if (_tab == TilesTab) return Mathf.Max(1, catalogRows);
                var plan = EditedPlan;
                var editorRows = HintRows;
                if (_selectedStep >= 0 && _selectedStep < plan.Steps.Count)
                {
                    var entry = HubPlan.Find(_catalog, plan.Steps[_selectedStep].DemoId);
                    editorRows = 2 + (entry == null ? 0 : HubPlan.VisibleOptions(plan.Steps[_selectedStep], entry.Descriptor).Count);
                }
                return Mathf.Max(Mathf.Max(catalogRows, Mathf.Min(plan.Steps.Count, RowsPerColumn)), editorRows);
            }
        }

        private const float TilesColumnWidth = TileLabelWidth + ButtonGap + TileToggleWidth + 2f * ColumnGap;

        private static float GridHeight(int rows) => rows * (RowHeight + RowGap) - RowGap;

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

            // Footer: settings shared by every launch, then done right after them; the status line below never moves them.
            var footerY = -size.y * 0.5f + Margin + StatusHeight + 4f + BarHeight * 0.5f;
            var x = left;
            DemoSessionButton Footer(float width, string label, int fontSize, System.Action press)
            {
                var button = _panel.AddButton(new Vector2(x + width * 0.5f, footerY), new Vector2(width, BarHeight), label, fontSize, press);
                x += width + FooterGap;
                return button;
            }
            Footer(FooterButtonWidth, Settings.HapticsUi ? HubText.HapticsUiOn : HubText.HapticsUiOff, 18,
                () => { Settings.HapticsUi = !Settings.HapticsUi; SettingsChanged(); }).Highlighted = Settings.HapticsUi;
            Footer(FooterButtonWidth, Settings.RecenterUi ? HubText.RecenterUiOn : HubText.RecenterUiOff, 18, () =>
            {
                Settings.RecenterUi = !Settings.RecenterUi;
                ApplyRecenterUi();
                SettingsChanged();
            }).Highlighted = Settings.RecenterUi;
            Footer(FooterButtonWidth, Settings.StaffWaiting ? HubText.StaffWaitingOn : HubText.StaffWaitingOff, 18,
                () => { Settings.StaffWaiting = !Settings.StaffWaiting; SettingsChanged(); }).Highlighted = Settings.StaffWaiting;
            Footer(FooterButtonWidth, Settings.HandStyle == DemoHandStyle.Skin ? HubText.HandStyleSkin : HubText.HandStyleGhost, 18, () =>
            {
                Settings.HandStyle = Settings.HandStyle == DemoHandStyle.Skin ? DemoHandStyle.Ghost : DemoHandStyle.Skin;
                ApplyHandStyle();
                SettingsChanged();
            });
            Footer(DoneWidth, HubText.Done, 20, () => Show(HubScreen.Top));
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

            // Plan: numbered rows (16 per column), each with a drag grip, ▲ / ▼ and 削除.
            var planLeft = PlanLeft(size);
            var planWidth = PlanColumns * PlanColumnWidth + (PlanColumns - 1) * ColumnGap;
            _panel.AddText(new Vector2(planLeft + planWidth * 0.5f, headingY), new Vector2(planWidth, HeadingHeight),
                string.Format(CultureInfo.InvariantCulture, HubText.PlanHeading, plan.Steps.Count, HubPlan.MaxSteps), 16, Muted, TextAnchor.MiddleLeft);
            if (plan.Steps.Count == 0)
                _panel.AddText(new Vector2(planLeft + planWidth * 0.5f, gridTop - 40f), new Vector2(planWidth, 80), HubText.EmptyPlan, 18, Muted, TextAnchor.UpperLeft);
            for (var index = 0; index < plan.Steps.Count; index++)
                PlanRow(index, planLeft + (index / RowsPerColumn) * (PlanColumnWidth + ColumnGap), RowCentreY(gridTop, index % RowsPerColumn));
            _flashStep = -1;
            _dropIndicator = _panel.AddRect(Vector2.zero, new Vector2(PlanColumnWidth, IndicatorHeight), Indicator);
            _dropIndicator.gameObject.SetActive(false);

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

        private float PlanLeft(Vector2 size) => -size.x * 0.5f + Margin + CatalogColumns * (CatalogWidth + ColumnGap);

        /// <summary>One plan row: grip (long press, then drag to a new place), name (selects), ▲, ▼, 削除.</summary>
        private void PlanRow(int index, float left, float y)
        {
            var plan = EditedPlan;
            var i = index;
            // No press action: holding the grip must not rebuild the panel.
            _grips.Add(_panel.AddButton(new Vector2(left + GripWidth * 0.5f, y), new Vector2(GripWidth, RowHeight), HubText.Grip, 20, null));
            var x = left + GripWidth + ButtonGap;
            var title = _panel.AddButton(new Vector2(x + StepTitleWidth * 0.5f, y), new Vector2(StepTitleWidth, RowHeight), StepTitle(index), 16, () => SelectStep(i));
            title.Text.alignment = TextAnchor.MiddleLeft;
            title.Highlighted = index == _selectedStep;
            x += StepTitleWidth + ButtonGap + MoveButtonWidth * 0.5f;
            var up = _panel.AddButton(new Vector2(x, y), new Vector2(MoveButtonWidth, RowHeight), HubText.Up, 16, () => Move(i, i - 1));
            up.Interactable = index > 0;
            x += MoveButtonWidth + ButtonGap;
            var down = _panel.AddButton(new Vector2(x, y), new Vector2(MoveButtonWidth, RowHeight), HubText.Down, 16, () => Move(i, i + 1));
            down.Interactable = index < plan.Steps.Count - 1;
            x += MoveButtonWidth * 0.5f + ButtonGap + RemoveButtonWidth * 0.5f;
            _panel.AddButton(new Vector2(x, y), new Vector2(RemoveButtonWidth, RowHeight), HubText.Remove, 16, () => RemoveStep(i));
            if (index != _flashStep) return;
            title.FlashFor(RowFlashSeconds);
        }

        /// <summary>
        /// Drag to reorder: holding a row's grip for <see cref="DragHoldSeconds"/> (fingertip pressed in, or
        /// the trigger held) picks the row up; moving up/down shows a line at the insertion place; releasing
        /// inserts the step there (not a swap).
        /// </summary>
        internal void UpdateDrag(float now)
        {
            if (Screen != HubScreen.Manage || _tab == TilesTab || _grips.Count == 0) return;
            if (_dragFrom < 0)
            {
                var held = _grips.FindIndex(g => g.Held);
                if (held != _dragCandidate)
                {
                    _dragHold.Reset();
                    _dragCandidate = held;
                }
                if (held < 0 || !_dragHold.Update(true, now)) return;
                _dragFrom = held;
                _grips[held].Highlighted = true;
            }
            var grip = _grips[_dragFrom];
            if (!grip.Captured)
            {
                // Released: insert at the last place shown.
                var from = _dragFrom;
                var slot = _dropSlot;
                _dragFrom = _dropSlot = _dragCandidate = -1;
                _dragHold.Reset();
                if (!(slot >= 0 && Insert(from, slot)))
                {
                    grip.Highlighted = false;
                    _dropIndicator.gameObject.SetActive(false);
                }
                return;
            }
            _dropSlot = SlotAt(grip.CapturePoint);
            var column = _dropSlot > RowsPerColumn || (_dropSlot == RowsPerColumn && PlanColumnOf(grip.CapturePoint) == 1) ? 1 : 0;
            var row = _dropSlot - column * RowsPerColumn;
            var planLeft = PlanLeft(_panel.Size) + column * (PlanColumnWidth + ColumnGap);
            _dropIndicator.rectTransform.anchoredPosition = new Vector2(planLeft + PlanColumnWidth * 0.5f, GridTop - row * (RowHeight + RowGap) + RowGap * 0.5f);
            if (!_dropIndicator.gameObject.activeSelf) _dropIndicator.gameObject.SetActive(true);
        }

        private int PlanColumnOf(Vector2 point) =>
            PlanColumns > 1 && point.x >= PlanLeft(_panel.Size) + PlanColumnWidth + ColumnGap * 0.5f ? 1 : 0;

        /// <summary>Insertion slot (0..step count) nearest to a panel point in the plan columns.</summary>
        internal int SlotAt(Vector2 point)
        {
            var count = EditedPlan.Steps.Count;
            var column = PlanColumnOf(point);
            var rows = Mathf.Clamp(count - column * RowsPerColumn, 0, RowsPerColumn);
            var row = Mathf.Clamp(Mathf.RoundToInt((GridTop - point.y) / (RowHeight + RowGap)), 0, rows);
            return Mathf.Min(column * RowsPerColumn + row, count);
        }

        /// <summary>Moves step <paramref name="from"/> to insertion slot <paramref name="slot"/>; the selection follows, the moved step flashes.</summary>
        internal bool Insert(int from, int slot)
        {
            var plan = EditedPlan;
            var to = slot > from ? slot - 1 : slot;
            if (!plan.Move(from, to)) return false;
            if (_selectedStep == from) _selectedStep = to;
            else if (from < _selectedStep && _selectedStep <= to) _selectedStep--;
            else if (to <= _selectedStep && _selectedStep < from) _selectedStep++;
            _flashStep = to;
            PlanChanged();
            return true;
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
                _panel.AddText(new Vector2(0, GridTop - RowHeight * 0.5f), new Vector2(size.x - 2f * Margin, RowHeight), HubText.NoDemos, 19, Color.white);
                return;
            }
            const float columnWidth = TileLabelWidth + ButtonGap + TileToggleWidth + 2f * ColumnGap;
            for (var index = 0; index < _catalog.Count; index++)
            {
                var entry = _catalog[index];
                var columnLeft = left + (index / RowsPerColumn) * columnWidth;
                var y = RowCentreY(GridTop, index % RowsPerColumn);
                _panel.AddText(new Vector2(columnLeft + TileLabelWidth * 0.5f, y), new Vector2(TileLabelWidth, RowHeight), entry.DisplayName, 17, Color.white, TextAnchor.MiddleLeft);
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
            _status = _store.SavePreset(EditedPlan, _tab + 1) ? string.Empty : HubText.Preset + " " + (_tab + 1) + HubText.SaveFailed;
            Rebuild();
        }

        private void SettingsChanged()
        {
            _status = _store.SaveSettings(Settings) ? string.Empty : HubText.SaveFailed;
            Rebuild();
        }

        // ------------------------------------------------- Demo Switch presets

        /// <summary>Staff are editing: PRESET_SET / PRESET_START / HUB_SETTINGS_SET / HUB_START are refused (the receiver also refuses during a launch).</summary>
        public bool IsBusy => Screen == HubScreen.Manage;

        /// <summary>STATE `screen`: the top screen is `main`, the finish screen `completion`.</summary>
        public string CurrentScreen => Screen == HubScreen.Manage ? DemoSwitchScreens.Manage
            : Screen == HubScreen.Finished ? DemoSwitchScreens.Completion : DemoSwitchScreens.Main;

        public DemoSwitchPreset ReadPreset(int number)
        {
            var plan = _presets[number - 1];
            return new DemoSwitchPreset(plan.Name, Settings.VisiblePresets.Contains(number), plan.Revision, plan.PresetSteps());
        }

        public DemoSwitchPresetCheck CheckSteps(IReadOnlyList<DemoSwitchPresetStep> steps, out string demoId) => HubPlan.Check(steps, _catalog, out demoId);

        /// <summary>Stores the preset with its revision increased, `visible` as the top-screen choice, and rebuilds the screen.</summary>
        public bool TryStorePreset(int number, string name, bool visible, IReadOnlyList<DemoSwitchPresetStep> steps, out string error)
        {
            var plan = HubPlan.FromPreset(name, steps);
            plan.Revision = _presets[number - 1].Revision;
            if (!_store.SavePreset(plan, number))
            {
                error = "Could not save preset " + number + ".";
                return false;
            }
            _presets[number - 1] = plan;
            error = null;
            if (visible != Settings.VisiblePresets.Contains(number))
            {
                if (visible) Settings.VisiblePresets.Add(number);
                else Settings.VisiblePresets.Remove(number);
                if (!_store.SaveSettings(Settings)) error = "Could not save the Hub settings.";
            }
            if (_panel != null) Rebuild();
            return error == null;
        }

        public bool HasInstalledStep(int number) => _presets[number - 1].Steps.Any(s => HubPlan.Find(_catalog, s.DemoId) != null);

        /// <summary>The top screen's preset start: the Hub-wide launch settings apply.</summary>
        public bool TryStartPreset(int number, Action<string> onFailed, out string error) =>
            TryLaunch(_presets[number - 1].BuildTicket(_catalog, Finish(), DemoSessionTicket.NewSessionId(), Settings.HapticsUi, Settings.HandStyle, Settings.RecenterUi),
                onFailed, out error);

        // ------------------------------------------ Demo Switch Hub settings

        /// <summary>The manage screen's settings, this device's address as read at start, and every catalog entry with its tile choice.</summary>
        public DemoSwitchHubSettings ReadSettings() => new DemoSwitchHubSettings(Settings.Revision, Settings.HapticsUi, Settings.RecenterUi,
            Settings.HandStyle, Settings.StaffWaiting,
            _deviceAddress == null ? DemoDeviceAddress.Unspecified : _deviceAddress.Player,
            _deviceAddress == null ? DemoDeviceAddress.Unspecified : _deviceAddress.Group,
            _catalog.Select(e => new DemoSwitchHubDemo(e.Descriptor.DemoId, RemoteTitle(e), Settings.VisibleDemos.Contains(e.Descriptor.DemoId))).ToList());

        /// <summary>
        /// HUB_SETTINGS `title`: the display name cut to 40 code points; the demo ID when that breaks the preset name rules
        /// (empty, only spaces, control characters or U+2028 / U+2029).
        /// </summary>
        internal static string RemoteTitle(DemoSessionCatalogEntry entry)
        {
            var title = HubPlan.Truncate(entry.DisplayName ?? string.Empty, HubPlan.TitleMaxLength);
            return DemoSwitchHub.IsValidTitle(title) ? title : entry.Descriptor.DemoId;
        }

        public DemoSwitchPresetCheck CheckVisibleDemos(IReadOnlyList<string> demoIds, out string demoId)
        {
            demoId = demoIds.FirstOrDefault(id => HubPlan.Find(_catalog, id) == null);
            return demoId == null ? DemoSwitchPresetCheck.Ok : DemoSwitchPresetCheck.NotInstalled;
        }

        /// <summary>
        /// HUB_SETTINGS_SET: the four values and the tiles of the installed demos (the choice of demos that are not installed
        /// is kept), saved with the revision increased and applied at once: the Hub's own hands and 視線をリセット button, and the
        /// shown screen (top or staff waiting).
        /// </summary>
        public bool TryStoreSettings(bool hapticsUi, bool recenterUi, DemoHandStyle handStyle, bool staffWaiting, IReadOnlyList<string> visibleDemos,
            out string error)
        {
            Settings.HapticsUi = hapticsUi;
            Settings.RecenterUi = recenterUi;
            Settings.HandStyle = handStyle;
            Settings.StaffWaiting = staffWaiting;
            foreach (var entry in _catalog) Settings.VisibleDemos.Remove(entry.Descriptor.DemoId);
            Settings.VisibleDemos.UnionWith(visibleDemos);
            ApplyHandStyle();
            ApplyRecenterUi();
            error = _store.SaveSettings(Settings) ? null : "Could not save the Hub settings.";
            if (_panel != null) Show(Screen);
            return error == null;
        }

        /// <summary>CONTROL `hand_style_*`: the manage screen's hand toggle (Hub-wide, saved with the revision increased).</summary>
        public bool TrySetHandStyle(DemoHandStyle style, out string error)
        {
            Settings.HandStyle = style;
            ApplyHandStyle();
            var saved = _store.SaveSettings(Settings);
            error = saved ? null : "Could not save the Hub settings.";
            if (_panel != null)
            {
                _status = saved ? string.Empty : HubText.SaveFailed;
                Rebuild();
            }
            return saved;
        }

        /// <summary>HUB_START: the same ticket as the start extra `steps` (Hub-wide launch settings, finish = this Hub, nothing stored).</summary>
        public bool TryStartSteps(IReadOnlyList<DemoSwitchPresetStep> steps, Action<string> onFailed, out string error)
        {
            var ticket = HubStartRequest.FromSteps(steps).BuildTicket(_presets, _catalog, Finish(), DemoSessionTicket.NewSessionId(), Settings, out error);
            return ticket != null && TryLaunch(ticket, onFailed, out error);
        }

        /// <summary>CONTROL `hub_top`: 完了 on the manage screen (its changes are already saved) and トップへ on the finish screen.</summary>
        public void ShowTop()
        {
            if (_panel == null || Screen != HubScreen.Top) Show(HubScreen.Top);
        }

        // --------------------------------------------------------------- finish

        private void BuildFinished()
        {
            var size = new Vector2(540, 340);
            _panel.Resize(size);
            _panel.AddText(new Vector2(0, 50), new Vector2(500, 110), HubText.FinishedMessage, 30, Color.white);
            _panel.AddButton(new Vector2(0, -60), new Vector2(380, 64), HubText.Restart, 24, RestartFinishedSession);
            _panel.AddButton(new Vector2(0, -125), new Vector2(160, 44), HubText.BackToTop, 18, () => Show(HubScreen.Top));
            _panel.AddText(new Vector2(0, -160), new Vector2(500, 24), _status, 15, Warning);
        }

        /// <summary>The finish screen's 最初から（同じプラン）; without a finished ticket the top screen.</summary>
        internal void RestartFinishedSession()
        {
            if (DemoSession.Ticket == null) { Show(HubScreen.Top); return; }
            TryReplay(null, out _);
        }

        /// <summary>
        /// Same steps as the completed ticket, new session ID, index 0, the current haptics UI, recenter UI and hand settings
        /// (also CONTROL `hub_replay`). A failure shows on the status line; <paramref name="onFailed"/> as in <see cref="TryLaunch"/>.
        /// </summary>
        public bool TryReplay(Action<string> onFailed, out string error)
        {
            var finished = DemoSession.Ticket;
            if (finished == null)
            {
                error = "No finished session.";
                return false;
            }
            var ticket = finished.WithIndex(0, Settings.HapticsUi).WithSession(DemoSessionTicket.NewSessionId()).WithHandStyle(Settings.HandStyle)
                .WithRecenterUi(Settings.RecenterUi);
            return TryLaunch(ticket, onFailed, out error);
        }

        private void OnDestroy()
        {
            DemoRecenter.Recentered -= Recenter;
            DemoSwitchHub.UnregisterHost(this);
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
