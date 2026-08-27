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
