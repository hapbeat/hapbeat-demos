# Hapbeat Demo Hub

Independent, silent Quest waiting room for exhibitions, and the planner for self-paced Demo Sessions. No Hapbeat haptic SDK, game assets, or demo-specific gameplay is included.

## Open and edit

Open this folder in Unity **6000.0.59f2** and open `Assets/Scenes/DemoHub.unity`. The four UI Text objects under `Head/Waiting Message` are editable in the Inspector. These head-locked labels (two metres) appear only in staff waiting mode.

The hub does not set the forward direction of other apps. Ask participants to face the physical play area before starting a demo. It is not a replacement for Quest boundary/recenter or each demo's tracking initialization.

`Hapbeat Demo Hub > Build Quest APK` builds `Builds/hapbeat-demo-hub.apk` with package `jp.hapbeat.demohub` (shown as **Demo Hub** in Quest's library), activity `com.unity3d.player.UnityPlayerGameActivity`, and logical switch ID `demo_hub`. The initial scene authoring command refuses to replace an existing scene. Builds do not regenerate scenes.

## Operator flow

1. Install the hub APK once: `adb install -r Builds/hapbeat-demo-hub.apk`.
2. Open **Demo Hub** from Quest's app list / Unknown Sources, or run `adb shell am start -n jp.hapbeat.demohub/com.unity3d.player.UnityPlayerGameActivity`.
3. Fit the headset and ask the participant to face forward.
4. Participants start a preset or a demo tile themselves. For M5 operation, turn on staff waiting mode in the manage screen; on the same isolated Wi-Fi LAN, press the existing M5 button for the desired demo. The hub uses the shared `com.hapbeat.demo-switch` UPM package, including automatic discovery. The receiving demo must already be installed.

`Assets/Resources/HapbeatDemoSwitchSettings.asset` contains launch targets for `gloveball`, `handdemo`, `gloveball_v2`, and `boxing`. Boxing is reserved for when its APK is installed. Add future demo package/activity pairs here and rebuild the hub.

The existing M5 A/B/C assignments and installed demo APKs are not modified by this project. **Returning to the hub via M5 requires installation updates:** controller firmware 0.1.0-d5 adds A+C simultaneous hold (one second), and common package 0.1.0-d3 adds `demo_hub` → `jp.hapbeat.demohub` / `com.unity3d.player.UnityPlayerGameActivity` automatically to the locally trusted destinations. Rebuild/install every participating demo with that package and install the hub. Existing installed APKs are not updated by source changes. Until deployment is complete, reopen the hub from Quest's app list or ADB between participants.

Unsigned switching is enabled to match the existing exhibition setup. Use an isolated LAN only; for shared networks configure the same secret in controller and all app settings before building.

## Demo Session (self-paced plan)

The hub follows `hapbeat-contracts/specs/demo-session.md` using the shared package's `DemoSession` API. Version 0.1.0-d4 (versionCode 4; ghost hands; needs package 0.1.0-d8).

- **Catalog**: PackageManager launcher activities (`MAIN`/`LAUNCHER`; the build adds the matching `<queries>` element) whose APK assets contain a valid `hapbeat-demo-session.json`. Package and activity come from PackageManager, never from the descriptor. In the Editor a dummy catalog (Volley, Boxing, T-Rex) is used; tests and tools can set `HubCatalog.Override`.
- **Top screen** (default, for participants): one large button per preset marked "トップに表示" in the manage screen ("プリセット n　約m分" over the installed step titles joined by →), then demo tiles (title and estimated minutes, 3 columns, 6 per page) for installed demos marked visible. A preset whose steps are all uninstalled is not shown. With nothing to show: "管理画面で表示するプリセット／デモを選んでください". Under the title, the device address line; bottom right, a small "管理" button that opens the manage screen after a **2 s long press** (the label turns into a ■□ progress bar while held). Launch errors appear left of it in a reserved line.
- **Staff waiting mode** (manage screen toggle, default off): the top screen shows the head-locked waiting labels and only a small panel with the device address and the 管理 button. M5 SWITCH is received in every mode.
- **This device's address**: a fixed-size line "この端末: プレイヤー <n> / グループ <n>" from the hub's own `hapbeat-device.json` (written by `tools/install-demos.ps1`; -1, a missing or an invalid file shows "指定なし"). The hub has no Hapbeat SDK and sends nothing; each demo applies its own file (shared package 0.1.0-d6).
- **Manage screen** (for operators): tabs "プリセット 1/2/3" and "デモのタイル". A preset tab edits that preset with the catalog (tap to append), plan rows with ↑ ↓ ×, option chips that cycle values (options whose `when` is not met are hidden but remembered) and "もう一度あり/なし" (new steps default to あり), plus its "トップに表示：する/しない" toggle. The tiles tab lists installed demos with "表示する/表示しない". The footer has "触覚ボタン：表示する/しない" (initial `haptics_ui` of every launch, default hidden), "スタッフ待機モード：ON/OFF" and "完了" (back to top). Every change is saved at once under `persistentDataPath/demo-session/` (`preset-1..3.json`, `hub-settings.json`). On the first run the planner's old `last.json` moves to preset 1 (shown on top when it has steps).
- **Start**: a preset builds a ticket (random 16-hex `session_id`, `finish` = this activity, options = active options only, uninstalled steps skipped); a tile builds a one-step ticket with the descriptor's default options and retry. Step 1 launches and the hub closes. A failed launch shows the error and keeps the hub open.
- **Finish screen**: shown when launched with `index == len(steps)` for a session of two or more steps (a finished one-step tile session opens the top screen): "体験は以上です。ヘッドセットを外してください", "最初から（同じプラン）" (same steps, new session ID) and "トップへ".
- **Placement**: every screen uses one world-space panel 0.6 m ahead and 0.18 m below eye height, placed when head tracking becomes valid. It follows the head's yaw lazily: once the heading is 35° or more away (or the distance/height is off by more than 0.25 m) it eases back in front over 0.5 s.
- Input is the package panel: fingertip poke (hand tracking) and controller ray + trigger.
- **Ghost hands**: the hub enables the package's ghost hands (`_ghostHands: 1` in `Assets/Resources/HapbeatDemoSwitchSettings.asset`), so tracked hands are drawn as translucent outlines whose index tip is the poke point. Controllers keep only the ray cursor.

`Hapbeat Demo Hub > Add Demo Session Controller` adds the controller to the existing scene without regenerating it (already applied to the checked-in scene).

## Verification and build

With this project's Editor closed, pass `-batchmode -quit -projectPath <this-folder> -executeMethod Hapbeat.DemoHub.Editor.HubValidation.Validate` to Unity for static scene, Japanese font (including every Demo Session UI string), session controller, and allowlist checks. EditMode tests cover the package and the plan editor logic. `HubValidation.RenderPreview` renders a silent non-Play preview; do not use `-nographics` for that command. `HubProject.BuildApk` is the batch build entry point (use `-buildTarget Android`). No test starts a demo or transmits haptics.

Hardware checks: readable Japanese/English in both eyes; follows the HMD after refitting; launches each installed demo via M5; demo height/orientation initializes correctly on repeated visits. Android activity relaunch is handled by the shared switch module; per-demo calibration still requires hardware verification.

### Initial verification (2026-09-16)

Unity scene/glyph/allowlist checks and desktop render passed. Quest ARM64 APK build succeeded (47,720,238 bytes; zero errors). The three remaining OpenXR warnings are optional recommendations for PoseControl, StickControl and input-polling priority; this waiting room does not use controller actions. APK metadata confirms the independent package ID, INTERNET permission, optional hand tracking, and Quest 2/3/3S device list. Installation, in-headset rendering and M5 end-to-end switching have not yet been tested for this new APK.

## Font

Noto Sans CJK JP Regular, from [notofonts/noto-cjk](https://github.com/notofonts/noto-cjk/tree/main/Sans/OTF/Japanese), is distributed under the bundled OFL text (SIL Open Font License 1.1). Since Demo Session 0.1.0-d5 the font lives in the shared package (`com.hapbeat.demo-switch/Runtime/Resources/HapbeatDemoSession/`) and the hub references it there. No operating-system fonts are required.
