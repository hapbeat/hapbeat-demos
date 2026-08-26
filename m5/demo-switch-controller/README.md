# M5 Demo Switch controller

`DemoSwitchController.ino` sends the normative UDP 7710 `SWITCH` command and listens for `ACK`, `READY`, or `FAILED` on local UDP 7711.

1. Install M5Unified with an ESP32 Arduino toolchain.
2. Edit `WIFI_SSID`, `WIFI_PASSWORD`, `HMD_IP`, `CONTROLLER_ID`, and `TARGET_DEMO_A/B/C` in the sketch. Each target must be a logical demo ID present in the foreground APK's local allowlist.
3. Set `SHARED_SECRET` to the same value provisioned locally in each APK, or leave it empty only when every APK explicitly enables isolated-LAN unsigned mode.
4. Flash the M5. Buttons A, B, and C select their corresponding logical demo. Every press increments and persists `seq`, and HMAC canonicalization uses the selected target.

The sketch does not use Hapbeat UDP 7700 and cannot send haptics or audio.
