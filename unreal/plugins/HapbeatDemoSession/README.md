# HapbeatDemoSession (Unreal plugin)

Shared runtime plugin for the Hapbeat Unreal demos (`unreal/trex-encounter`, `unreal/safety-mill-vr`):

- **Demo Session** (`hapbeat-contracts/specs/demo-session.md`): reads the session ticket from the launching Intent, shows the completion panel (体験完了 / `n / N` / もう一度 / 次へ：<title> or デモを終了) and starts the next runtime with the next ticket.
- **In-view haptics ON/OFF button** (lower left of the view) for demos whose descriptor declares `supports.haptics_toggle`.
- **Demo Switch receiver** (`hapbeat-contracts/specs/demo-switch-control.md`) on UDP 7710: DISCOVER/HERE and CONTROL (the demo's registered `restart` / `menu_open` / `menu_close` / `recenter`, plus `haptics_on` / `haptics_off` / `haptics_ui_show` / `haptics_ui_hide`). `SWITCH` (starting another application) is answered `FAILED/not_allowed`.
- **Per-device Hapbeat address** (`hapbeat-contracts/specs/demo-session.md`, 端末ごとの Hapbeat 宛先): `hapbeat-device.json` in the app's external files directory, written by `tools/install-demos.ps1 -Group <n> [-Player <n>]`, sets the Hapbeat SDK's address override at start. The plugin depends on the HapbeatSDK plugin for this.

This folder is the source of truth. Each demo keeps a Git-ignored copy in `Plugins/HapbeatDemoSession`, pinned to a commit of this repository by `Scripts/hapbeat-demo-session.ref` and refreshed with `Scripts/sync-hapbeat-demo-session.ps1` (editor closed, then rebuild the editor target). `-WorkingTree` copies uncommitted source for development and leaves the pin alone.

## Using it in a demo

1. Enable the plugin in the `.uproject` and add `HapbeatDemoSession` to the module's dependencies.
2. Put the descriptor at `Config/HapbeatDemoSession/hapbeat-demo-session.json` (schema `hapbeat-contracts/schemas/demo-session-descriptor.schema.json`). The Android build copies it to the root of the APK assets, where the Hub reads it. Without a descriptor the plugin stays off (no session, no receiver).
3. From game code, with `UHapbeatDemoSessionSubsystem::Get(WorldContext)`:
   - call `ShowCompletion()` on the demo's own completion event (does nothing outside a session);
   - bind `OnRestartRequested` (もう一度: restart the same step inside the demo);
   - bind `OnHapticsChanged(bool)`: off stops Hapbeat output **and** loops / streams already playing; sound and picture stay;
   - while `IsGameplayPaused()` (completion panel open; also `OnGameplayPausedChanged`) ignore the demo's own game input;
   - `RegisterControl(Action, Owner, Handler)` for each Demo Switch action the demo supports, `UnregisterControls(Owner)` in `EndPlay`;
   - `GetOption(Id)` for descriptor options (ticket value or default; unknown values fall back to the default with a warning).

## Behaviour

- The ticket is accepted only when it passes the schema constraints (16384 bytes, field patterns) and `steps[index].demo_id` is this demo. Anything else logs `DEMO_SESSION_TICKET_REJECTED` and the demo runs as if launched without a ticket. The extra is removed from the Intent; nothing is persisted.
- Haptics start ON at every step. The button is visible while the ticket's `haptics_ui` (or `haptics_ui_show`) says so; its state carries to the next step in the ticket.
- Completion panel: 55 cm ahead of the head (head yaw only), 12 cm below eye level, facing the eye; buttons ignore input for 1.0 s after it appears.
- 次へ / デモを終了: explicit Intent (`setClassName`) to `steps[index+1]` (or `finish`) with `FLAG_ACTIVITY_NEW_TASK | FLAG_ACTIVITY_CLEAR_TASK` and the ticket with `index + 1` and the current `haptics_ui`. On success haptics go off, all sounds stop, the 7710 listener closes, then `finishAndRemoveTask()` and `RequestExit`. On failure the panel shows an error and the demo stays. Off Android nothing is launched (`DEMO_SESSION_LAUNCH_SKIPPED` with the ticket in the log).
- Input: index-fingertip poke (OpenXR hand tracking; approach 2.5–12 cm in front, press at ≤ 0.6 cm, the same window as Safety Mill's guide keys) and, on a side without a tracked hand, the controller aim ray + trigger (`OculusTouch_*_Trigger_Click`; a thin ray is drawn while the UI is visible). The demos build no OpenXR action mapping contexts, so UE binds every controller key through its legacy path; controller input has not been tried on a Quest yet.
- Haptics button: 45 cm from the eye, 30° left of the head yaw and 35° below the horizon. The yaw follows the head only once it turns more than 25° away, so looking at the button does not push it away. Fixed width for 「触覚 ON」/「触覚 OFF」.

## Per-device Hapbeat address

`{"version":1,"player":<n>,"group":<n>}` (at most 1024 bytes, values 1–99 or -1, no other fields; schema `hapbeat-contracts/schemas/demo-device-address.schema.json`) at `getExternalFilesDir(null)/hapbeat-device.json`, on Quest `/sdcard/Android/data/<package>/files/hapbeat-device.json`. It is read once when the game instance starts, after `UHapbeatSubsystem` has initialized (so after the saved override and the pinned axes are restored), whether or not the demo has a descriptor.

- Each axis that is not -1 goes to `UHapbeatSubsystem::SetAddressOverride(player, group, false)`: it beats the saved override for this run and is not persisted.
- A -1 axis stays as it is. `SetAddressOverride` would clear an axis given -1, so the plugin passes that axis' current override (`GetOverridePlayer` / `GetOverrideGroup`) instead.
- Axes pinned in the build (`UHapbeatConfig::ForcedOverridePlayer` / `ForcedOverrideGroup`) are kept by the SDK.
- Log: `HAPBEAT_DEVICE_ADDRESS player=<n> group=<n> source=<path>` with the effective values afterwards (-1 = no override on that axis). A file that breaks the schema logs `HAPBEAT_DEVICE_ADDRESS_REJECTED <reason> source=<path>` as a warning and changes nothing (on Android an oversized or unreadable file is reported by the Java side with the same token). No file: nothing happens, nothing is logged.
- Non-Shipping desktop builds: `-HapbeatDeviceAddressFile=<path>` stands in for the Android file.

## Demo Switch receiver settings

The receiver is **off unless the project opts in** in its checked-in `Config/DefaultGame.ini`, the counterpart of each Unity demo's `HapbeatDemoSwitchSettings` asset. The exhibition setting (isolated LAN, unsigned, same as the Unity demos) is:

```ini
[HapbeatDemoSession.DemoSwitch]
Enabled=True
AllowUnsignedOnIsolatedLan=True
```

With an empty secret the receiver logs `DEMO_SWITCH_UNSIGNED` as a warning.

Override per device with `Saved/Config/HapbeatDemoSession.json` (on Quest under the app's `files/UnrealGame/<Project>/<Project>/Saved/`):

```json
{"enabled": true, "shared_secret": "<same secret as the M5>", "allow_unsigned": false, "isolated_lan": false}
```

`{"enabled": false}` turns the receiver off. With an empty secret, unsigned packets are accepted only when both `allow_unsigned` and `isolated_lan` are true. Never commit or distribute a secret. The highest accepted sequence per controller persists in `Saved/HapbeatDemoSession/Sequences.json`; an unreadable file disables the receiver (fail closed). The socket is bound only while the application has focus.

Non-Shipping test flags: `-HapbeatDemoSwitchTest` (loopback 127.0.0.1:17710, separate sequence file), `-HapbeatDemoSwitchConfig=<file>`, `-HapbeatSessionTicketFile=<file>` (desktop stand-in for the Intent extra).

## Tests

Automation spec `HapbeatDemoSession.*` (editor): device address file (schema, -1 against the SDK's `SetAddressOverride`), ticket validation (contract fixture, schema violations, size limit, demo mismatch, last step → finish), next ticket (index, `haptics_ui`), options (`when`, unknown values / options), Demo Switch protocol (HMAC vectors, unsigned mode, malformed packets, status), poke / ray presses and the 1-second completion delay.

```powershell
UnrealEditor-Cmd.exe <demo>.uproject -ExecCmds="Automation RunTests HapbeatDemoSession;Quit" -TestExit="Automation Test Queue Empty" -nullrhi -NoSound -unattended
```
