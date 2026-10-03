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

The hub follows `hapbeat-contracts/specs/demo-session.md` using the shared package's `DemoSession` API. Version 0.1.0-d9 (versionCode 9; 視線をリセット replaces 手前に移動, panels always on top; needs package 0.1.0-d14).

- **Catalog**: PackageManager launcher activities (`MAIN`/`LAUNCHER`; the build adds the matching `<queries>` element) whose APK assets contain a valid `hapbeat-demo-session.json`. Package and activity come from PackageManager, never from the descriptor. In the Editor a dummy catalog (Volley, Boxing, T-Rex Encounter) is used; tests and tools can set `HubCatalog.Override`.
- **Names**: every list, tile and step title uses the application's PackageManager label (the name in Quest's library, e.g. Volley, T-Rex Encounter; the descriptor's Japanese title only when the label is missing). A step title adds the active option values ("Volley ブロック 3点先取", at most 40 characters). The descriptor's optional `minutes` is not shown anywhere (no measured basis).
- **Top screen** (default, for participants): one large button per preset marked "トップに表示" in the manage screen ("プリセット n" over the installed step titles joined by →), then every visible installed demo as a tile (the name, 3 columns, as many rows as needed; no pages). A preset whose steps are all uninstalled is not shown. With nothing to show: "管理画面で表示するプリセット／デモを選んでください". Under the title, the device address line; bottom right, a small "管理" button that opens the manage screen after a **1 s long press** (the label turns into a ■□ progress bar while held). Launch errors appear left of it in a reserved line.
- **Staff waiting mode** (manage screen toggle, default off): the top screen shows the head-locked waiting labels and only a small panel with the device address and the 管理 button. M5 SWITCH is received in every mode.
- **This device's address**: a fixed-size line "この端末: プレイヤー <n> / グループ <n>" from the hub's own `hapbeat-device.json` (written by `tools/install-demos.ps1`; -1, a missing or an invalid file shows "指定なし"). The hub has no Hapbeat SDK and sends nothing; each demo applies its own file (shared package 0.1.0-d6).
- **Manage screen** (for operators): one panel that shows everything at once, without pages. All rows and buttons are at least 40 mm tall for fingertip pokes.
  - **Size**: the grid is as tall as the tab's longest column: the catalog or the plan (at most 16 rows each), or the selected step's settings (its title, options and もう一度; 3 rows for the hint when nothing is selected). Example: catalog 6 and plan 4 → 6 rows; plan 10 → 10 rows. So there is no empty space between the tabs, the grid and the footer; the shorter column ends early. The width (about 0.94 m, set by the footer's four settings and 完了) is the same on every tab; a second plan column is added only when a preset has more than 16 steps. Within the manage screen a size change keeps the panel's top-left corner in place, so the tabs and the catalog do not move under the finger while steps are added.
  - Top bar: tabs "プリセット 1/2/3" and "デモのタイル", then (preset tabs) "トップに表示：する/しない".
  - Preset tab, left to right: the catalog column ("カタログ（タップで追加）", one button per installed demo, 16 per column), the plan ("プラン（n / 32）", 16 numbered rows per column) whose rows are "≡" (grip), "n. Volley ブロック 3点先取", "▲", "▼" and "削除", and the column "選んだ回の設定". Pressing a row's name selects it (highlighted) and that column then shows the step's option buttons, which cycle the values (options whose `when` is not met are hidden but remembered), and "もう一度あり/なし" (new steps default to あり). An added or moved step is selected and flashes briefly; the selection follows a moved step.
  - **Drag to reorder**: press and hold a row's "≡" for 0.5 s (fingertip pushed into the panel, or the controller trigger held on it; the grip lights up), then move up or down while still pressing: a line shows where the step will go (between rows, or after the last). Releasing inserts the step there (the others close up; not a swap). Releasing at its own place changes nothing. ▲ / ▼ still move a step by one.
  - Tiles tab: every installed demo "Volley" with "表示する/表示しない", in columns of 16.
  - Footer, left-aligned without gaps: "触覚ボタン：表示する/しない" (initial `haptics_ui` of every launch, default hidden), "視線リセットボタン：表示する/しない" (initial `recenter_ui` of every launch and the Hub's own 視線をリセット button, default hidden), "スタッフ待機モード：ON/OFF", "手の見た目：ゴースト/肌" (the hub's own hands and every launch's `hand_style`, default ghost) and "完了" (back to top); a status line below for save errors and "プランは最大 32 件です".
  - Every change is saved at once under `persistentDataPath/demo-session/` (`preset-1..3.json`, `hub-settings.json` with `hand_style` and `recenter_ui`). On the first run the planner's old `last.json` moves to preset 1 (shown on top when it has steps).
- **Start**: a preset builds a ticket (random 16-hex `session_id`, `finish` = this activity, options = active options only, uninstalled steps skipped, `hand_style` and `recenter_ui` from the manage screen); a tile builds a one-step ticket with the descriptor's default options and retry. Step 1 launches and the hub closes once that demo is in front (the package's hand-over). A failed launch, or a demo that does not come to the front within 5 s, shows the error and keeps the hub open.
- **Finish screen**: shown when launched with `index == len(steps)` for a session of two or more steps (a finished one-step tile session opens the top screen): "体験は以上です。ヘッドセットを外してください", "最初から（同じプラン）" (same steps, new session ID) and "トップへ".
- **Placement**: every screen uses one world-space panel, fixed in space. It is placed once, 0.6 m ahead along the head's heading and 0.18 m below eye height, in the first frame head tracking is valid, and then never follows the head (screen changes keep its centre; inside the manage screen, its top-left corner). The package's "視線をリセット" button (lower left of view, above the haptics button's place; shown only when the manage screen's 視線リセットボタン is on) and CONTROL `recenter` place it in front of the head again; in the Hub nothing else moves. A system recenter (the package's `DemoRecenter`) also places the panel in front again. The former always-shown 手前に移動 key is removed.
- **Rendering**: `HubProject.Configure` sets 4x MSAA on Android's default quality level (the built-in pipeline also uses it for the XR eye buffers), against jagged panel and button edges. The hub's panels raster their glyphs at 2.5 px/mm instead of the package's 4 (`DemoSessionPanel.GlyphPixelsPerMillimetre`): glyphs have no mipmaps, and Quest's eye buffer is about 2 px/mm at 0.6 m, so 4 px/mm was minified about 2x and shimmered. Both still need a look in the headset.
- Input is the package panel: fingertip poke (hand tracking) and controller ray + trigger.
- **Hands**: the hub enables the package's shared hands (`_hands: 1`, `_handStyle: 0` in `Assets/Resources/HapbeatDemoSwitchSettings.asset`) and applies the manage screen's look. With the private assets linked (`tools/link-private-assets.ps1`: `Assets/HapbeatPrivate` → `private-assets/unity/demo-hub/HapbeatPrivate`, git-ignored) they are Meta's hand mesh in the ghost or skin look; without them (public clone) the procedural ghost hands. Either way the drawn index tip is the poke point. Controllers keep only the ray cursor.
- **Button haptics**: not implemented (the hub has no Hapbeat SDK). The planned route is the SDK through the same reference as the other demos, a one-clip Kit with an Event Map entry (for example `ui.click`) played on press, and the device address file applied by the package's SDK applier; no waveform in code.

`Hapbeat Demo Hub > Add Demo Session Controller` adds the controller to the existing scene without regenerating it (already applied to the checked-in scene).

## Verification and build

With this project's Editor closed, pass `-batchmode -quit -projectPath <this-folder> -executeMethod Hapbeat.DemoHub.Editor.HubValidation.Validate` to Unity for static scene, Japanese font (including every Demo Session UI string), session controller, and allowlist checks. EditMode tests cover the package and the plan editor logic. `HubValidation.RenderPreview` renders a silent non-Play preview; do not use `-nographics` for that command. `HubProject.BuildApk` is the batch build entry point (use `-buildTarget Android`). No test starts a demo or transmits haptics.

Hardware checks: readable Japanese/English in both eyes; follows the HMD after refitting; launches each installed demo via M5; demo height/orientation initializes correctly on repeated visits. Android activity relaunch is handled by the shared switch module; per-demo calibration still requires hardware verification.

### Initial verification (2026-09-16)

Unity scene/glyph/allowlist checks and desktop render passed. Quest ARM64 APK build succeeded (47,720,238 bytes; zero errors). The three remaining OpenXR warnings are optional recommendations for PoseControl, StickControl and input-polling priority; this waiting room does not use controller actions. APK metadata confirms the independent package ID, INTERNET permission, optional hand tracking, and Quest 2/3/3S device list. Installation, in-headset rendering and M5 end-to-end switching have not yet been tested for this new APK.

## Font

Noto Sans CJK JP Regular, from [notofonts/noto-cjk](https://github.com/notofonts/noto-cjk/tree/main/Sans/OTF/Japanese), is distributed under the bundled OFL text (SIL Open Font License 1.1). Since Demo Session 0.1.0-d5 the font lives in the shared package (`com.hapbeat.demo-switch/Runtime/Resources/HapbeatDemoSession/`) and the hub references it there. No operating-system fonts are required.
