# HapbeatDemoSession (Unreal plugin)

Shared runtime plugin for the Hapbeat Unreal demos (`unreal/trex-encounter`, `unreal/safety-mill-vr`):

- **Demo Session** (`hapbeat-contracts/specs/demo-session.md`): reads the session ticket from the launching Intent, shows the completion panel (体験完了 / `n / N` / もう一度 / 次へ：<title> or デモを終了) and starts the next runtime with the next ticket.
- **In-view haptics ON/OFF button** (lower left of the view) for demos whose descriptor declares `supports.haptics_toggle`.
- **In-view 「視線をリセット」 button** (left of it; `hapbeat-contracts/specs/demo-session.md`, 視線をリセット), hidden by default.
- **Demo Switch receiver** (`hapbeat-contracts/specs/demo-switch-control.md`) on UDP 7710: DISCOVER/HERE, QUERY/STATE and CONTROL (the demo's registered `restart` / `menu_open` / `menu_close` / `tutorial_start`, the plugin's `recenter` / `recenter_ui_show` / `recenter_ui_hide`, plus `haptics_on` / `haptics_off` / `haptics_ui_show` / `haptics_ui_hide`; without a registered handler `menu_open` / `menu_close` / `restart` go to the shared pause, see below). `SWITCH` (starting another application) is answered `FAILED/not_allowed`. The socket stays bound while the process runs, also without VR focus (boundary setup, system menu): DISCOVER and QUERY are still answered (STATE `foreground: false`) and CONTROL is answered `FAILED/not_allowed` (`not in foreground`). A failed bind is retried every 0.5 s for 5 s (`DEMO_SWITCH_BIND_RETRY`, then `DEMO_SWITCH_LISTENER_FAILED`). On Android a Wi-Fi `MulticastLock` is held while bound (`CHANGE_WIFI_MULTICAST_STATE`), so a broadcast DISCOVER is not filtered.
- **Shared pause** (demos without a menu of their own): the left hand's Quest menu gesture or the controller's menu button opens 一時停止 with 再開 / 最初からやり直す / 次へ (session only) / Hub に戻る, with or without a session.
- **Per-device Hapbeat address** (`hapbeat-contracts/specs/demo-session.md`, 端末ごとの Hapbeat 宛先): `hapbeat-device.json` in the app's external files directory, written by `tools/install-demos.ps1 -Group <n> [-Player <n>]`, sets the Hapbeat SDK's address override at start. The plugin depends on the HapbeatSDK plugin for this.

The panels look like the Unity package's (`unity/packages/com.hapbeat.demo-switch/Runtime/DemoSessionUi.cs`): the same colours, Noto Sans CJK JP Regular (`Resources/Fonts`, SIL OFL, staged with the plugin), sizes in the same millimetres (2 widget px per mm), buttons stacked vertically, the pressed colour for 0.15 s and the same short click (2.4 kHz, 30 ms, `Content/Audio`, played as a UI sound so it is heard under the pause). They are drawn on top of the scene: `Content/Materials/M_HapbeatDemoSessionPanel` is the engine's translucent widget material without a depth test, at translucency sort priority `AHapbeatDemoSessionUi::PanelSortPriority` (500). The content is made by `Scripts/create_content.py` (see the script).

This folder is the source of truth. Each demo keeps a Git-ignored copy in `Plugins/HapbeatDemoSession`, pinned to a commit of this repository by `Scripts/hapbeat-demo-session.ref` and refreshed with `Scripts/sync-hapbeat-demo-session.ps1` (editor closed, then rebuild the editor target). `-WorkingTree` copies uncommitted source for development and leaves the pin alone.

## Using it in a demo

1. Enable the plugin in the `.uproject` and add `HapbeatDemoSession` to the module's dependencies. Cook the plugin's content: `+DirectoriesToAlwaysCook=(Path="/HapbeatDemoSession")` under `[/Script/UnrealEd.ProjectPackagingSettings]` in `Config/DefaultGame.ini`.
   Translucent hand layers that must stay visible in front of a panel use `AHapbeatDemoSessionUi::HandSortPriority` (501) or above; anything else is drawn under the panels.
2. Put the descriptor at `Config/HapbeatDemoSession/hapbeat-demo-session.json` (schema `hapbeat-contracts/schemas/demo-session-descriptor.schema.json`). The Android build copies it to the root of the APK assets, where the Hub reads it. Without a descriptor the plugin stays off (no session, no receiver).
3. From game code, with `UHapbeatDemoSessionSubsystem::Get(WorldContext)`:
   - call `ShowCompletion()` on the demo's own completion event (does nothing outside a session); optionally place a `UHapbeatDemoSessionPanelAnchor` component where the panel should appear;
   - bind `OnRestartRequested` (もう一度: restart the same step inside the demo);
   - bind `OnHapticsChanged(bool)`: off stops Hapbeat output **and** loops / streams already playing; sound and picture stay;
   - while `IsGameplayPaused()` (completion or pause panel open; also `OnGameplayPausedChanged`) ignore the demo's own game input;
   - with the shared pause enabled, bind `OnPauseChanged(bool)`: stop / resume the game's progress, the navigation voice and haptics (e.g. `UGameplayStatics::SetGamePaused`, which also pauses non-UI sounds; actors that must keep drawing the tracked hands tick when paused);
   - `RegisterControl(Action, Owner, Handler)` for each Demo Switch action the demo supports, `UnregisterControls(Owner)` in `EndPlay` (not `recenter`: it is the plugin's). A demo with a tutorial registers `tutorial_start` (start the tutorial from its beginning); without it the action is `FAILED/not_allowed`. A demo with a menu of its own registers `menu_open` / `menu_close`, otherwise the shared pause takes them;
   - `SetRecenterHandler(Owner, Handler(FacingYaw))` when the demo has its own start alignment (`ClearRecenterHandler(Owner)` in `EndPlay`): put the head at the start position facing the start direction, where `FacingYaw` (world) is the user's facing, without changing the floor height. Without one the plugin turns and moves the player's pawn (below);
   - `GetOption(Id)` for descriptor options (ticket value or default; unknown values fall back to the default with a warning).

## Behaviour

- The ticket is accepted only when it passes the schema constraints (16384 bytes, field patterns) and `steps[index].demo_id` is this demo. Anything else logs `DEMO_SESSION_TICKET_REJECTED` and the demo runs as if launched without a ticket. The extra is removed from the Intent; nothing is persisted.
- Haptics start ON at every step. The button is visible while the ticket's `haptics_ui` (or `haptics_ui_show`) says so; its state carries to the next step in the ticket.
- Completion panel: at the world's first `UHapbeatDemoSessionPanelAnchor` (its yaw turned toward the eye when it appears; with `bUseRotation` the anchor's own rotation, +X toward the user, turned round when the eye is behind it), otherwise 55 cm ahead of the head (head yaw only), 12 cm below eye level, facing the eye. It stays where it appeared. Buttons ignore input for 1.0 s after it appears.
- Recenter: on a system recenter (Meta button held; OpenXR `XrEventDataReferenceSpaceChangePending`, which UE's OpenXR plugin broadcasts as `FCoreDelegates::VRHeadsetRecenter`) the open completion or pause panel moves to the default place in front of the head (anchor or not), for 0.3 s after the event so that the tracking origin's change and a demo's own re-alignment are taken in. The input delay does not restart. Log `DEMO_SESSION_RECENTER`. That a Quest delivers the event for the long press has not been confirmed on a device yet.
- 次へ / デモを終了: explicit Intent (`setClassName`) to `steps[index+1]` (or `finish`) with `FLAG_ACTIVITY_NEW_TASK | FLAG_ACTIVITY_CLEAR_TASK` and the ticket with `index + 1` and the current `haptics_ui`. Off Android nothing is launched (`DEMO_SESSION_LAUNCH_SKIPPED` with the ticket in the log).
- Hand-over (次へ / デモを終了 / Hub に戻る, `FHapbeatLaunchHandoff`): after `startActivity` the demo does **not** end at once (ending right away let Quest switch to its home environment, which sent the starting app to the background). It waits (`DEMO_SESSION_LAUNCH_WAITING`) until it has gone to the background itself: VR focus lost (`FApp::HasVRFocus`, OpenXR session no longer FOCUSED) or the activity pausing (`FCoreDelegates::ApplicationWillDeactivateDelegate`, broadcast on the game thread just before Android suspends it). Then haptics go off, all sounds stop, the 7710 listener closes, then `finishAndRemoveTask()` and `RequestExit`, once (`DEMO_SESSION_BACKGROUNDED`). Still in front after 5 s, or `startActivity` failed: `DEMO_SESSION_LAUNCH_FAILED`, the open panel shows an error and the demo stays. While waiting the panels take no input.
- Input: index-fingertip poke (OpenXR hand tracking; approach 2.5–12 cm in front, press at ≤ 0.6 cm, the same window as Safety Mill's guide keys) and, on a side without a tracked hand, the controller aim ray + trigger (`OculusTouch_*_Trigger_Click`; a thin ray is drawn while the UI is visible). The demos build no OpenXR action mapping contexts, so UE binds every controller key through its legacy path; controller input has not been tried on a Quest yet.
- In-view buttons (the same values as Unity's `DemoSessionCornerButtons`): 45 cm from the eye and 30° below the horizon, 視線をリセット 19.5° and haptics 5° left of the heading (`AHapbeatDemoSessionUi::InViewButtonLocation`), side by side about 10 mm apart, each an 88 × 64 mm plate (an 80 × 56 mm button). Each keeps its place when the other is hidden. Two-line English labels, 18 mm: 「Haptics / ON」 / 「Haptics / OFF」 (the selected colour while ON) and 「Reset / View」; the size is fixed whatever the state. Like Unity's `DemoHeadingPlacement`, they follow the head's heading slowly (1 − e^(−2.5 Δt) of the way each frame) and ignore its pitch, so looking down at a button keeps it still.
- 視線をリセット button: shown while the ticket's `recenter_ui` (or `CONTROL recenter_ui_show` / `recenter_ui_hide`) says so; its state carries to the next step in the ticket. Pressing it (facing = the head's yaw, as Unity's `DemoRecenter.ResetView`) or `CONTROL recenter` (facing = the head's yaw) calls the demo's recenter handler, or else turns the player's pawn about the head by `start yaw − facing` and moves it so the head is over the pawn's location as first seen (height unchanged). Open panels then go in front of the head as after a system recenter. Log `DEMO_SESSION_RECENTER source=button|control handler=demo|pawn facing=<yaw> done=<0|1>`, `DEMO_SESSION_RECENTER_UI visible=<0|1>`. A `RegisterControl("recenter", ...)` is ignored with a warning.
- Ticket: the optional `recenter_ui` and `hand_style` are read and handed on (`hand_style` is not used: these demos draw their own hands).

## Shared pause

Off unless the project opts in in its checked-in `Config/DefaultGame.ini` (demos with their own menu, e.g. T-Rex's operator menu, leave it off):

```ini
[HapbeatDemoSession.Pause]
Enabled=True
Gesture=SystemMenu
; HoldSeconds=0.3
```

- `Gesture=SystemMenu` (A, default): Quest's own menu gesture (left palm toward the face, thumb and index pinched) and the Touch controller's ≡. The gesture is read in two ways that count as one gesture (whichever comes first toggles; the hand has to let go before the next): (1) from the left hand's joints, the palm-facing pinch held for `HoldSeconds` (default 0.3 s); (2) where the runtime has `XR_FB_hand_tracking_aim`, its `MENU_PRESSED` flag of the left hand (the flag Unity's `MenuPressed` reads), from a hand tracker of the plugin's own (the plugin is an OpenXR extension plugin; needs `XR_EXT_hand_tracking`, i.e. the OpenXRHandTracking plugin). Log `DEMO_SESSION_HAND_MENU tracker=<0|1>` at session start. That a Quest raises the flag to a UE application has not been confirmed on a device yet; (1) works without it.
- `Gesture=PalmPinchHold` (B): the same hand shape held for 2 s (`HoldSeconds` default), read from the hand joints only (the T-Rex development pause); the ≡ button still works. The decision is in one place, `FHapbeatPauseDetector`.
- Hand shape: the palm normal within 60° of the direction to the eye (dot > 0.5); the pinch closes at thumb tip to index tip < 1.5 cm and stays closed up to 3 cm. `HoldSeconds=<s>` in the same section overrides the hold (e.g. longer if the short hold fires by accident).
- The same gesture / button closes the panel again. The panel (一時停止: 再開 / 最初からやり直す / 次へ：<title> or デモを終了 / Hub に戻る) appears 55 cm ahead of the head and stays there (until a recenter); not over the completion panel, not while leaving. Nothing is read while the application has no focus.
- While the panel is open, the `UPoseableMeshComponent`s of the player's pawn and of actors attached to it tick even when the world is paused (restored when it closes): a posed mesh refreshes its bones in its own tick, so with `SetGamePaused` the drawn hands froze although the demo kept posing them. The demo still has to pose them while paused (an actor that ticks when paused, e.g. the pawn with `PrimaryActorTick.bTickEvenWhenPaused`).
- 最初からやり直す closes the pause, then broadcasts `OnRestartRequested`. 次へ / デモを終了 (only in a session; the completion panel's next step) hands over like the completion panel. Hub に戻る (only when `jp.hapbeat.demohub` is installed; the manifest lists it in `<queries>`) starts the Hub's `com.unity3d.player.UnityPlayerGameActivity` without a ticket and ends this demo like 次へ.
- Demo Switch (`UHapbeatDemoSessionSubsystem::RouteControl`): with the pause enabled and no registered handler, `CONTROL menu_open` / `menu_close` open / close this panel (explicit set: `menu_open` fails with `launch_failed` when the panel cannot open, e.g. over the completion panel) and `CONTROL restart` does what 最初からやり直す does (the completion panel closes too). With the pause off and no handler they are `FAILED/not_allowed`.
- Log: `DEMO_SESSION_PAUSE enabled|shown|hidden`, `DEMO_SESSION_PAUSE_RESTART [source=control]`, `DEMO_SESSION_PAUSE_NEXT`, `DEMO_SESSION_RETURN_TO_HUB`.

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

`QUERY` (authenticated like `DISCOVER`, no sequence) is answered with `STATE` (`UHapbeatDemoSessionSubsystem::GetSwitchState`): `current_demo_id` the descriptor's `demo_id`; `haptics_on` the haptics switch; `haptics_ui` the haptics button as drawn (`supports.haptics_toggle` and its visibility); `recenter_ui` the 視線をリセット button's visibility; `paused` the shared pause panel (a demo's own menu is not reported); `step_index` / `step_count` the ticket's `index` / step count, -1 / 0 without a session.

Override per device with `Saved/Config/HapbeatDemoSession.json` (on Quest under the app's `files/UnrealGame/<Project>/<Project>/Saved/`):

```json
{"enabled": true, "shared_secret": "<same secret as the M5>", "allow_unsigned": false, "isolated_lan": false}
```

`{"enabled": false}` turns the receiver off. With an empty secret, unsigned packets are accepted only when both `allow_unsigned` and `isolated_lan` are true. Never commit or distribute a secret. The highest accepted sequence per controller persists in `Saved/HapbeatDemoSession/Sequences.json`; an unreadable file disables the receiver (fail closed). The socket is bound only while the application has focus.

Non-Shipping test flags: `-HapbeatDemoSwitchTest` (loopback 127.0.0.1:17710, separate sequence file), `-HapbeatDemoSwitchConfig=<file>`, `-HapbeatSessionTicketFile=<file>` (desktop stand-in for the Intent extra).

## Tests

Automation spec `HapbeatDemoSession.*` (editor): device address file (schema, -1 against the SDK's `SetAddressOverride`), ticket validation (contract fixture, schema violations, size limit, demo mismatch, last step → finish), next ticket (index, `haptics_ui`, `recenter_ui`, `hand_style`), options (`when`, unknown values / options), Demo Switch protocol (HMAC vectors, unsigned mode, malformed packets, status, QUERY / STATE with their canonical strings and vectors), CONTROL routing (shared pause / restart fallbacks, `tutorial_start`), poke / ray presses and the 1-second completion delay, the Unity panel layout (sizes, stacked buttons), panel placement (default, anchor, anchor rotation turned toward the user), the pause detector (≡ rising edge, A 0.3-second hold, the hand menu flag as the same gesture, B 2-second hold and release, palm direction), the pause panel buttons with / without the Hub and the next step, a recenter moving the open panel, the 視線をリセット button (side-by-side placement) and the default pawn recenter, and the hand-over (background → one exit; 5 s in front → failed, no exit).

```powershell
UnrealEditor-Cmd.exe <demo>.uproject -ExecCmds="Automation RunTests HapbeatDemoSession;Quit" -TestExit="Automation Test Queue Empty" -nullrhi -NoSound -unattended
```
