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

The package's settings include the trusted local `demo_hub` destination (`jp.hapbeat.demohub` / `com.unity3d.player.UnityPlayerGameActivity`). No per-game scene edit or duplicate target entry is required. A normal authenticated `SWITCH` to `demo_hub` uses the existing sequence protection, ACK/READY/FAILED and local launch adapter. Application UI may call `DemoSwitch.ReturnToHub()`.

Controller firmware 0.1.0-d5 sends this command when **A+C are held together for one second**; A/B/C short actions and B-hold Wi-Fi remain available. Install the hub APK and rebuild/install **each** participating demo with this package before using the gesture. Updating package source or firmware alone does not update already installed APKs. Preserve old demo gameplay versions when rebuilding; do not use the new game's source under an old package ID merely to update switching.

### In-application controls (0.1.0-d4)

The package receives authenticated `CONTROL` commands for `menu_open`, `menu_close`, `recenter`, `restart` and `scene`. The command must name the currently running demo; scene IDs are logical allowlisted identifiers, not paths. Network validation and persistent replay protection stay in this package. Add exactly one enabled MonoBehaviour implementing `IDemoAppControls` to each participating scene. Its `CanExecuteControl` must be side-effect-free; its `ExecuteControl` enumerator performs the operation on Unity's main thread. READY follows completion and a scene initialization frame; exceptions produce FAILED. Opening/closing the menu is explicit, never toggle. Unsupported apps reject CONTROL; updating the package alone cannot discover a game's private menu API.

Firmware d6 provides pages for the three configured apps, menu/reposition, Volley scenes, and reload/hub/discovery tools. A/B/C short press activates the visible entry, A/C hold changes page, B hold retains Wi-Fi, and A+C hold returns to the hub. Volley maps `receive`, `spike`, `block`; other scene catalogs and additional configurable app slots are not yet exposed in the Web tool. Both firmware and APK must be updated; already installed APKs do not acquire controls automatically. The current Volley adapter restores pause/input state before scene changes. No synthetic controller input or privileged OS operation is used.

App-space recenter can reuse `XrStartAlignment` with an explicit scene anchor/XR Origin adapter, preserving floor height. This is not an OS boundary or global Oculus recenter. `XRInputSubsystem.TryRecenter()` is runtime/tracking-origin dependent and can return false; never report success unconditionally. A default scene reload is suitable only for simple apps; app adapters must own reset of additive scenes, persistent managers and pause state. A full Android process restart is a separate operation and not equivalent to reloading a Unity scene.

### Demo Session (0.1.0-d5)

Self-paced sequences built in the Hub follow `hapbeat-contracts/specs/demo-session.md`. Nothing changes for SWITCH, CONTROL menus/scenes or discovery.

- **Descriptor**: ship `Assets/StreamingAssets/hapbeat-demo-session.json` (`demo_id` must equal the settings' Current Demo ID). The Hub lists only installed launcher activities whose APK assets contain a valid descriptor; package and activity always come from PackageManager.
- **Ticket**: at cold start the bootstrap reads and removes the Intent String extra `com.hapbeat.demo_session.ticket`. It enters session mode only when the ticket passes schema-equivalent validation (16384 UTF-8 bytes max) and `steps[index].demo_id` equals the current demo; otherwise it logs a warning and starts normally. Unknown or invalid option values fall back to the descriptor defaults with a warning. `DemoSession.IsActive`, `CurrentStep`, `GetOption(id)` and `Next` expose the state; it is never persisted.
- **Scene adapter**: add one `IDemoSessionHost` per scene and call `DemoSession.RegisterHost(this)` from `Start` (unregister on destroy). Registration pushes the haptics state, and in session mode `ApplyOptions`. In session mode the demo stops its automatic restart and calls `DemoSession.ShowCompletion()` when the experience completes.
- **Completion panel**: shown 0.55 m in front of the user with "体験完了", `n / N`, "もう一度" (only when `retry`) and "次へ：<title>" or "デモを終了". Buttons ignore input for 1.0 s. While shown, `SetGameplayPaused(true)` and `CompletionShown` fire; `Closed` follows. "次へ" launches the next component explicitly (`NEW_TASK | CLEAR_TASK`) with `index + 1` and the current `haptics_ui`; on success the runtime stops 7710, raises `DemoSwitch.BeforeSwitch`, turns host haptics off, pauses audio and calls `finishAndRemoveTask`. A failed launch shows the error on the panel and keeps running. Editor and non-Android players only log.
- **Haptics button**: when the descriptor declares `supports.haptics_toggle` and `haptics_ui` is true, a fixed-width "触覚 ON" / "触覚 OFF" button follows the user's heading at the lower left (yaw -30°, pitch -35°, 0.45 m). Each step starts with haptics on.
- **CONTROL**: `haptics_on`, `haptics_off`, `haptics_ui_show` and `haptics_ui_hide` are handled here instead of by `IDemoAppControls`, also without a session, with the usual authentication, sequence, ACK and READY. A demo without `haptics_toggle` returns `FAILED/not_allowed`.
- **Input**: panels use their own input: XR Hands index-tip poke (arm 2 cm in front, press at the surface) and Input System XR controller pointer ray + trigger. No EventSystem or demo input stack is required.
- **Font**: Noto Sans CJK JP (SIL OFL 1.1, `Runtime/Resources/HapbeatDemoSession/OFL.txt`) is loaded from `Resources`, so every APK using this package includes it (about 16 MB uncompressed).

### Per-device Hapbeat address (0.1.0-d6)

Follows the "device address file" section of `hapbeat-contracts/specs/demo-session.md`. `hapbeat-demos/tools/install-demos.ps1 -Group <n> [-Player <n>]` writes `{"version":1,"player":<n>,"group":<n>}` to each package's `/sdcard/Android/data/<package>/files/hapbeat-device.json`.

- **Read** (`DemoDeviceAddress`, SDK-independent): players read `Application.persistentDataPath/hapbeat-device.json` once. At most 1024 bytes, `version` 1, `player`/`group` -1 or 1..99, no other field. A missing file does nothing; an invalid one logs a warning and does nothing. The Editor reads only the file named by the environment variable `HAPBEAT_DEVICE_ADDRESS_FILE` (tests set `DemoDeviceAddress.PathOverride`).
- **Apply** (assembly `Hapbeat.DemoSwitch.HapbeatSdk`, compiled only when `com.hapbeat.sdk` is installed, so the Hub builds without the SDK): bootstraps itself before the first scene, waits for `HapbeatManager.Instance` and calls `SetAddressOverride(player, group, persist: false)` once. The SDK reads -1 as "disable this axis", so a -1 axis in the file passes the manager's current effective value instead and stays unchanged. Build-forced axes (`HapbeatConfig.buildOverride*`) are kept by the SDK. The file wins over a PlayerPrefs override restored by the SDK; later runtime UI or API changes are free. It logs `HAPBEAT_DEVICE_ADDRESS player=<n> group=<n> source=<path>` with the effective values. No demo code or scene change is needed.
- **Hub**: reads its own file and shows it on the top screen (no haptics).

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
