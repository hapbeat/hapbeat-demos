# Hapbeat Demo Switch

Foreground demo applications use this package to receive controller commands on UDP 7710 and launch only locally allowlisted applications. It is independent of the Hapbeat SDK and UDP 7700.

## Install and configure

1. Add `"com.hapbeat.demo-switch": "file:../../packages/com.hapbeat.demo-switch"` to the Unity project's `Packages/manifest.json` (adjust the relative path for that project).
2. Create `Resources/HapbeatDemoSwitchSettings.asset` with **Hapbeat > Demo Switch Settings**.
3. Set Current Demo ID, Receiver Enabled, port 7710, and each target's logical Demo ID, Android package name, and fully qualified activity name.
4. Configure the same non-empty Shared Secret in every APK and the controller. Do not commit that value. Empty secret plus Allow Unsigned is intended only for an isolated demo LAN and logs a warning.
5. Subscribe to `DemoSwitch.BeforeSwitch` for application cleanup, or call `DemoSwitch.SwitchTo("logical-demo-id")` for a local UI-driven switch.

No Scene component is required. Bootstrap occurs only when `Resources/HapbeatDemoSwitchSettings.asset` exists. Editor and non-Android players never start another application; the safe adapter logs a failure.

## Controller flow

Send a version 1 `SWITCH` command to the current headset IP at UDP 7710 from a stable controller UDP port. Expect `ACK` from the current APK and `READY` from the next APK on that same controller port. `FAILED` means the command was rejected or application launch failed. See `hapbeat-contracts/specs/demo-switch-control.md` for the canonical HMAC-SHA256 input and complete message contract.

```json
{"version":1,"type":"SWITCH","controller_id":"m5-main","seq":42,"demo_id":"boxing","auth":"<64 lowercase hex HMAC-SHA256>"}
```

The working M5Unified sample is in `m5/demo-switch-controller/` at the repository root.
