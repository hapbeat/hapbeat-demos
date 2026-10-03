using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR.Hands;
using InputDevice = UnityEngine.InputSystem.InputDevice;

namespace Hapbeat.DemoSwitch
{
    internal static class DemoSessionFont
    {
        public const string ResourcePath = "HapbeatDemoSession/NotoSansCJKjp-Regular";
        private static Font _font;

        public static Font Get()
        {
            if (_font != null) return _font;
            _font = Resources.Load<Font>(ResourcePath);
            if (_font == null)
            {
                Debug.LogWarning("[Demo Session] Bundled Japanese font is missing; falling back to the built-in font.");
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            return _font;
        }
    }

    /// <summary>One sampled input: an index fingertip (poke) or a controller ray with its trigger.</summary>
    internal struct DemoSessionPointer
    {
        public int Id;
        public bool IsPoke;
        public Vector3 Position;
        public Vector3 Direction;
        public bool TriggerHeld;
    }

    /// <summary>
    /// Samples XR Hands index tips and Input System XR controller pointer poses once per frame,
    /// independently of any demo's own input stack. Poses are converted from tracking space through
    /// the XR Origin camera offset, or the head camera's parent when no XR Origin exists.
    /// </summary>
    internal static class DemoSessionPointers
    {
        private const float TriggerPress = 0.75f;
        private const float TriggerRelease = 0.35f;
        private static readonly List<DemoSessionPointer> Pointers = new List<DemoSessionPointer>();
        private static readonly List<XRHandSubsystem> Hands = new List<XRHandSubsystem>();
        private static readonly bool[] TriggerHeld = new bool[2];
        private static int _frame = -1;
        private static XROrigin _origin;
        private static float _nextOriginSearch;

        /// <summary>Tests replace live sampling with fixed pointers.</summary>
        internal static List<DemoSessionPointer> Override;

        public static IReadOnlyList<DemoSessionPointer> Current
        {
            get
            {
                if (Override != null) return Override;
                if (_frame == Time.frameCount) return Pointers;
                _frame = Time.frameCount;
                Sample();
                return Pointers;
            }
        }

        private static void Sample()
        {
            Pointers.Clear();
            var space = TrackingSpace();
            SubsystemManager.GetSubsystems(Hands);
            foreach (var subsystem in Hands)
            {
                if (!subsystem.running) continue;
                AddFingertip(subsystem.leftHand, 0, space);
                AddFingertip(subsystem.rightHand, 1, space);
                break;
            }
            AddController(XRController.leftHand, 0, space);
            AddController(XRController.rightHand, 1, space);
        }

        private static void AddFingertip(XRHand hand, int side, Transform space)
        {
            if (!hand.isTracked || !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var pose)) return;
            Pointers.Add(new DemoSessionPointer { Id = side, IsPoke = true, Position = ToWorld(space, pose.position) });
        }

        private static void AddController(InputDevice device, int side, Transform space)
        {
            if (device == null || !device.added) return;
            var tracked = device.TryGetChildControl<ButtonControl>("isTracked");
            if (tracked != null && !tracked.isPressed) { TriggerHeld[side] = false; return; }
            var position = device.TryGetChildControl<Vector3Control>("pointerPosition") ?? device.TryGetChildControl<Vector3Control>("devicePosition");
            var rotation = device.TryGetChildControl<QuaternionControl>("pointerRotation") ?? device.TryGetChildControl<QuaternionControl>("deviceRotation");
            if (position == null || rotation == null) return;
            var trigger = device.TryGetChildControl<AxisControl>("trigger");
            var value = trigger != null ? trigger.ReadValue() : 0f;
            TriggerHeld[side] = TriggerHeld[side] ? value > TriggerRelease : value > TriggerPress;
            var direction = rotation.ReadValue() * Vector3.forward;
            Pointers.Add(new DemoSessionPointer
            {
                Id = 2 + side,
                Position = ToWorld(space, position.ReadValue()),
                Direction = space != null ? space.TransformDirection(direction) : direction,
                TriggerHeld = TriggerHeld[side]
            });
        }

        private static Vector3 ToWorld(Transform space, Vector3 position) => space != null ? space.TransformPoint(position) : position;

        private static Transform TrackingSpace()
        {
            if (_origin == null && Time.realtimeSinceStartup >= _nextOriginSearch)
            {
                _origin = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
                _nextOriginSearch = Time.realtimeSinceStartup + 1f;
            }
            if (_origin != null && _origin.CameraFloorOffsetObject != null) return _origin.CameraFloorOffsetObject.transform;
            var camera = Camera.main;
            return camera != null ? camera.transform.parent : null;
        }
    }

    /// <summary>
    /// Fingertip press with hysteresis. The tip must first be 2 cm in front of the surface (armed);
    /// crossing the surface fires once. A tip that comes from behind the panel never fires.
    /// After a press the tip counts as holding until it leaves the surface again (long press).
    /// </summary>
    internal struct DemoSessionPokeTracker
    {
        public const float ArmDepth = -0.02f;
        public const float PressDepth = 0f;
        public const float MaxDepth = 0.06f;
        public bool Armed;
        public bool Holding;

        /// <summary><paramref name="depth"/> is metres past the surface (negative = in front).</summary>
        public bool Update(float depth, bool overTarget)
        {
            if (depth <= ArmDepth) { Armed = true; Holding = false; return false; }
            if (depth > MaxDepth) { Armed = false; Holding = false; return false; }
            if (depth < PressDepth) Holding = false;
            if (!Armed || depth < PressDepth) return false;
            Armed = false;
            Holding = overTarget;
            return overTarget;
        }
    }

    public sealed class DemoSessionButton
    {
        private static readonly Color Normal = new Color(0.12f, 0.2f, 0.27f, 1f);
        private static readonly Color Hover = new Color(0.16f, 0.42f, 0.52f, 1f);
        private static readonly Color Selected = new Color(0.08f, 0.52f, 0.58f, 1f);
        private static readonly Color Disabled = new Color(0.1f, 0.12f, 0.15f, 1f);
        private static readonly Color Flash = new Color(0.55f, 0.9f, 1f, 1f);
        private readonly Image _background;
        private readonly Text _text;
        private float _flashUntil;

        internal DemoSessionButton(RectTransform rect, Image background, Text text, Action onPress)
        {
            Rect = rect;
            _background = background;
            _text = text;
            OnPress = onPress;
        }

        internal RectTransform Rect { get; }
        internal Action OnPress { get; set; }
        internal bool Hovered { get; set; }
        /// <summary>Pressed and still held this frame: a fingertip that pressed it stays past the surface, or a ray keeps the trigger down on it.</summary>
        public bool Held { get; internal set; }
        public bool Interactable { get; set; } = true;
        /// <summary>Selected/ON look for chips and toggles.</summary>
        public bool Highlighted { get; set; }

        public string Label
        {
            get => _text.text;
            set => _text.text = value;
        }

        public Text Text => _text;

        internal void Press()
        {
            _flashUntil = Time.realtimeSinceStartup + 0.15f;
            OnPress?.Invoke();
        }

        internal void Refresh()
        {
            _background.color = !Interactable ? Disabled
                : Time.realtimeSinceStartup < _flashUntil ? Flash
                : Hovered ? Hover
                : Highlighted ? Selected
                : Normal;
            _text.color = Interactable ? Color.white : new Color(0.45f, 0.5f, 0.56f);
        }
    }

    /// <summary>
    /// World-space panel whose buttons accept fingertip pokes and controller ray + trigger without
    /// any EventSystem. Sizes are millimetres; the canvas is scaled so one unit equals 1 mm.
    /// </summary>
    public sealed class DemoSessionPanel : MonoBehaviour
    {
        private const float MetresPerUnit = 0.001f;
        private const float HoverDepth = 0.08f;
        private readonly List<DemoSessionButton> _buttons = new List<DemoSessionButton>();
        private readonly List<GameObject> _content = new List<GameObject>();
        private readonly Dictionary<int, DemoSessionPokeTracker> _pokes = new Dictionary<int, DemoSessionPokeTracker>();
        private readonly Dictionary<int, bool> _triggers = new Dictionary<int, bool>();
        private readonly Image[] _cursors = new Image[2];
        private RectTransform _root;
        private Image _background;
        private float _inputEnabledAt;

        public RectTransform Root => _root;
        public Vector2 Size => _root.sizeDelta;

        public static DemoSessionPanel Create(string name, Vector2 sizeMillimetres)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 500;
            // Renders dynamic glyphs at a resolution that stays sharp at arm's length.
            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
            var panel = go.AddComponent<DemoSessionPanel>();
            panel._root = (RectTransform)go.transform;
            panel._root.sizeDelta = sizeMillimetres;
            panel._root.localScale = Vector3.one * MetresPerUnit;
            panel._background = go.AddComponent<Image>();
            panel._background.color = new Color(0.025f, 0.04f, 0.065f, 0.97f);
            panel._background.raycastTarget = false;
            for (var index = 0; index < panel._cursors.Length; index++)
            {
                var cursor = new GameObject("Ray cursor " + index, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                cursor.rectTransform.SetParent(panel._root, false);
                cursor.rectTransform.sizeDelta = new Vector2(9, 9);
                cursor.color = new Color(0.6f, 0.95f, 1f, 0.95f);
                cursor.raycastTarget = false;
                cursor.gameObject.SetActive(false);
                panel._cursors[index] = cursor;
            }
            return panel;
        }

        public void Resize(Vector2 sizeMillimetres) => _root.sizeDelta = sizeMillimetres;

        public Text AddText(Vector2 centre, Vector2 size, string text, int fontSize, Color color, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var label = CreateText("Text", _root, size, text, fontSize, color, alignment);
            label.rectTransform.anchoredPosition = centre;
            _content.Add(label.gameObject);
            return label;
        }

        public DemoSessionButton AddButton(Vector2 centre, Vector2 size, string label, int fontSize, Action onPress)
        {
            var go = new GameObject("Button " + label, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_root, false);
            rect.sizeDelta = size;
            rect.anchoredPosition = centre;
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            var text = CreateText("Label", rect, size - new Vector2(10, 4), label, fontSize, Color.white, TextAnchor.MiddleCenter);
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Mathf.Max(8, fontSize / 2);
            text.resizeTextMaxSize = fontSize;
            var button = new DemoSessionButton(rect, image, text, onPress);
            button.Refresh();
            _buttons.Add(button);
            _content.Add(go);
            // Cursors stay on top of later content.
            foreach (var cursor in _cursors) cursor.rectTransform.SetAsLastSibling();
            return button;
        }

        /// <summary>Removes texts and buttons; the background and placement stay.</summary>
        public void ClearContent()
        {
            foreach (var go in _content)
                if (go != null)
                {
                    if (Application.isPlaying) Destroy(go);
                    else DestroyImmediate(go);
                }
            _content.Clear();
            _buttons.Clear();
            _pokes.Clear();
        }

        public void EnableInputAfter(float seconds) => _inputEnabledAt = Time.realtimeSinceStartup + seconds;
        internal bool AcceptsInputAt(float realtime) => realtime >= _inputEnabledAt;

        /// <summary>Places the panel in front of <paramref name="head"/>, facing it, lowered by <paramref name="drop"/> metres.</summary>
        public void PlaceInFront(Transform head, float distance, float drop)
        {
            if (head == null) return;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.ProjectOnPlane(head.up, Vector3.up);
            forward.Normalize();
            var position = head.position + forward * distance + Vector3.down * drop;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(position - head.position, Vector3.up));
        }

        private void Update()
        {
            ProcessPointers(DemoSessionPointers.Current, Time.realtimeSinceStartup);
            foreach (var button in _buttons) button.Refresh();
        }

        internal void ProcessPointers(IReadOnlyList<DemoSessionPointer> pointers, float now)
        {
            foreach (var button in _buttons) button.Hovered = button.Held = false;
            var accepts = AcceptsInputAt(now);
            var cursorShown = new bool[_cursors.Length];
            DemoSessionButton pressed = null;
            foreach (var pointer in pointers)
            {
                if (pointer.IsPoke)
                {
                    var local = _root.InverseTransformPoint(pointer.Position);
                    var depth = local.z * MetresPerUnit;
                    var target = ButtonAt(local);
                    if (target != null && depth > -HoverDepth && depth < DemoSessionPokeTracker.MaxDepth) target.Hovered = true;
                    _pokes.TryGetValue(pointer.Id, out var tracker);
                    if (tracker.Update(depth, target != null) && accepts && target != null && target.Interactable) pressed = target;
                    if (tracker.Holding && accepts && target != null && target.Interactable) target.Held = true;
                    _pokes[pointer.Id] = tracker;
                    continue;
                }

                _triggers.TryGetValue(pointer.Id, out var wasHeld);
                _triggers[pointer.Id] = pointer.TriggerHeld;
                var ray = new Ray(pointer.Position, pointer.Direction);
                var plane = new Plane(_root.forward, _root.position);
                if (Vector3.Dot(pointer.Direction, _root.forward) <= 0f || !plane.Raycast(ray, out var distance) || distance > 3f) continue;
                var hit = _root.InverseTransformPoint(ray.GetPoint(distance));
                if (!_root.rect.Contains(hit)) continue;
                var cursorIndex = pointer.Id - 2;
                if (cursorIndex >= 0 && cursorIndex < _cursors.Length)
                {
                    cursorShown[cursorIndex] = true;
                    _cursors[cursorIndex].rectTransform.anchoredPosition = hit;
                }
                var button = ButtonAt(hit);
                if (button == null) continue;
                button.Hovered = true;
                if (pointer.TriggerHeld && accepts && button.Interactable) button.Held = true;
                if (pointer.TriggerHeld && !wasHeld && accepts && button.Interactable) pressed = button;
            }
            for (var index = 0; index < _cursors.Length; index++)
                if (_cursors[index].gameObject.activeSelf != cursorShown[index]) _cursors[index].gameObject.SetActive(cursorShown[index]);
            // At most one activation per frame; the callback may rebuild this panel.
            pressed?.Press();
        }

        private DemoSessionButton ButtonAt(Vector3 rootLocal)
        {
            foreach (var button in _buttons)
            {
                var local = button.Rect.InverseTransformPoint(_root.TransformPoint(rootLocal));
                if (button.Rect.rect.Contains(new Vector2(local.x, local.y))) return button;
            }
            return null;
        }

        private static Text CreateText(string name, Transform parent, Vector2 size, string value, int fontSize, Color color, TextAnchor alignment)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.rectTransform.SetParent(parent, false);
            text.rectTransform.sizeDelta = size;
            text.font = DemoSessionFont.Get();
            text.fontSize = fontSize;
            text.text = value;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }
    }

    /// <summary>"体験完了" panel shown by <see cref="DemoSession.ShowCompletion"/>.</summary>
    internal sealed class DemoSessionCompletionPanel : MonoBehaviour
    {
        public const float InputDelaySeconds = 1f;
        public const float Distance = 0.55f;
        public const float Drop = 0.12f;
        private const float Width = 440f;
        private const float ButtonHeight = 68f;
        private const float ButtonGap = 14f;
        private DemoSessionPanel _panel;
        private Text _error;

        internal DemoSessionPanel Panel => _panel;
        internal DemoSessionButton RetryButton { get; private set; }
        internal DemoSessionButton ForwardButton { get; private set; }
        internal string ErrorText => _error.text;

        public static DemoSessionCompletionPanel Create(DemoSessionTicket ticket, DemoSessionNext next)
        {
            var step = ticket.Steps[ticket.Index];
            var buttons = step.Retry ? 2 : 1;
            // Fixed slots: heading, progress, buttons, and a reserved error line (no layout shift).
            var height = 28f + 46f + 34f + 18f + buttons * (ButtonHeight + ButtonGap) + 52f + 16f;
            var panel = DemoSessionPanel.Create("Hapbeat Demo Session Completion", new Vector2(Width, height));
            var completion = panel.gameObject.AddComponent<DemoSessionCompletionPanel>();
            completion._panel = panel;
            var y = height * 0.5f - 28f - 23f;
            panel.AddText(new Vector2(0, y), new Vector2(Width - 40, 46), "体験完了", 34, Color.white);
            y -= 23f + 17f;
            panel.AddText(new Vector2(0, y), new Vector2(Width - 40, 34), (ticket.Index + 1) + " / " + ticket.Steps.Count, 22, new Color(0.7f, 0.82f, 0.92f));
            y -= 17f + 18f + ButtonHeight * 0.5f;
            var size = new Vector2(Width - 60, ButtonHeight);
            if (step.Retry)
            {
                completion.RetryButton = panel.AddButton(new Vector2(0, y), size, "もう一度", 24, DemoSession.RetryFromCompletion);
                y -= ButtonHeight + ButtonGap;
            }
            var forward = next.IsFinish ? "デモを終了" : "次へ：" + next.Step.Title;
            completion.ForwardButton = panel.AddButton(new Vector2(0, y), size, forward, 24, completion.LaunchForward);
            y -= ButtonHeight * 0.5f + ButtonGap + 26f;
            completion._error = panel.AddText(new Vector2(0, y), new Vector2(Width - 40, 52), string.Empty, 17, new Color(1f, 0.55f, 0.45f));
            var camera = Camera.main;
            if (camera != null) panel.PlaceInFront(camera.transform, Distance, Drop);
            panel.EnableInputAfter(InputDelaySeconds);
            return completion;
        }

        private void LaunchForward()
        {
            _error.text = string.Empty;
            if (DemoSession.LaunchNext(out var error)) return;
            _error.text = "起動できませんでした: " + error;
            _panel.EnableInputAfter(0.5f);
        }
    }

    /// <summary>
    /// Haptics ON/OFF button low-left in view (yaw -30°, pitch -35°, 0.45 m), slowly following the
    /// head's heading. Visible while the descriptor supports the toggle and haptics UI is shown.
    /// </summary>
    internal sealed class DemoSessionHapticsButton : MonoBehaviour
    {
        public const float YawDegrees = -30f;
        public const float PitchDegrees = 35f;
        public const float Distance = 0.45f;
        private const float FollowRate = 2.5f;
        private DemoSessionPanel _panel;
        private DemoSessionButton _button;
        private bool _placed;

        internal DemoSessionPanel Panel => _panel;

        private void Update()
        {
            var camera = Camera.main;
            var show = DemoSession.SupportsHapticsToggle && DemoSession.HapticsUiVisible && camera != null;
            if (!show)
            {
                if (_panel != null && _panel.gameObject.activeSelf) _panel.gameObject.SetActive(false);
                _placed = false;
                return;
            }
            if (_panel == null) Build();
            if (!_panel.gameObject.activeSelf) _panel.gameObject.SetActive(true);
            var target = TargetPose(camera.transform.position, camera.transform.forward);
            if (!_placed)
            {
                _panel.transform.SetPositionAndRotation(target.position, target.rotation);
                _placed = true;
            }
            else
            {
                var alpha = 1f - Mathf.Exp(-FollowRate * Time.unscaledDeltaTime);
                _panel.transform.SetPositionAndRotation(Vector3.Lerp(_panel.transform.position, target.position, alpha),
                    Quaternion.Slerp(_panel.transform.rotation, target.rotation, alpha));
            }
            _button.Label = Label(DemoSession.HapticsEnabled);
            _button.Highlighted = DemoSession.HapticsEnabled;
        }

        internal static string Label(bool enabled) => enabled ? "触覚 ON" : "触覚 OFF";

        /// <summary>Heading-relative pose: head pitch is ignored so looking down at the button keeps it still.</summary>
        internal static Pose TargetPose(Vector3 headPosition, Vector3 headForward)
        {
            var heading = Vector3.ProjectOnPlane(headForward, Vector3.up);
            if (heading.sqrMagnitude < 0.0001f) heading = Vector3.forward;
            heading.Normalize();
            var direction = Quaternion.AngleAxis(YawDegrees, Vector3.up) * heading;
            direction = Quaternion.AngleAxis(PitchDegrees, Vector3.Cross(Vector3.up, direction)) * direction;
            var position = headPosition + direction * Distance;
            return new Pose(position, Quaternion.LookRotation(position - headPosition, Vector3.up));
        }

        private void Build()
        {
            // Width fits the longer "触覚 OFF" so the label never resizes the button.
            _panel = DemoSessionPanel.Create("Hapbeat Haptics Toggle", new Vector2(132, 54));
            _button = _panel.AddButton(Vector2.zero, new Vector2(124, 46), Label(true), 22,
                () => DemoSession.SetHapticsEnabled(!DemoSession.HapticsEnabled));
            if (Application.isPlaying) DontDestroyOnLoad(_panel.gameObject);
        }

        private void OnDestroy()
        {
            if (_panel == null) return;
            if (Application.isPlaying) Destroy(_panel.gameObject);
            else DestroyImmediate(_panel.gameObject);
        }
    }

    /// <summary>Installed demos for the Hub: launcher activities whose APK assets carry a valid descriptor.</summary>
    public static class DemoSessionCatalog
    {
        public static IReadOnlyList<DemoSessionCatalogEntry> LoadInstalled()
        {
            var platform = DemoSession.Platform;
            var result = new List<DemoSessionCatalogEntry>();
            var seenPackages = new HashSet<string>(StringComparer.Ordinal);
            var seenDemos = new HashSet<string>(StringComparer.Ordinal);
            platform.TryGetOwnComponent(out var own);
            foreach (var component in platform.ListLauncherActivities())
            {
                if (own != null && component.PackageName == own.PackageName) continue;
                if (!seenPackages.Add(component.PackageName)) continue;
                if (!platform.TryReadPackageAsset(component.PackageName, DemoSessionDescriptor.FileName, out var text, out _)) continue;
                if (!DemoSessionDescriptor.TryParse(text, out var descriptor, out var error))
                {
                    Debug.LogWarning("[Demo Session] " + component.PackageName + " has an invalid descriptor: " + error);
                    continue;
                }
                if (!seenDemos.Add(descriptor.DemoId))
                {
                    Debug.LogWarning("[Demo Session] Duplicate demo_id '" + descriptor.DemoId + "' in " + component.PackageName + " was ignored.");
                    continue;
                }
                result.Add(new DemoSessionCatalogEntry(descriptor, component.PackageName, component.ActivityName));
            }
            return result;
        }

        /// <summary>This runtime's own launch component (the Hub uses it as the ticket's `finish`).</summary>
        public static bool TryGetOwnComponent(out DemoSessionComponent component) => DemoSession.Platform.TryGetOwnComponent(out component);
    }
}
