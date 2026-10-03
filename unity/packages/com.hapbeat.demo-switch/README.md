# Hapbeat Demo Switch

Foreground demo applications use this package to receive controller commands on UDP 7710 and launch only locally allowlisted applications, and to run self-paced Demo Sessions. It is independent of the Hapbeat SDK and UDP 7700; only the optional per-device address applier uses the SDK when it is installed.

## Install and configure

1. Add `"com.hapbeat.demo-switch": "file:../../packages/com.hapbeat.demo-switch"` to the Unity project's `Packages/manifest.json` (adjust the relative path for that project).
2. Create `Resources/HapbeatDemoSwitchSettings.asset` with **Hapbeat > Demo Switch Settings**.
3. Set Current Demo ID, Receiver Enabled, port 7710, and each target's logical Demo ID, Android package name, and fully qualified activity name.
4. Configure the same non-empty Shared Secret in every APK and the controller. Do not commit that value. Empty secret plus Allow Unsigned is intended only for an isolated demo LAN and logs a warning.
5. Subscribe to `DemoSwitch.BeforeSwitch` for application cleanup, or call `DemoSwitch.SwitchTo("logical-demo-id")` for a local UI-driven switch.

For reproducible project setup, the package also provides an Editor command that creates or updates the settings asset while preserving its local Shared Secret:

```powershell
Unity.exe -batchmode -quit -projectPath . -executeMethod Hapbeat.DemoSwitch.Editor.DemoSwitchSettingsConfigurator.ConfigureFromCommandLine -demoSwitchCurrentDemo gloveball -demoSwitchTarget handdemo com.Hapbeat.HapticHandDemo_G2 com.unity3d.player.UnityPlayerGameActivity
```

No Scene component is required. Bootstrap occurs only when `Resources/HapbeatDemoSwitchSettings.asset` exists. Editor and non-Android players never start another application; the safe adapter logs a failure.

### Return to the shared hub (0.1.0-d3)

The package's settings include the trusted local `demo_hub` destination (`jp.hapbeat.demohub` / `com.unity3d.player.UnityPlayerGameActivity`). No per-game scene edit or duplicate target entry is required. A normal authenticated `SWITCH` to `demo_hub` uses the existing sequence protection, ACK/READY/FAILED and local launch adapter. Application UI may call `DemoSwitch.ReturnToHub()`. Since 0.1.0-d11 every start of another application ends with the hand-over below (this application finishes once the started one is in front).

Controller firmware 0.1.0-d5 sends this command when **A+C are held together for one second**; A/B/C short actions and B-hold Wi-Fi remain available. Install the hub APK and rebuild/install **each** participating demo with this package before using the gesture. Updating package source or firmware alone does not update already installed APKs. Preserve old demo gameplay versions when rebuilding; do not use the new game's source under an old package ID merely to update switching.

### In-application controls (0.1.0-d4)

The package receives authenticated `CONTROL` commands for `menu_open`, `menu_close`, `recenter`, `restart` and `scene`. The command must name the currently running demo; scene IDs are logical allowlisted identifiers, not paths. Network validation and persistent replay protection stay in this package. Add exactly one enabled MonoBehaviour implementing `IDemoAppControls` to each participating scene. Its `CanExecuteControl` must be side-effect-free; its `ExecuteControl` enumerator performs the operation on Unity's main thread. READY follows completion and a scene initialization frame; exceptions produce FAILED. Opening/closing the menu is explicit, never toggle. Unsupported apps reject CONTROL; updating the package alone cannot discover a game's private menu API.

Firmware d6 provides pages for the three configured apps, menu/reposition, Volley scenes, and reload/hub/discovery tools. A/B/C short press activates the visible entry, A/C hold changes page, B hold retains Wi-Fi, and A+C hold returns to the hub. Volley maps `receive`, `spike`, `block`; other scene catalogs and additional configurable app slots are not yet exposed in the Web tool. Both firmware and APK must be updated; already installed APKs do not acquire controls automatically. The current Volley adapter restores pause/input state before scene changes. No synthetic controller input or privileged OS operation is used.

App-space recenter can reuse `XrStartAlignment` with an explicit scene anchor/XR Origin adapter, preserving floor height. This is not an OS boundary or global Oculus recenter. `XRInputSubsystem.TryRecenter()` is runtime/tracking-origin dependent and can return false; never report success unconditionally. A default scene reload is suitable only for simple apps; app adapters must own reset of additive scenes, persistent managers and pause state. A full Android process restart is a separate operation and not equivalent to reloading a Unity scene.

### Demo Session (0.1.0-d5)

Self-paced sequences built in the Hub follow `hapbeat-contracts/specs/demo-session.md`. Nothing changes for SWITCH, CONTROL menus/scenes or discovery.

- **Descriptor**: ship `Assets/StreamingAssets/hapbeat-demo-session.json` (`demo_id` must equal the settings' Current Demo ID). The Hub lists only installed launcher activities whose APK assets contain a valid descriptor; package and activity always come from PackageManager.
- **Catalog names** (0.1.0-d9): each catalog entry also carries the activity's PackageManager label (`AppLabel`, the name in Quest's library); `DisplayName` is that label, or the descriptor's Japanese title when it is missing. The Hub shows and titles steps with it.
- **Hand style** (0.1.0-d9): the ticket's optional `hand_style` (`ghost` / `skin`) is parsed into `DemoSessionTicket.HandStyle`, written back only when set, and carried by `WithIndex` / `WithSession` to every later step; see Shared hands.
- **Ticket**: at cold start the bootstrap reads and removes the Intent String extra `com.hapbeat.demo_session.ticket`. It enters session mode only when the ticket passes schema-equivalent validation (16384 UTF-8 bytes max) and `steps[index].demo_id` equals the current demo; otherwise it logs a warning and starts normally. Unknown or invalid option values fall back to the descriptor defaults with a warning. `DemoSession.IsActive`, `CurrentStep`, `GetOption(id)` and `Next` expose the state; it is never persisted.
- **Scene adapter**: add one `IDemoSessionHost` per scene and call `DemoSession.RegisterHost(this)` from `Start` (unregister on destroy). Registration pushes the haptics state, and in session mode `ApplyOptions`. In session mode the demo stops its automatic restart and calls `DemoSession.ShowCompletion()` when the experience completes.
- **Completion panel**: shown 0.55 m in front of the user (or at the scene's `DemoSessionPanelAnchor`, 0.1.0-d10) with "体験完了", `n / N`, "もう一度" (only when `retry`) and "次へ：<title>" or "デモを終了". Buttons ignore input for 1.0 s. While shown, `SetGameplayPaused(true)` and `CompletionShown` fire; `Closed` follows. "次へ" launches the next component explicitly (`NEW_TASK | CLEAR_TASK`) with `index + 1` and the current `haptics_ui`, then hands over (below). A failed launch, or a started application that does not come to the front, shows the error on the panel and keeps running. Editor and non-Android players only log.
- **Haptics button**: when the descriptor declares `supports.haptics_toggle` and `haptics_ui` is true, a fixed-width "触覚 ON" / "触覚 OFF" button follows the user's heading at the lower left (yaw -30°, pitch -35°, 0.45 m). Each step starts with haptics on.
- **CONTROL**: `haptics_on`, `haptics_off`, `haptics_ui_show` and `haptics_ui_hide` are handled here instead of by `IDemoAppControls`, also without a session, with the usual authentication, sequence, ACK and READY. A demo without `haptics_toggle` returns `FAILED/not_allowed`.
- **Input**: panels use their own input: XR Hands index-tip poke (arm 2 cm in front, press at the surface) and Input System XR controller pointer ray + trigger. No EventSystem or demo input stack is required. Since 0.1.0-d7 `DemoSessionButton.Held` reports a press that is still held (the pressing fingertip stays past the surface, or the trigger stays down on it), for long-press controls such as the Hub's 管理 button. Since 0.1.0-d11 `Captured` / `CapturePoint` keep following that pointer after it leaves the button (fingertip still past the surface, or trigger still down; the point is in panel millimetres), for drag gestures such as the Hub's plan reordering. Panels accept no input during a hand-over.
- **Font**: Noto Sans CJK JP (SIL OFL 1.1, `Runtime/Resources/HapbeatDemoSession/OFL.txt`) is loaded from `Resources`, so every APK using this package includes it (about 16 MB uncompressed).

### Per-device Hapbeat address (0.1.0-d6)

Follows the "device address file" section of `hapbeat-contracts/specs/demo-session.md`. `hapbeat-demos/tools/install-demos.ps1 -Group <n> [-Player <n>]` writes `{"version":1,"player":<n>,"group":<n>}` to each package's `/sdcard/Android/data/<package>/files/hapbeat-device.json`.

- **Read** (`DemoDeviceAddress`, SDK-independent): players read `Application.persistentDataPath/hapbeat-device.json` once. At most 1024 bytes, `version` 1, `player`/`group` -1 or 1..99, no other field. A missing file does nothing; an invalid one logs a warning and does nothing. The Editor reads only the file named by the environment variable `HAPBEAT_DEVICE_ADDRESS_FILE` (tests set `DemoDeviceAddress.PathOverride`).
- **Apply** (assembly `Hapbeat.DemoSwitch.HapbeatSdk`, compiled only when `com.hapbeat.sdk` is installed, so the Hub builds without the SDK): bootstraps itself before the first scene, waits for `HapbeatManager.Instance` and calls `SetAddressOverride(player, group, persist: false)` once. The SDK reads -1 as "disable this axis", so a -1 axis in the file passes the manager's current effective value instead and stays unchanged. Build-forced axes (`HapbeatConfig.buildOverride*`) are kept by the SDK. The file wins over a PlayerPrefs override restored by the SDK; later runtime UI or API changes are free. It logs `HAPBEAT_DEVICE_ADDRESS player=<n> group=<n> source=<path>` with the effective values. No demo code or scene change is needed.
- **Hub**: reads its own file and shows it on the top screen (no haptics).

### Shared hands (0.1.0-d9)

`DemoHands` draws tracked hands while hand tracking is active, so participants see where they point in apps without their own hand rendering (the Hub). It is **off by default**; leave it off in demos that draw their own hands (Boxing, Hand Demo, Volley, Energy Duel).

- **Enable**: tick **Presentation > Hands** on `Resources/HapbeatDemoSwitchSettings.asset` (YAML `_hands: 1`); **Hand Style** (`_handStyle`, 0 ghost / 1 skin) is the default look. The bootstrap adds the component to the persistent `Hapbeat Demo Switch` object; no scene change is needed. The configurator command does not change these fields.
- **Look**: the session ticket's optional `hand_style` (`ghost` / `skin`) wins, otherwise the settings' Hand Style (`DemoHands.ResolveStyle`). An application may switch at runtime with `DemoHands.Instance.SetStyle(...)` (the Hub applies its manage-screen choice). **Ghost**: dark translucent fill whose two greys blend by fresnel, a light outline outside the silhouette, fading out past the wrist (after Meta's hand representation). **Skin**: the baked skin texture under a fixed unlit lighting function, opaque, with a thin dark outline and a short wrist fade. Both are ports of Energy Duel's hand shaders (Safety Mill VR's looks), rewritten for UnityCG without a LightMode tag so they render in the built-in pipeline and in URP (as `SRPDefaultUnlit`): `Runtime/Resources/HapbeatDemoHands/Hand{Ghost,Skin,Outline}.shader`. The single submesh is drawn three times by queue: depth pre-pass (2999, the ghost shader with `_ColorMask 0`/`_ZWrite 1`), fill (3000), outline (3001).
- **Model**: Meta XR Interaction SDK's `OpenXR{Left,Right}Hand.fbx` and the skin textures `T_MetaHand_{L,R}.png` baked by Safety Mill VR, loaded from `Resources/HapbeatPrivate/MetaHands/` (constants on `DemoHands`). They may ship only inside built apps, so they live in the private `hapbeat-demo-assets` repository and reach a project through its `Assets/HapbeatPrivate` junction (see that repository's README). Each model needs one skinned submesh and every `XRHand_<joint>` transform.
- **Joints**: every mesh joint is set in world space to its XR Hands joint (rotation times a half turn about Y: Meta's joints face -Z toward the fingertips), sampled in `LateUpdate` and again in `Application.onBeforeRender` from the same subsystem and tracking space as the panels' fingertip poke. Meta's `XRHand_IndexTip` bone sits about halfway along the last phalanx (11-12 mm short of the surface) and is not the main influence of any vertex: the fingertip moves rigidly with the distal joint and ends 22.7 mm past it (`DemoHands.IndexDistalToTip`). So the index distal joint alone is drawn that far behind the tracked IndexTip along the finger, and the drawn fingertip ends at the poke point whatever the user's finger length; the poke itself is unchanged. The EditMode test skins both meshes on the CPU, checks the constant and checks the drawn tip within 1 mm of the poke point for 18 mm and 28 mm last phalanges. An untracked hand is hidden; controllers draw nothing (the panels' ray cursor stays).
- **Fallback**: when the private models (or the hand shaders) are absent, as in a public clone, the component adds the procedural `DemoGhostHands` below instead and logs it once.

### Procedural ghost hands (0.1.0-d8, fallback)

`DemoGhostHands` draws translucent, rim-lit hands generated from XR Hands joints. Since 0.1.0-d9 it is the fallback of `DemoHands` and is not enabled on its own.

- **Shape**: generated every frame from `XRHandSubsystem` joints as tapered capsules (finger chains, metacarpal spokes, knuckle row and thumb web). No hand model (FBX, Meta or XR Hands sample assets) is used. Joints that fail `TryGetPose` drop their bones; an untracked hand, or no running subsystem, hides that hand. Controllers draw nothing here (the panels' ray cursor stays as before).
- **Same fingertip as the panels**: joints are read from the same subsystem and converted through the same tracking space as the panels' index-tip poke, and each fingertip cap ends exactly at its tip joint, so the drawn index tip is the press point.
- **Shader**: `Runtime/Resources/HapbeatDemoGhostHands/GhostHand.shader` (`Hidden/Hapbeat/DemoGhostHand`) is loaded through `Resources`, so it ships in every APK using this package. It is an unlit UnityCG shader without a LightMode tag with single-pass-instanced/multiview stereo support; it renders in the built-in pipeline and in URP (as `SRPDefaultUnlit`). Two materials share it: a depth-only pre-pass (queue 2999) and the colour pass (queue 3000), so the overlapping parts blend as one surface. If the shader is missing or unsupported, a warning is logged and the component disables itself.

### Completion panel anchor (0.1.0-d10)

- **Where**: add a `DemoSessionPanelAnchor` component to an object in the scene. The completion panel then appears at that object's position instead of 0.55 m in front of the HMD; the first active and enabled anchor (by instance ID) wins, and without one nothing changes. Like every Demo Session panel it stays where it appeared (it does not follow the head).
- **Facing**: **Face User** (default on) turns the panel about the vertical axis toward the HMD when it appears, so it never shows its back. Off: the panel takes the anchor's rotation, whose +Z points away from the viewer like a world-space Canvas. The Scene view gizmo shows the panel's footprint (about 0.44 m × 0.35 m) and a short line toward the viewer.
- Move the anchor object in the scene to adjust a demo; no code change is needed.

### Shared pause (0.1.0-d10)

For demos without their own menu. **Off by default**: tick **Pause > Pause Menu** on `Resources/HapbeatDemoSwitchSettings.asset` (YAML `_pauseMenu: 1`); the bootstrap then adds `DemoPauseInput` to the persistent object. Ignored in the Hub. Leave it off in demos with their own menu (Volley, GloveBall, Boxing, T-Rex's operator menu).

- **Input** (`DemoPauseInput`, the only place that decides it): the left controller's menu (≡) button, plus the hand input chosen by **Pause Gesture** (`_pauseGesture`):
  - `SystemMenu` (0, default, option A): Quest's left-hand system menu gesture (palm toward the face, pinch). It is read from XR Hands' `MetaAimHand.left` aim flag `MetaAimFlags.MenuPressed` (OpenXR `XR_FB_hand_tracking_aim`), so the project must enable the OpenXR feature **Meta Hand Tracking Aim** for Android; without it only the controller button works.
  - `PalmPinchHold` (1, option B): the left palm faces the head (palm normal within 60°, from the wrist / middle / index / little proximal joints) while thumb and index tips are closer than 1.5 cm (3 cm once holding), continuously for 2 s; once per hold. Same thresholds as T-Rex's development pause (`DemoPalmPinchHold`).
  - Each input toggles: it opens the pause, or resumes when paused. Nothing happens while the completion panel is shown.
- **While paused** (`DemoPause`): the host gets `SetGameplayPaused(true)` and `SetHapticsEnabled(false)`, and `AudioListener.pause` is set (navigation voice and all other Unity audio). Haptics switch changes during the pause only update the state; `Resume` restores the session's haptics ON/OFF, the previous audio pause state and gameplay. `DemoPause.PausedChanged` lets a demo hold its own sequencing (e.g. a voice queue). Works with or without a session.
- **Panel**: "一時停止" with "再開", "最初からやり直す" (resume, then `IDemoSessionHost.Restart()`), in a session "次へ：<next title>" or, on the last step, "デモを終了" (0.1.0-d11, the completion panel's forward action), and "Hub に戻る", 0.55 m in front of the HMD when opened, then fixed (a recenter places it in front again); buttons accept input after 0.5 s. "Hub に戻る" appears only when PackageManager sees the settings' hub package; it starts the Hub and hands over (below); a failure shows an error on the panel. If the completion panel is requested while paused, the pause closes first. The menu input is ignored during a hand-over.
- **Android 11+ visibility**: with Pause Menu on, the package's Editor post-process adds `<queries><package android:name="<hub package>" /></queries>` to the generated manifest (`DemoSwitchAndroidManifest`), so the installed-Hub check works.

### Hand-over and recenter (0.1.0-d11)

- **Hand-over** (`DemoAppHandoff`, every route: session 次へ / デモを終了, the Hub's starts, pause 次へ and Hub に戻る, `DemoSwitch.SwitchTo` / `ReturnToHub`, and a received `SWITCH`): after the other application was started, nothing stops yet. Only when this application has gone to the background (`OnApplicationFocus(false)` or `OnApplicationPause(true)`) does it stop the 7710 listener, raise `DemoSwitch.BeforeSwitch`, turn host haptics off, pause audio and call `finishAndRemoveTask`, exactly once. If it is still in front after 5 s, the start counts as failed: the panel shows "起動したアプリが前面に出ませんでした" (a `SWITCH` gets `FAILED/launch_failed`) and it keeps running. Reason: finishing right after `startActivity` made Quest switch to the home environment, which sent the starting Hub to the background (logcat 2026-10-03: `Duplicate finish request`, `top_app_changed:backgrounded:jp.hapbeat.demohub`). A second start, CONTROL or SWITCH is refused while a hand-over is pending.
- **Recenter** (`DemoRecenter`, watched by the bootstrap): `XRInputSubsystem.trackingOriginUpdated`, or a jump of the HMD pose in tracking space within one ordinary frame (over 30° of heading or 0.25 m; frames over 50 ms are not compared), counts as a system recenter (log `DEMO_SESSION_RECENTER`). For 0.3 s after it, the open pause panel and the completion panel (also one at a scene anchor) are placed 0.55 m in front of the HMD again, without restarting their input delay; `DemoRecenter.Recentered` lets an application do the same (the Hub moves its panel). Which of the two sources fires for Quest's Meta-button long press has not been confirmed on a device yet.
- **Head-following controls**: `DemoHeadingPlacement` (heading-relative pose and follow, used by the haptics button) is public for application keys such as the Hub's 手前に移動.

## Controller flow and IP discovery

The headset IP does not need to be entered manually. When no explicit target IP is configured, broadcast a version 1 `DISCOVER` request to UDP 7710 from the controller's stable UDP port. The foreground APK authenticates it and unicasts `HERE`, including its current demo ID, back to that source endpoint. Use the discovered address only when the collection window contains one valid responding IPv4; do not choose the first response when multiple headsets answer.

Then send `SWITCH` by unicast to the selected headset IP. Expect `ACK` from the current APK and `READY` from the next APK on that same controller port. `FAILED` means the command was rejected or application launch failed. See `hapbeat-contracts/specs/demo-switch-control.md` for the canonical HMAC-SHA256 input and complete message contract.

```json
{"version":1,"type":"DISCOVER","controller_id":"m5-main","nonce":"0123456789abcdef","auth":"<64 lowercase hex HMAC-SHA256>"}
```

```json
{"version":1,"type":"SWITCH","controller_id":"m5-main","seq":42,"demo_id":"handdemo","auth":"<64 lowercase hex HMAC-SHA256>"}
```

The M5 controller is maintained in the standalone `hapbeat-demo-switch-controller-firmware` repository and is flashed/configured from the [Demo Switch controller tool](https://devtools.hapbeat.com/tools/demo-switch-controller/).
