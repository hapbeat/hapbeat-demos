# Hapbeat Demo Switch

Foreground demo applications use this package to receive controller commands on UDP 7710 and launch only locally allowlisted applications. It is independent of the Hapbeat SDK and UDP 7700.

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
