# Hapbeat Demo Hub

Independent, silent Quest waiting room for exhibitions. No Hapbeat haptic SDK, controller actions, game assets, or demo-specific gameplay is included.

## Open and edit

Open this folder in Unity **6000.0.59f2** and open `Assets/Scenes/DemoHub.unity`. The four UI Text objects under `Head/Waiting Message` are editable in the Inspector. The panel is head-relative at two metres, so fitting the headset or changing participants does not leave it behind or below the participant.

The hub does not set the forward direction of other apps. Ask participants to face the physical play area before starting a demo. It is not a replacement for Quest boundary/recenter or each demo's tracking initialization.

`Hapbeat Demo Hub > Build Quest APK` builds `Builds/hapbeat-demo-hub.apk` with package `jp.hapbeat.demohub`, activity `com.unity3d.player.UnityPlayerGameActivity`, and logical switch ID `demo_hub`. The initial scene authoring command refuses to replace an existing scene. Builds do not regenerate scenes.

## Operator flow

1. Install the hub APK once: `adb install -r Builds/hapbeat-demo-hub.apk`.
2. Open **Hapbeat Demo Hub** from Quest's app list / Unknown Sources, or run `adb shell am start -n jp.hapbeat.demohub/com.unity3d.player.UnityPlayerGameActivity`.
3. Fit the headset and ask the participant to face forward.
4. On the same isolated Wi-Fi LAN, press the existing M5 button for the desired demo. The hub uses the shared `com.hapbeat.demo-switch` UPM package, including automatic discovery. The receiving demo must already be installed.

`Assets/Resources/HapbeatDemoSwitchSettings.asset` contains launch targets for `gloveball`, `handdemo`, `gloveball_v2`, and `boxing`. Boxing is reserved for when its APK is installed. Add future demo package/activity pairs here and rebuild the hub.

The existing M5 A/B/C assignments and installed demo APKs are not modified by this project. **Returning to the hub via M5 requires installation updates:** controller firmware 0.1.0-d5 adds A+C simultaneous hold (one second), and common package 0.1.0-d3 adds `demo_hub` → `jp.hapbeat.demohub` / `com.unity3d.player.UnityPlayerGameActivity` automatically to the locally trusted destinations. Rebuild/install every participating demo with that package and install the hub. Existing installed APKs are not updated by source changes. Until deployment is complete, reopen the hub from Quest's app list or ADB between participants.

Unsigned switching is enabled to match the existing exhibition setup. Use an isolated LAN only; for shared networks configure the same secret in controller and all app settings before building.

## Verification and build

With this project's Editor closed, pass `-batchmode -quit -projectPath <this-folder> -executeMethod Hapbeat.DemoHub.Editor.HubValidation.Validate` to Unity for static scene, Japanese font, and allowlist checks. `HubValidation.RenderPreview` renders a silent non-Play preview; do not use `-nographics` for that command. `HubProject.BuildApk` is the batch build entry point (use `-buildTarget Android`). No test starts a demo or transmits haptics.

Hardware checks: readable Japanese/English in both eyes; follows the HMD after refitting; launches each installed demo via M5; demo height/orientation initializes correctly on repeated visits. Android activity relaunch is handled by the shared switch module; per-demo calibration still requires hardware verification.

### Initial verification (2026-09-16)

Unity scene/glyph/allowlist checks and desktop render passed. Quest ARM64 APK build succeeded (47,720,238 bytes; zero errors). The three remaining OpenXR warnings are optional recommendations for PoseControl, StickControl and input-polling priority; this waiting room does not use controller actions. APK metadata confirms the independent package ID, INTERNET permission, optional hand tracking, and Quest 2/3/3S device list. Installation, in-headset rendering and M5 end-to-end switching have not yet been tested for this new APK.

## Font

Noto Sans CJK JP Regular, from [notofonts/noto-cjk](https://github.com/notofonts/noto-cjk/tree/main/Sans/OTF/Japanese), is distributed under the bundled `Assets/Fonts/OFL.txt` (SIL Open Font License 1.1). No operating-system fonts are required.
