# Third-party notices

Hapbeat Demo Remote のソースは hapbeat-demos repo の MIT ライセンス。APK には以下のサードパーティソフトウェアを含む。いずれも Apache License 2.0（全文はアプリ内「設定 → ライセンス」および `app/src/main/assets/licenses/APACHE-2.0.txt`）。

| ソフトウェア | 著作権者 | ライセンス | 用途 |
|---|---|---|---|
| [scrcpy-server](https://github.com/Genymobile/scrcpy) v4.1 | Copyright (C) 2018 Genymobile / Copyright (C) 2018-2026 Romain Vimont | Apache-2.0 | 画面ミラー（未改変のリリースバイナリを実行時に Quest へ転送） |
| [dadb](https://github.com/mobile-dev-inc/dadb) 2.0.0 | mobile.dev inc. | Apache-2.0 | Wi-Fi adb クライアント |
| [Okio](https://github.com/square/okio) | Square, Inc. | Apache-2.0 | dadb の I/O |
| [zxing-android-embedded](https://github.com/journeyapps/zxing-android-embedded) 4.3.0 | ZXing authors / Journey Mobile | Apache-2.0 | プリセット取り込みの QR 読み取り |
| [ZXing core](https://github.com/zxing/zxing) 3.4.1 | ZXing authors | Apache-2.0 | QR のデコード（zxing-android-embedded が使用） |
| [AndroidX / Jetpack Compose](https://developer.android.com/jetpack/androidx) | The Android Open Source Project | Apache-2.0 | UI |
| [Kotlin](https://github.com/JetBrains/kotlin) / [kotlinx.coroutines](https://github.com/Kotlin/kotlinx.coroutines) | JetBrains s.r.o. and contributors | Apache-2.0 | 言語ランタイム |

## Windows 版

上のうち QR 読み取り（zxing）以外を含み、次を加える。全文はアプリ内「設定 → ライセンス」と `desktop/src/main/resources/licenses/`。

| ソフトウェア | 著作権者 | ライセンス | 用途 |
|---|---|---|---|
| [FFmpeg](https://ffmpeg.org) 7.1（[bytedeco の presets](https://github.com/bytedeco/javacpp-presets/tree/master/ffmpeg) の LGPL ビルド `org.bytedeco:ffmpeg:7.1-1.5.11`、未改変の DLL） | FFmpeg developers | LGPL 3.0 以上（`avcodec_license()` で確認） | ミラーの H.264 デコード |
| [JavaCPP](https://github.com/bytedeco/javacpp) / JavaCPP Presets | Samuel Audet | Apache-2.0 | FFmpeg の呼び出し |
| [Compose Multiplatform](https://github.com/JetBrains/compose-multiplatform) / Skiko | JetBrains s.r.o. | Apache-2.0 | UI |
| [JNA](https://github.com/java-native-access/jna) | JNA contributors | Apache-2.0 | 画面を消さない設定（SetThreadExecutionState） |
