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
- インストール: `tools\install-demo-remote.cmd` をダブルクリック（`-Build` を付けるとビルドから）。USB またはワイヤレスデバッグで見えている全スマホ・タブレットに入れて起動する（Quest は除外）
- Wi-Fi でインストール（Android 11 以上）: スマホの「開発者向けオプション > ワイヤレスデバッグ」を ON。初回だけ「ペア設定コードによるデバイスのペア設定」に出る IP:ポートとコードで `powershell -File tools\install-demo-remote.ps1 -Pair 192.168.0.50:37123 -Code 123456`。以後はワイヤレスデバッグが ON で PC と同じ LAN にいれば、ケーブル無しで見つかる（mDNS）
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

Quest を再起動すると、スマホからの Wi-Fi 接続（adb、TCP 5555）が切れる。アプリに「Quest の Wi-Fi adb が切れています」と出たら、次のどちらかで元に戻す。Demo Switch の切替・操作は adb なしでも使える。

**スマホと USB でつなぐ（PC 不要）**

スマホが USB ホストになって、PC の `adb tcpip 5555` と同じことをする。

1. スマホと Quest を USB-C ケーブル（データ対応）でつなぐ。アプリが開き「Quest と USB でつなぐ」が出る（アプリ表示中にスマホが周辺機器側になった時も、直し方を添えて出る）
2. 自動で始まる。初めてのスマホでは HMD を被り、「USB デバッグを許可」で「常に許可」にチェックして許可する（Wi-Fi adb と同じ鍵なので、一度許可すれば両方に効く）
3. 「完了。ケーブルを外してください（Quest の IP / prefix と SSID）」と出たらケーブルを外す（3 秒後にダイアログが閉じる）。Quest の IP を一覧に追加・選択し、Wi-Fi adb で自動接続する（1.5 秒おきに最大 6 回）

- 挿しても何も出ない時は、スマホが周辺機器側（Quest 側がホスト）になっている可能性が高い。メイン画面の「USB でつなぐ」（または「設定 → USB でつなぐ」）でダイアログを開き、「USB の状態」を見る。Quest がホスト認識済みならダイアログを開くだけで始まる
- うまくいかない時は「もう一度試す」。ダイアログの「USB の状態」で原因を切り分ける
  - 「スマホが周辺機器側（Quest 側がホスト）」: ケーブルを挿し直すか、スマホの通知の「USB の制御」で「このデバイス」を選ぶ
  - 「Quest は見えるが USB デバッグ無効」: Quest の開発者モード（USB デバッグ）を確認する
  - 「未接続」: データ通信できる USB-C ケーブルか、Quest の電源を確認する
  - 状態欄の下の小さい 1 行は、スマホから見えている USB 機器（VID:PID とインターフェース）。全文はログに残る
- 機種（`ro.product.model`）が Quest でない端末には `tcpip` を打たず「Quest ではありません」と出す
- Quest がすでに 5555 で待ち受けている時は `tcpip` を省く（adbd を再起動しない）
- Quest の Wi-Fi がスマホと別のネットワーク（サブネット）だと Wi-Fi 接続は試さず、「Quest は別のネットワークにいます」と両方の IP・SSID を出す。スマホが Wi-Fi に繋がっていない時は「スマホが Wi-Fi に繋がっていません」と Quest の SSID を出す
- スマホが USB ホストになれない端末では使えない

**PC から**

Quest を USB で PC に繋いで `tools/enable-quest-wifi-adb.cmd` をダブルクリックし、アプリで「adb 接続」を押す。

## トラブル対応

| 表示 | 意味 | 対応 |
|---|---|---|
| Quest の Wi-Fi adb が切れています | Quest には届くが 5555 が閉じている（再起動後など） | スマホと Quest を USB でつなぐ（上記。何も出なければ「USB でつなぐ」ボタン） |
| Quest（IP）に届きません（スリープ／別の Wi-Fi／端末間通信の遮断） | 5555 に何も応答しない | HMD を被って画面を点ける／同じ Wi-Fi か確認（診断行）／ルーターの端末間通信の遮断（AP アイソレーション）を確認 |
| USB での有効化は成功しました。スマホから Quest に届きません | Quest 側は有効。経路の問題 | 同じ Wi-Fi か、ルーターの端末間通信の遮断を確認 |
| 接続待ち: …（灰色） | アプリ起動時・Quest 選択時の自動接続が失敗しただけ | 30 秒ごとの走査で Quest の 5555 が開いたら自動で接続し直す。すぐ繋ぐなら「adb 接続」 |
| 応答なし（デモが起動していない／スリープ／届いていない） | 「操作」の前面確認に応答がない | Demo Switch 対応デモを起動する・HMD を被って画面を点ける |
| Quest のデモが前面にありません（HMD 内でメニューや一時停止を閉じてください） | デモは起動しているが、メニュー・一時停止・境界設定などで前面にない（状態表示が「前面: X（非前面: …）」）。この間 Quest は切替・操作を断る | HMD を被ってメニュー・一時停止・境界設定を閉じる |

選んだ Quest が adb 未接続の間は、30 秒ごとに 5555 を走査し直す。Wi-Fi に繋がり直した時も走査する。選んだ Quest が別の IP に移った時の追跡（他の 5555 のホストに adb で接続してシリアルを比べる）は、ホストごとに 1 回だけ試す（他のブースの Quest に許可ダイアログを繰り返し出さないため）。「再探索」で試し直す。

**診断行の読み方**（Quest の状態欄の下の小さい 2 行）

- 1 行目: スマホの Wi-Fi の IP / prefix と GW。「Wi-Fi 未接続」なら broadcast も走査もしない（モバイル回線には送らない）。USB でつないだ後は、続けて USB で読んだ Quest の IP / prefix と SSID（「USB: Quest …」）
- 2 行目: 最後の探索（送信数・応答数、うち broadcast への応答）と 5555 走査（時刻・見つかった台数）

「デモ応答 ○」は Demo Switch 対応デモが **起動している** ことを表す（前面とは限らない）。メニュー・一時停止・境界設定の間も応答するが、切替・操作は断られる。前面かどうかは「前面: X」の後ろに「（非前面: …）」が出るか、「操作」タブの状態欄の「非前面」で分かる。

「デモ応答」も「adb」も × の時は、次の順に見る。

1. 1 行目: スマホが Wi-Fi に繋がっているか。GW が Quest 側のネットワークと同じか
2. 2 行目の探索: 送信が 0 や失敗が出ていれば、スマホ側の Wi-Fi・ソケットの問題
3. 応答 0 で 5555 走査も 0 台なら、Quest に何も届いていない（スリープ、別の Wi-Fi、端末間通信の遮断）。USB でつなぐと Quest の IP・SSID がログに出るので比べる
4. 走査で見つかるのに応答 0 なら、Demo Switch 対応デモが起動していない（ホーム画面など）

**ログ**: 結果ログと接続の各段階（USB の状態、getprop、Quest の IP / SSID、tcpip の応答、Wi-Fi adb の試行結果、接続時の Quest の状態）をスマホ内に保存する（最新 500 行）。「設定 → ログ → ログをコピー」で貼り付けて報告に使える。

## 画面ミラー

- 既定: 最大サイズ 1024、8 Mbps、30 fps、左目のみ（横長パネルを半分に crop）
- 設定画面で 最大サイズ 720 / 1024 / 1280 / 1600、ビットレート 2 / 4 / 8 Mbps、30 / 60 fps、「両目を表示」を変更できる。カクつく端末では最大サイズかビットレートを下げる
- デコードが詰まった時はキーフレームまで映像を捨て、2 秒以内に来なければ server を再起動する
- アプリを背面に回すと停止し、前面に戻ると再開する
- PC の `tools/quest-mirror.cmd` と同時に使える（adbd は複数の TCP クライアントを受ける）

## ライセンス

本アプリのソースは repo の MIT ライセンス。同梱するサードパーティソフトウェアは [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)（アプリ内「設定 → ライセンス」でも表示）。
