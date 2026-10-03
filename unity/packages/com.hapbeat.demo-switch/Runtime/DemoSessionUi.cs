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

        internal static Vector3 ToWorld(Transform space, Vector3 position) => space != null ? space.TransformPoint(position) : position;

        /// <summary>The tracked left hand of the first running hand subsystem (same source as the pokes).</summary>
        internal static bool TryGetLeftHand(out XRHand hand)
        {
            SubsystemManager.GetSubsystems(Hands);
            foreach (var subsystem in Hands)
            {
                if (!subsystem.running) continue;
                hand = subsystem.leftHand;
                return hand.isTracked;
            }
            hand = default;
            return false;
        }

        /// <summary>Tracking-space parent shared with <see cref="DemoGhostHands"/> so drawn hands match the poke point.</summary>
        internal static Transform TrackingSpace()
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

    /// <summary>
    /// Short, quiet click played when a panel button is pressed (not on hover, never for a disabled button).
    /// The clip is synthesised once (no external audio asset): a 2.4 kHz tone with a 1 ms attack and a 5 ms
    /// exponential decay, 30 ms long. One 2D source survives scene loads and keeps playing while the shared
    /// pause holds Unity audio, so the pause panel's own buttons click too. Edit mode (tests) never plays it.
    /// </summary>
    internal static class DemoSessionClickSound
    {
        public const int SampleRate = 48000;
        public const float DurationSeconds = 0.03f;
        public const float FrequencyHz = 2400f;
        public const float AttackSeconds = 0.001f;
        public const float DecaySeconds = 0.005f;
        /// <summary>Peak of the clip itself.</summary>
        public const float Peak = 0.4f;
        /// <summary>Source volume: the click peaks at Peak * Volume (about -14 dBFS).</summary>
        public const float Volume = 0.5f;
        private static AudioSource _source;
        private static AudioClip _clip;

        /// <summary>The clip's mono PCM samples at <paramref name="sampleRate"/>.</summary>
        internal static float[] Samples(int sampleRate)
        {
            var count = Mathf.RoundToInt(DurationSeconds * sampleRate);
            var samples = new float[count];
            for (var index = 0; index < count; index++)
            {
                var time = (float)index / sampleRate;
                var envelope = time < AttackSeconds ? time / AttackSeconds : Mathf.Exp(-(time - AttackSeconds) / DecaySeconds);
                // The last millisecond ramps to exact silence, so the clip never ends on a step.
                var tail = Mathf.Clamp01((count - 1 - index) / (AttackSeconds * sampleRate));
                samples[index] = Peak * envelope * tail * Mathf.Sin(2f * Mathf.PI * FrequencyHz * time);
            }
            return samples;
        }

        public static void Play()
        {
            if (!Application.isPlaying) return;
            if (_source == null)
            {
                if (_clip == null)
                {
                    _clip = AudioClip.Create("Hapbeat Demo Click", Mathf.RoundToInt(DurationSeconds * SampleRate), 1, SampleRate, false);
                    _clip.SetData(Samples(SampleRate), 0);
                }
                var go = new GameObject("Hapbeat Demo Click Sound");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _source = go.AddComponent<AudioSource>();
                _source.playOnAwake = false;
                _source.spatialBlend = 0f;
                _source.volume = Volume;
                _source.ignoreListenerPause = true;
            }
            _source.PlayOneShot(_clip);
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

        public RectTransform Rect { get; }
        internal Action OnPress { get; set; }
        internal bool Hovered { get; set; }
        /// <summary>Pressed and still held this frame: a fingertip that pressed it stays past the surface, or a ray keeps the trigger down on it.</summary>
        public bool Held { get; internal set; }
        /// <summary>
        /// The pointer that pressed this button still holds this frame, also after leaving the button (a
        /// fingertip stays past the panel surface, or the trigger stays down): drag gestures.
        /// </summary>
        public bool Captured { get; internal set; }
        /// <summary>Where that pointer is on the panel (millimetres from the panel centre) while <see cref="Captured"/>.</summary>
        public Vector2 CapturePoint { get; internal set; }
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
            FlashFor(0.15f);
            DemoSessionClickSound.Play();
            OnPress?.Invoke();
        }

        /// <summary>Shows the pressed colour for <paramref name="seconds"/> (e.g. a row that a rebuild just moved).</summary>
        public void FlashFor(float seconds) => _flashUntil = Time.realtimeSinceStartup + seconds;

        public bool IsFlashing => Time.realtimeSinceStartup < _flashUntil;

        internal void Refresh()
        {
            _background.color = !Interactable ? Disabled
                : IsFlashing ? Flash
                : Hovered ? Hover
                : Highlighted ? Selected
                : Normal;
            _text.color = Interactable ? Color.white : new Color(0.45f, 0.5f, 0.56f);
        }
    }

    /// <summary>
    /// World-space panel whose buttons accept fingertip pokes and controller ray + trigger without
    /// any EventSystem. Sizes are millimetres; the canvas is scaled so one unit equals 1 mm.
    /// Every colour is drawn with <see cref="UiShaderPath"/> (ZTest Always), so the scene's models never hide
    /// a panel. The last child is a depth-only layer (<see cref="DepthShaderPath"/>, also ZTest Always):
    /// world-space UI writes no depth, and the shared hands draw after the panels (<see cref="HandSortingOrder"/>),
    /// so a hand in front of a panel covers it with its fill and outline while a hand behind it stays hidden.
    /// </summary>
    public sealed class DemoSessionPanel : MonoBehaviour
    {
        /// <summary>Canvas sorting order of every panel (in front of the scene's ordinary transparent objects).</summary>
        public const int SortingOrder = 500;
        /// <summary>Renderer sorting order of the shared hands: after the panels' colours and their depth layer.</summary>
        public const int HandSortingOrder = SortingOrder + 1;
        public const string DepthShaderPath = "HapbeatDemoSession/PanelDepth";
        public const string UiShaderPath = "HapbeatDemoSession/PanelUi";
        private const float MetresPerUnit = 0.001f;
        private const float HoverDepth = 0.08f;
        private static Material _depthMaterial;
        private static Material _uiMaterial;
        private static bool _uiMaterialFailed;
        private readonly List<DemoSessionButton> _buttons = new List<DemoSessionButton>();
        private readonly List<GameObject> _content = new List<GameObject>();
        private readonly Dictionary<int, DemoSessionPokeTracker> _pokes = new Dictionary<int, DemoSessionPokeTracker>();
        private readonly Dictionary<int, bool> _triggers = new Dictionary<int, bool>();
        private readonly Dictionary<int, DemoSessionButton> _captures = new Dictionary<int, DemoSessionButton>();
        private readonly Image[] _cursors = new Image[2];
        private RectTransform _root;
        private CanvasScaler _scaler;
        private Image _background;
        private Image _depth;
        private float _inputEnabledAt;

        public RectTransform Root => _root;
        public Vector2 Size => _root.sizeDelta;
        public IReadOnlyList<DemoSessionButton> Buttons => _buttons;

        public static DemoSessionPanel Create(string name, Vector2 sizeMillimetres)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = SortingOrder;
            var panel = go.AddComponent<DemoSessionPanel>();
            panel._scaler = go.GetComponent<CanvasScaler>();
            // Renders dynamic glyphs at a resolution that stays sharp at arm's length.
            panel._scaler.dynamicPixelsPerUnit = 4f;
            panel._root = (RectTransform)go.transform;
            panel._root.sizeDelta = sizeMillimetres;
            panel._root.localScale = Vector3.one * MetresPerUnit;
            panel._background = go.AddComponent<Image>();
            panel._background.color = new Color(0.025f, 0.04f, 0.065f, 0.97f);
            panel._background.raycastTarget = false;
            DrawOnTop(panel._background);
            for (var index = 0; index < panel._cursors.Length; index++)
            {
                var cursor = new GameObject("Ray cursor " + index, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                cursor.rectTransform.SetParent(panel._root, false);
                cursor.rectTransform.sizeDelta = new Vector2(9, 9);
                cursor.color = new Color(0.6f, 0.95f, 1f, 0.95f);
                cursor.raycastTarget = false;
                DrawOnTop(cursor);
                cursor.gameObject.SetActive(false);
                panel._cursors[index] = cursor;
            }
            panel.CreateDepthLayer();
            return panel;
        }

        /// <summary>The depth-only layer, or null when its shader is missing or unsupported.</summary>
        internal Image DepthLayer => _depth;

        /// <summary>The panels' colour material (<see cref="UiShaderPath"/>), or null when its shader is missing or unsupported.</summary>
        internal static Material OnTopMaterial
        {
            get
            {
                if (_uiMaterial != null || _uiMaterialFailed) return _uiMaterial;
                var shader = Resources.Load<Shader>(UiShaderPath);
                if (shader == null || !shader.isSupported)
                {
                    _uiMaterialFailed = true;
                    Debug.LogWarning("[Demo Session] Shader " + UiShaderPath + " is missing or unsupported; scene models may hide panels.");
                    return null;
                }
                _uiMaterial = new Material(shader) { name = "Demo Panel UI" };
                return _uiMaterial;
            }
        }

        /// <summary>Draws <paramref name="graphic"/> over the scene (see <see cref="UiShaderPath"/>).</summary>
        private static void DrawOnTop(Graphic graphic)
        {
            var material = OnTopMaterial;
            if (material != null) graphic.material = material;
        }

        private void CreateDepthLayer()
        {
            if (_depthMaterial == null)
            {
                var shader = Resources.Load<Shader>(DepthShaderPath);
                if (shader == null || !shader.isSupported)
                {
                    Debug.LogWarning("[Demo Session] Shader " + DepthShaderPath + " is missing or unsupported; hands may show through panels.");
                    return;
                }
                _depthMaterial = new Material(shader) { name = "Demo Panel Depth" };
            }
            _depth = new GameObject("Depth", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            _depth.rectTransform.SetParent(_root, false);
            _depth.rectTransform.anchorMin = Vector2.zero;
            _depth.rectTransform.anchorMax = Vector2.one;
            _depth.rectTransform.sizeDelta = Vector2.zero;
            _depth.material = _depthMaterial;
            _depth.raycastTarget = false;
        }

        /// <summary>Cursors stay on top of later content, and the depth layer after everything.</summary>
        private void KeepOverlaysLast()
        {
            foreach (var cursor in _cursors) cursor.rectTransform.SetAsLastSibling();
            if (_depth != null) _depth.rectTransform.SetAsLastSibling();
        }

        public void Resize(Vector2 sizeMillimetres) => _root.sizeDelta = sizeMillimetres;

        /// <summary>
        /// Raster density of text glyphs in pixels per millimetre (default 4). Glyphs have no mipmaps, so a
        /// density well above the eye buffer's at the viewing distance shimmers and looks jagged.
        /// </summary>
        public float GlyphPixelsPerMillimetre
        {
            get => _scaler.dynamicPixelsPerUnit;
            set => _scaler.dynamicPixelsPerUnit = value;
        }

        /// <summary>A plain coloured rectangle (e.g. an insertion line); removed with the other content.</summary>
        public Image AddRect(Vector2 centre, Vector2 size, Color color)
        {
            var image = new GameObject("Rect", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.rectTransform.SetParent(_root, false);
            image.rectTransform.sizeDelta = size;
            image.rectTransform.anchoredPosition = centre;
            image.color = color;
            image.raycastTarget = false;
            DrawOnTop(image);
            _content.Add(image.gameObject);
            KeepOverlaysLast();
            return image;
        }

        public Text AddText(Vector2 centre, Vector2 size, string text, int fontSize, Color color, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var label = CreateText("Text", _root, size, text, fontSize, color, alignment);
            label.rectTransform.anchoredPosition = centre;
            _content.Add(label.gameObject);
            KeepOverlaysLast();
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
            DrawOnTop(image);
            var text = CreateText("Label", rect, size - new Vector2(10, 4), label, fontSize, Color.white, TextAnchor.MiddleCenter);
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Mathf.Max(8, fontSize / 2);
            text.resizeTextMaxSize = fontSize;
            var button = new DemoSessionButton(rect, image, text, onPress);
            button.Refresh();
            _buttons.Add(button);
            _content.Add(go);
            KeepOverlaysLast();
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
            _captures.Clear();
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

        /// <summary>Places the panel at a scene <paramref name="anchor"/>, facing a viewer at <paramref name="headPosition"/> when the anchor asks for it.</summary>
        public void PlaceAt(DemoSessionPanelAnchor anchor, Vector3 headPosition)
        {
            var pose = anchor.ResolvePose(headPosition);
            transform.SetPositionAndRotation(pose.position, pose.rotation);
        }

        private void Update()
        {
            ProcessPointers(DemoSessionPointers.Current, Time.realtimeSinceStartup);
            foreach (var button in _buttons) button.Refresh();
            UpdateDepthLayer();
        }

        /// <summary>A panel whose background was made see-through (e.g. a floating marker) does not hide what is behind it.</summary>
        internal void UpdateDepthLayer()
        {
            if (_depth == null) return;
            var occludes = _background.color.a >= 0.5f;
            if (_depth.enabled != occludes) _depth.enabled = occludes;
        }

        internal void ProcessPointers(IReadOnlyList<DemoSessionPointer> pointers, float now)
        {
            foreach (var button in _buttons) button.Hovered = button.Held = button.Captured = false;
            // No input while another application is being started (DemoAppHandoff).
            var accepts = AcceptsInputAt(now) && !DemoAppHandoff.IsPending;
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
                    if (tracker.Update(depth, target != null) && accepts && target != null && target.Interactable)
                    {
                        pressed = target;
                        _captures[pointer.Id] = target;
                    }
                    if (tracker.Holding && accepts && target != null && target.Interactable) target.Held = true;
                    _pokes[pointer.Id] = tracker;
                    UpdateCapture(pointer.Id, tracker.Holding, local);
                    continue;
                }

                _triggers.TryGetValue(pointer.Id, out var wasHeld);
                _triggers[pointer.Id] = pointer.TriggerHeld;
                var ray = new Ray(pointer.Position, pointer.Direction);
                var plane = new Plane(_root.forward, _root.position);
                if (Vector3.Dot(pointer.Direction, _root.forward) <= 0f || !plane.Raycast(ray, out var distance) || distance > 3f)
                {
                    UpdateCapture(pointer.Id, false, Vector3.zero);
                    continue;
                }
                var hit = _root.InverseTransformPoint(ray.GetPoint(distance));
                UpdateCapture(pointer.Id, pointer.TriggerHeld, hit);
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
                if (pointer.TriggerHeld && !wasHeld && accepts && button.Interactable)
                {
                    pressed = button;
                    _captures[pointer.Id] = button;
                    UpdateCapture(pointer.Id, true, hit);
                }
            }
            for (var index = 0; index < _cursors.Length; index++)
                if (_cursors[index].gameObject.activeSelf != cursorShown[index]) _cursors[index].gameObject.SetActive(cursorShown[index]);
            // At most one activation per frame; the callback may rebuild this panel.
            pressed?.Press();
        }

        /// <summary>A pointer that pressed a button keeps it captured while it holds, wherever it is on the panel.</summary>
        private void UpdateCapture(int pointerId, bool holding, Vector3 rootLocal)
        {
            if (!_captures.TryGetValue(pointerId, out var button)) return;
            if (!holding || !_buttons.Contains(button))
            {
                _captures.Remove(pointerId);
                return;
            }
            button.Captured = true;
            button.CapturePoint = new Vector2(rootLocal.x, rootLocal.y);
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
            DrawOnTop(text);
            return text;
        }
    }

    /// <summary>"体験完了" panel shown by <see cref="DemoSession.ShowCompletion"/>, at the scene's <see cref="DemoSessionPanelAnchor"/> or in front of the HMD.</summary>
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
            // A scene's DemoSessionPanelAnchor wins; otherwise in front of the HMD. Fixed after this.
            var anchor = DemoSessionPanelAnchor.FindActive();
            if (anchor != null) panel.PlaceAt(anchor, camera != null ? camera.transform.position : anchor.transform.position - anchor.transform.forward);
            else if (camera != null) panel.PlaceInFront(camera.transform, Distance, Drop);
            panel.EnableInputAfter(InputDelaySeconds);
            return completion;
        }

        /// <summary>After a system recenter: in front of the HMD, also when it was at a scene anchor.</summary>
        internal void PlaceInFront()
        {
            var camera = Camera.main;
            if (camera != null) _panel.PlaceInFront(camera.transform, Distance, Drop);
        }

        private void LaunchForward()
        {
            _error.text = string.Empty;
            if (DemoSession.LaunchNext(out var error, ShowError)) return;
            ShowError(error);
        }

        private void ShowError(string error)
        {
            if (this == null) return;
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
            DemoHeadingPlacement.Follow(_panel.transform, TargetPose(camera.transform.position, camera.transform.forward), !_placed, Time.unscaledDeltaTime);
            _placed = true;
            _button.Label = Label(DemoSession.HapticsEnabled);
            _button.Highlighted = DemoSession.HapticsEnabled;
        }

        internal static string Label(bool enabled) => enabled ? "触覚 ON" : "触覚 OFF";

        internal static Pose TargetPose(Vector3 headPosition, Vector3 headForward) =>
            DemoHeadingPlacement.Target(headPosition, headForward, YawDegrees, PitchDegrees, Distance);

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

    /// <summary>
    /// "視線をリセット" button low-left in view, above the haptics button (yaw -30°, pitch -27°, 0.45 m), slowly
    /// following the head's heading. Visible while <see cref="DemoSession.RecenterUiVisible"/> (default hidden),
    /// in every runtime including the Hub. Press: <see cref="DemoRecenter.ResetView"/>.
    /// </summary>
    internal sealed class DemoSessionRecenterButton : MonoBehaviour
    {
        public const string Label = "視線をリセット";
        public const float YawDegrees = -30f;
        /// <summary>The haptics button is at 35° (±3.4° tall at 0.45 m); this one (±3.4°) sits above it with a gap.</summary>
        public const float PitchDegrees = 27f;
        public const float Distance = 0.45f;
        private DemoSessionPanel _panel;
        private bool _placed;

        internal DemoSessionPanel Panel => _panel;

        private void Update()
        {
            var camera = Camera.main;
            var show = DemoSession.RecenterUiVisible && camera != null;
            if (!show)
            {
                if (_panel != null && _panel.gameObject.activeSelf) _panel.gameObject.SetActive(false);
                _placed = false;
                return;
            }
            if (_panel == null) Build();
            if (!_panel.gameObject.activeSelf) _panel.gameObject.SetActive(true);
            DemoHeadingPlacement.Follow(_panel.transform, TargetPose(camera.transform.position, camera.transform.forward), !_placed, Time.unscaledDeltaTime);
            _placed = true;
        }

        internal static Pose TargetPose(Vector3 headPosition, Vector3 headForward) =>
            DemoHeadingPlacement.Target(headPosition, headForward, YawDegrees, PitchDegrees, Distance);

        private void Build()
        {
            // Fixed width for the label at the haptics button's font size.
            _panel = DemoSessionPanel.Create("Hapbeat Recenter Button", new Vector2(172, 54));
            _panel.AddButton(Vector2.zero, new Vector2(164, 46), Label, 22, DemoRecenter.ResetView);
            if (Application.isPlaying) DontDestroyOnLoad(_panel.gameObject);
        }

        private void OnDestroy()
        {
            if (_panel == null) return;
            if (Application.isPlaying) Destroy(_panel.gameObject);
            else DestroyImmediate(_panel.gameObject);
        }
    }

    /// <summary>
    /// Heading-relative placement of small head-following controls (the haptics and 視線をリセット buttons):
    /// a direction turned by yaw (negative = left) and lowered by pitch from the head's heading, at a distance.
    /// Head pitch is ignored, so looking down at a control keeps it still.
    /// </summary>
    public static class DemoHeadingPlacement
    {
        /// <summary>How quickly a control catches up with the heading (1/s).</summary>
        public const float FollowRate = 2.5f;

        public static Pose Target(Vector3 headPosition, Vector3 headForward, float yawDegrees, float pitchDegrees, float distance)
        {
            var heading = Vector3.ProjectOnPlane(headForward, Vector3.up);
            if (heading.sqrMagnitude < 0.0001f) heading = Vector3.forward;
            heading.Normalize();
            var direction = Quaternion.AngleAxis(yawDegrees, Vector3.up) * heading;
            direction = Quaternion.AngleAxis(pitchDegrees, Vector3.Cross(Vector3.up, direction)) * direction;
            var position = headPosition + direction * distance;
            return new Pose(position, Quaternion.LookRotation(position - headPosition, Vector3.up));
        }

        /// <summary>Moves <paramref name="control"/> toward <paramref name="target"/> (at once when <paramref name="snap"/>).</summary>
        public static void Follow(Transform control, Pose target, bool snap, float deltaTime)
        {
            if (snap)
            {
                control.SetPositionAndRotation(target.position, target.rotation);
                return;
            }
            var alpha = 1f - Mathf.Exp(-FollowRate * deltaTime);
            control.SetPositionAndRotation(Vector3.Lerp(control.position, target.position, alpha),
                Quaternion.Slerp(control.rotation, target.rotation, alpha));
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
            foreach (var activity in platform.ListLauncherActivities())
            {
                var component = activity.Component;
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
                result.Add(new DemoSessionCatalogEntry(descriptor, component.PackageName, component.ActivityName, activity.Label));
            }
            return result;
        }

        /// <summary>This runtime's own launch component (the Hub uses it as the ticket's `finish`).</summary>
        public static bool TryGetOwnComponent(out DemoSessionComponent component) => DemoSession.Platform.TryGetOwnComponent(out component);
    }
}
