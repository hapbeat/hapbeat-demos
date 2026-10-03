# Hapbeat Demo Remote（Android）

展示・デモ運営用の Android リモコンアプリ。同じ LAN 上の Meta Quest に対して次を行う。

- **アプリ切替 / アプリ操作**: Demo Switch プロトコル（UDP 7710、`hapbeat-contracts` の `specs/demo-switch-control.md`）で `SWITCH` / `CONTROL` を送る。M5 controller（`hapbeat-demo-switch-controller-firmware`）の代替として使え、併存もできる
- **直接起動**: Wi-Fi adb で固定表のデモアプリを起動する（「Hub を開く」は Quest ライブラリで見つけにくい Demo Hub を前面に出すため）
- **画面ミラー**: [scrcpy](https://github.com/Genymobile/scrcpy) server v4.1 で Quest の画面を表示する（表示のみ・音なし・入力なし）

このアプリは触覚（UDP 7700）を送らない。

## 対応 Android

**Android 8.0（API 26）以上**。低遅延デコードフラグ（`KEY_LOW_LATENCY`）は Android 11 以上でのみ有効。廉価タブレットでも動くよう、ミラーの既定値は控えめにしている。

## ビルド

Android Studio 同梱の JDK を使う（PowerShell）。

```powershell
cd android/demo-remote
$env:JAVA_HOME='C:\Program Files\Android\Android Studio\jbr'
.\gradlew.bat assembleDebug
```

- APK: `app/build/outputs/apk/debug/app-debug.apk`
- インストール: `adb install -r app\build\outputs\apk\debug\app-debug.apk`
- `local.properties`（`sdk.dir=...`）は各 PC で用意する（commit しない）
- scrcpy-server はビルド時に GitHub Releases から取得し、SHA-256 を検証して APK に同梱する（repo には置かない）。`tools/quest-mirror/scrcpy/scrcpy-server` に同じハッシュのファイルがあればそれを使う（オフラインビルド用）
- ユニットテスト: `.\gradlew.bat :app:testDebugUnitTest`

## 初回設定

1. 起動時に認証モードを選ぶ。現在のデモ受信側はすべて **署名なしモード** なので、外部から隔離したデモ専用 LAN では「隔離 LAN で署名なしを使う」を選ぶ。受信側に shared secret を設定している場合は同じ値を入れる（保存後は「設定済み」とだけ表示される）
2. Quest は Demo Switch の自動探索（5 秒ごと）で一覧に出る。設定画面から IPv4 を手動追加・ラベル変更もできる
3. **宛先は一覧から自分で選ぶ**（複数の Quest が応答しても自動では選ばない）
4. 選んだ Quest に adb 接続すると、初回はヘッドセット内に USB デバッグの許可ダイアログが出る。「このコンピューターから常に許可」にチェックして許可する（アプリは最大 30 秒待つ）

Android 端末と Quest は **同じ LAN** に接続する。

## Quest 再起動後の Wi-Fi adb 復旧

Quest を再起動すると Wi-Fi adb（5555）が無効になる。アプリに「Quest の Wi-Fi adb が無効です」と出たら、Quest を USB で PC に繋いで `tools/enable-quest-wifi-adb.cmd` をダブルクリックし、アプリで「adb 接続」を押す。Demo Switch の切替・操作は adb なしでも使える。

## 画面ミラー

- 既定: 最大サイズ 1024、8 Mbps、30 fps、左目のみ（横長パネルを半分に crop）
- 設定画面で 最大サイズ 720 / 1024 / 1280 / 1600、ビットレート 2 / 4 / 8 Mbps、30 / 60 fps、「両目を表示」を変更できる。カクつく端末では最大サイズかビットレートを下げる
- デコードが詰まった時はキーフレームまで映像を捨て、2 秒以内に来なければ server を再起動する
- アプリを背面に回すと停止し、前面に戻ると再開する
- PC の `tools/quest-mirror.cmd` と同時に使える（adbd は複数の TCP クライアントを受ける）

## ライセンス

本アプリのソースは repo の MIT ライセンス。同梱するサードパーティソフトウェアは [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)（アプリ内「設定 → ライセンス」でも表示）。
