# hapbeat-demos

Hapbeat を体験するためのデモを配布するリポジトリ。**ビルド済みデモアプリ**と、**既存の VR ゲームに触覚を後付けする mod** の 2 系統を収録する。

触覚の体験にはHapbeat実機が必要です。Boxingなどのゲーム操作・描画は、デバイス未接続でも確認できます。

## 1. ビルド済みデモアプリ

最新のビルドは [Releases](https://github.com/hapbeat/hapbeat-demos/releases/latest) から取得できる。

| ファイル | 内容 | 対象 |
|---|---|---|
| `hapbeat-handdemo_all.apk` | XR Interaction Toolkit の Hands Interaction Demo に Hapbeat の触覚を追加したもの。掴む・押す・スナップ・こするの各操作に haptics が返る | Meta Quest（ハンドトラッキング対応機） |

インストール手順（adb / SideQuest / ストアのリリースチャンネル）はドキュメントポータルで説明している。

→ **[XRI デモを APK で試す](https://devtools.hapbeat.com/docs/sdk-integration/unity-sdk/xri-handdemo-apk/)**

Hapbeat と Quest を**同じ Wi-Fi** に接続してから起動する。

## 2. 既存ゲーム向け mod / 触覚 Kit

既存の市販ゲームに mod で触覚を後付けするツール群は、
[hapbeat-modkit](https://github.com/hapbeat/hapbeat-modkit) に移動した。
Pistol Whip / Robo Recall / Blade & Sorcery 向け mod、共通送信コア、
触覚 Kit (vr-shooter-kit) はそちらを参照。

## 3. デモのソースコード

各デモのソースは、1 プロジェクト 1 リポジトリで管理している。公開準備が済んだものから順次 public にする。

| デモ | エンジン | リポジトリ |
|---|---|---|
| Boxing（Quest 向け 90 秒スパーリング） | Unity | `hapbeat-demo-boxing-vr`（公開準備中） |
| GloveBall / Volley（Ultimate Glove Ball アリーナ資産を使った 1 人プレイデモ） | Unity | `hapbeat-demo-gloveball`（公開準備中） |
| T-Rex Encounter | Unreal | `hapbeat-demo-trex-encounter`（公開準備中） |
| Safety Mill VR（フライス盤の安全教育） | Unreal | `hapbeat-demo-safety-mill-vr`（公開準備中） |

ライセンス上ソース形式で再配布できない第三者アセット（Unity XR Hands のサンプルモデル、Meta の手モデル等）は、各リポジトリに含めない。代替モデルや入手手順は各リポジトリの README に記載する。

このリポジトリには、デモ共通の [Demo Switch package](unity/packages/com.hapbeat.demo-switch/README.md)、APK を切り替える [Demo Hub](unity/demo-hub/README.md)、補助スクリプト（`tools/`）と、ビルド済みアプリの Releases を置く。

デモ一式の Quest へのインストールは `tools/install-demos.cmd -Group 1` のように行う。`-Group` / `-Player` を付けると、その Quest の全デモの触覚の宛先（`hapbeat-device.json`）も同時に書き込む。2 台目は `-Group 2`。

Quest の画面を PC に映すには `tools/quest-mirror.cmd` をダブルクリックする（同じ LAN 内の Wi-Fi adb + [scrcpy](https://github.com/Genymobile/scrcpy)。インターネット不要、表示のみ・音声なし。接続中の Quest ごとに「Quest 1 / Quest 2」のウィンドウを並べる）。Quest を再起動した直後は USB を挿した状態で `tools/enable-quest-wifi-adb.cmd`（または quest-mirror.cmd）をダブルクリックすると Wi-Fi adb に切り替わり、ケーブルを抜いてよい。

## 4. 独立 APK の切替

[`com.hapbeat.demo-switch`](unity/packages/com.hapbeat.demo-switch/README.md) は、前面の Unity APK が UDP 7710 の `DISCOVER` に `HERE` を unicast 応答して Quest の IPv4 を自動検出可能にし、logical demo ID を受けて端末内 allowlist に登録した次の APK を起動する共通 package。M5 controller は独立した `hapbeat-demo-switch-controller-firmware` repo で管理し、[Demo Switch controller tool](https://devtools.hapbeat.com/tools/demo-switch-controller/) から書き込みと設定を行う。触覚 UDP 7700 とは独立しており、この controller は触覚・音声を送信しない。

## ライセンス

このリポジトリに含まれる Hapbeat 製のソースコードは **MIT ライセンス**（[LICENSE](LICENSE)）。

ただし **Releases で配布するビルド済みデモアプリは対象外** で、それらに同梱される
サードパーティアセットは各々のライセンスに従う。デモごとの著作権表示は以下のとおり。

### hapbeat-handdemo

Unity 製の **XR Interaction Toolkit** のサンプルアセットを含む。

- XR Interaction Toolkit copyright © Unity Technologies
- ライセンス: [Unity Companion License](http://www.unity3d.com/legal/licenses/Unity_Companion_License)

## 関連

- [Hapbeat devtools portal](https://devtools.hapbeat.com/) — ドキュメント一式
- [hapbeat-unity-sdk](https://github.com/Hapbeat/hapbeat-unity-sdk) — 自分の Unity プロジェクトに触覚を組み込む場合はこちら
