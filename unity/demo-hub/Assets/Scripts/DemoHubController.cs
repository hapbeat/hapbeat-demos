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
        public const string TopTitle = "体験プラン";
        public const string Start = "体験を開始";
        public const string Edit = "編集";
        public const string EmptyPlan = "プランが空です。「編集」でデモを追加してください。";
        public const string NotInstalled = "（未インストール）";
        public const string NothingInstalled = "インストール済みのデモがプランにありません。";
        public const string LaunchFailed = "起動できませんでした: ";
        public const string Catalog = "カタログ（タップで追加）";
        public const string Plan = "プラン";
        public const string Up = "↑";
        public const string Down = "↓";
        public const string Remove = "×";
        public const string PagePrevious = "▲";
        public const string PageNext = "▼";
        public const string RetryOn = "もう一度あり";
        public const string RetryOff = "もう一度なし";
        public const string HapticsUiOn = "触覚ボタン：表示する";
        public const string HapticsUiOff = "触覚ボタン：表示しない";
        public const string Done = "完了";
        public const string Save = "保存";
        public const string Load = "読込";
        public const string Saved = "に保存しました";
        public const string Loaded = "を読み込みました";
        public const string Empty = "は空です";
        public const string Preset = "プリセット";
        public const string Minutes = "目安 合計 約{0}分";
        public const string More = "ほか {0} 件";
        public const string FinishedMessage = "体験は以上です。\nヘッドセットを外してください";
        public const string Restart = "最初から（同じプラン）";
        public const string BackToTop = "トップへ";
        public const string PlanFull = "プランは最大 32 件です";
        public const string SaveFailed = "に保存できませんでした";
        public const string Separator = "　／　";

        public static IEnumerable<string> All => typeof(HubText).GetFields()
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue());
    }

    /// <summary>
    /// Self-paced Demo Session front end: top (plan summary + start), edit (catalog, plan, presets)
    /// and the finish screen for an all-complete ticket. The M5 SWITCH waiting room keeps working.
    /// </summary>
    public sealed class DemoHubController : MonoBehaviour
    {
        public enum HubScreen { Top, Edit, Finished }

        private const int CatalogPageSize = 5;
        private const float PlanTop = 172f;
        private const float PlanBottom = -112f;
        private static readonly Color Muted = new Color(0.7f, 0.8f, 0.9f);
        private static readonly Color Warning = new Color(1f, 0.6f, 0.45f);

        [Tooltip("Head-locked waiting labels; shown only on the top screen.")]
        [SerializeField] private GameObject _waitingMessage;

        private readonly List<int> _planPageStarts = new List<int> { 0 };
        private DemoSessionPanel _panel;
        private HubPlanStore _store;
        private IReadOnlyList<DemoSessionCatalogEntry> _catalog;
        private int _catalogPage, _planPage;
        private string _status = string.Empty;
        private bool _placed;

        public HubScreen Screen { get; private set; }
        public HubPlan Plan { get; private set; } = new HubPlan();
        internal DemoSessionPanel Panel => _panel;

        private void Start()
        {
            Initialize(HubCatalog.Load(), HubPlanStore.Default);
            Show(DemoSession.IsFinishedSession ? HubScreen.Finished : HubScreen.Top);
        }

        internal void Initialize(IReadOnlyList<DemoSessionCatalogEntry> catalog, HubPlanStore store)
        {
            _catalog = catalog;
            _store = store;
            Plan = _store.TryLoad(HubPlanStore.LastSlot, out var plan) ? plan : new HubPlan();
        }

        private void Update()
        {
            // The first pose may predate tracking; place once the headset reports a tracked pose.
            if (!_placed && _panel != null && (Application.isEditor || IsHeadTracked())) Place();
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
            if (_waitingMessage != null) _waitingMessage.SetActive(screen == HubScreen.Top);
            Rebuild();
            _placed = false;
            Place();
            _panel.EnableInputAfter(0.4f);
        }

        private void Place()
        {
            var camera = Camera.main;
            if (camera == null) return;
            // Top sits low so the head-locked waiting text above stays readable.
            if (Screen == HubScreen.Top) _panel.PlaceInFront(camera.transform, 0.6f, 0.3f);
            else _panel.PlaceInFront(camera.transform, 0.6f, 0.15f);
            _placed = Application.isEditor || IsHeadTracked();
        }

        internal void Rebuild()
        {
            _panel.ClearContent();
            switch (Screen)
            {
                case HubScreen.Top: BuildTop(); break;
                case HubScreen.Edit: BuildEdit(); break;
                case HubScreen.Finished: BuildFinished(); break;
            }
        }

        private void Changed()
        {
            _store.Save(Plan, HubPlanStore.LastSlot);
            Rebuild();
        }

        private void BuildTop()
        {
            _panel.Resize(new Vector2(540, 380));
            _panel.AddText(new Vector2(0, 160), new Vector2(500, 40), HubText.TopTitle, 30, Color.white);
            var lines = new List<string>();
            for (var index = 0; index < Plan.Steps.Count && lines.Count < 5; index++) lines.Add(Summary(index));
            if (Plan.Steps.Count > 5) lines.Add(string.Format(CultureInfo.InvariantCulture, HubText.More, Plan.Steps.Count - 5));
            var summary = Plan.Steps.Count == 0 ? HubText.EmptyPlan : string.Join("\n", lines);
            _panel.AddText(new Vector2(0, 45), new Vector2(500, 180), summary, 19, Color.white, TextAnchor.UpperLeft);
            var minutes = Plan.Minutes(_catalog);
            _panel.AddText(new Vector2(0, -68), new Vector2(500, 30),
                string.Format(CultureInfo.InvariantCulture, HubText.Minutes, minutes.ToString("0.#", CultureInfo.InvariantCulture))
                + HubText.Separator + (Plan.HapticsUi ? HubText.HapticsUiOn : HubText.HapticsUiOff), 17, Muted);
            var start = _panel.AddButton(new Vector2(-75, -122), new Vector2(330, 66), HubText.Start, 28, StartSession);
            start.Interactable = Plan.Steps.Any(s => HubPlan.Find(_catalog, s.DemoId) != null);
            _panel.AddButton(new Vector2(185, -122), new Vector2(140, 50), HubText.Edit, 20, () => Show(HubScreen.Edit));
            _panel.AddText(new Vector2(0, -170), new Vector2(500, 30), _status, 16, Warning);
        }

        private string Summary(int index)
        {
            var step = Plan.Steps[index];
            var entry = HubPlan.Find(_catalog, step.DemoId);
            var title = entry == null ? step.DemoId + HubText.NotInstalled : HubPlan.Title(step, entry.Descriptor);
            return (index + 1) + ". " + title + (step.Retry ? "（" + HubText.RetryOn + "）" : string.Empty);
        }

        internal void StartSession()
        {
            if (!DemoSessionCatalog.TryGetOwnComponent(out var finish)) finish = new DemoSessionComponent(HubIdentity.PackageName, HubIdentity.ActivityName);
            var ticket = Plan.BuildTicket(_catalog, finish, DemoSessionTicket.NewSessionId());
            if (ticket == null) { SetStatus(HubText.NothingInstalled); return; }
            if (!DemoSession.LaunchTicket(ticket, out var error)) SetStatus(HubText.LaunchFailed + error);
        }

        private void SetStatus(string status)
        {
            _status = status;
            Rebuild();
        }

        private void BuildEdit()
        {
            _panel.Resize(new Vector2(780, 500));
            // Catalog column.
            _panel.AddText(new Vector2(-270, 222), new Vector2(220, 34), HubText.Catalog, 17, Muted);
            var catalogPages = Mathf.Max(1, Mathf.CeilToInt(_catalog.Count / (float)CatalogPageSize));
            _catalogPage = Mathf.Clamp(_catalogPage, 0, catalogPages - 1);
            for (var slot = 0; slot < CatalogPageSize; slot++)
            {
                var index = _catalogPage * CatalogPageSize + slot;
                if (index >= _catalog.Count) break;
                var entry = _catalog[index];
                _panel.AddButton(new Vector2(-270, 175 - slot * 56), new Vector2(220, 48), entry.Descriptor.Title.Ja, 19, () =>
                {
                    if (!Plan.Add(entry.Descriptor)) { SetStatus(HubText.PlanFull); return; }
                    _planPage = int.MaxValue; // Show the appended step.
                    Changed();
                });
            }
            if (catalogPages > 1) PageControls(-270, -112, _catalogPage, catalogPages, page => { _catalogPage = page; Rebuild(); });

            // Plan column.
            _panel.AddText(new Vector2(60, 222), new Vector2(300, 34), HubText.Plan, 17, Muted, TextAnchor.MiddleLeft);
            LayoutPlanPages();
            _planPage = Mathf.Clamp(_planPage, 0, _planPageStarts.Count - 1);
            var y = PlanTop;
            var end = _planPage + 1 < _planPageStarts.Count ? _planPageStarts[_planPage + 1] : Plan.Steps.Count;
            for (var index = _planPageStarts[_planPage]; index < end; index++) y = PlanRow(index, y);
            if (_planPageStarts.Count > 1) PageControls(300, 222, _planPage, _planPageStarts.Count, page => { _planPage = page; Rebuild(); });

            // Footer: session haptics button, done, presets, status.
            var haptics = _panel.AddButton(new Vector2(-230, -160), new Vector2(300, 46), Plan.HapticsUi ? HubText.HapticsUiOn : HubText.HapticsUiOff, 18,
                () => { Plan.HapticsUi = !Plan.HapticsUi; Changed(); });
            haptics.Highlighted = Plan.HapticsUi;
            _panel.AddButton(new Vector2(300, -160), new Vector2(150, 46), HubText.Done, 20, () => Show(HubScreen.Top));
            for (var number = 1; number <= HubPlanStore.PresetCount; number++)
            {
                var slot = number;
                _panel.AddButton(new Vector2(-330 + (number - 1) * 112, -212), new Vector2(104, 40), HubText.Save + slot, 17, () => SavePreset(slot));
                _panel.AddButton(new Vector2(30 + (number - 1) * 112, -212), new Vector2(104, 40), HubText.Load + slot, 17, () => LoadPreset(slot));
            }
            _panel.AddText(new Vector2(0, -240), new Vector2(740, 20), _status, 15, Warning);
        }

        /// <summary>Greedy pagination: rows grow with their option chip lines.</summary>
        private void LayoutPlanPages()
        {
            _planPageStarts.Clear();
            _planPageStarts.Add(0);
            var y = PlanTop;
            for (var index = 0; index < Plan.Steps.Count; index++)
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
            var entry = HubPlan.Find(_catalog, Plan.Steps[index].DemoId);
            return (entry == null ? 0 : HubPlan.VisibleOptions(Plan.Steps[index], entry.Descriptor).Count) + 1;
        }

        private float RowHeight(int index) => 48f + Mathf.CeilToInt(ChipCount(index) / 3f) * 44f + 10f;

        private float PlanRow(int index, float top)
        {
            var step = Plan.Steps[index];
            var entry = HubPlan.Find(_catalog, step.DemoId);
            var title = entry == null ? step.DemoId + HubText.NotInstalled : entry.Descriptor.Title.Ja;
            var y = top - 22f;
            _panel.AddText(new Vector2(0, y), new Vector2(260, 40), (index + 1) + ". " + title, 19, Color.white, TextAnchor.MiddleLeft);
            var i = index;
            _panel.AddButton(new Vector2(190, y), new Vector2(50, 40), HubText.Up, 20, () => { Plan.MoveUp(i); Changed(); }).Interactable = index > 0;
            _panel.AddButton(new Vector2(250, y), new Vector2(50, 40), HubText.Down, 20, () => { Plan.MoveDown(i); Changed(); }).Interactable = index < Plan.Steps.Count - 1;
            _panel.AddButton(new Vector2(320, y), new Vector2(60, 40), HubText.Remove, 22, () => { Plan.Remove(i); Changed(); });

            var chips = new List<(string label, System.Action press, bool highlighted)>();
            if (entry != null)
                foreach (var option in HubPlan.VisibleOptions(step, entry.Descriptor))
                {
                    var id = option.Id;
                    chips.Add((option.Label.Ja + "：" + option.Find(HubPlan.Value(step, option)).Label.Ja,
                        () => { Plan.CycleOption(i, entry.Descriptor, id); Changed(); }, false));
                }
            chips.Add((step.Retry ? HubText.RetryOn : HubText.RetryOff, () => { Plan.ToggleRetry(i); Changed(); }, step.Retry));
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

        internal void SavePreset(int number)
        {
            var saved = _store.Save(Plan, HubPlanStore.PresetSlot(number));
            SetStatus(HubText.Preset + number + (saved ? HubText.Saved : HubText.SaveFailed));
        }

        internal void LoadPreset(int number)
        {
            if (!_store.TryLoad(HubPlanStore.PresetSlot(number), out var plan)) { SetStatus(HubText.Preset + number + HubText.Empty); return; }
            Plan = plan;
            _planPage = 0;
            _store.Save(Plan, HubPlanStore.LastSlot);
            SetStatus(HubText.Preset + number + HubText.Loaded);
        }

        private void BuildFinished()
        {
            _panel.Resize(new Vector2(540, 300));
            _panel.AddText(new Vector2(0, 70), new Vector2(500, 110), HubText.FinishedMessage, 30, Color.white);
            _panel.AddButton(new Vector2(0, -40), new Vector2(380, 64), HubText.Restart, 24, RestartFinishedSession);
            _panel.AddButton(new Vector2(0, -105), new Vector2(160, 44), HubText.BackToTop, 18, () => Show(HubScreen.Top));
            _panel.AddText(new Vector2(0, -140), new Vector2(500, 24), _status, 15, Warning);
        }

        /// <summary>Same steps as the completed ticket, new session ID, index 0, the plan's initial haptics UI.</summary>
        internal void RestartFinishedSession()
        {
            var finished = DemoSession.Ticket;
            if (finished == null) { Show(HubScreen.Top); return; }
            var ticket = finished.WithIndex(0, Plan.HapticsUi).WithSession(DemoSessionTicket.NewSessionId());
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
