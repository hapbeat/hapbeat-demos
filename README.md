# hapbeat-demos

Hapbeat を体験するためのデモを配布するリポジトリ。**ビルド済みデモアプリ**と、**既存の VR ゲームに触覚を後付けする mod** の 2 系統を収録する。

**すべてのデモに Hapbeat 実機が必要。** デバイスが無いと何も起きない。

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

## 3. Unity デモのソーススナップショット

- [GloveBall](unity/gloveball/README.md) — Meta の Ultimate Glove Ball アリーナ資産を使った、Quest 向け 1 人プレイデモ

## 4. 独立 APK の切替

[`com.hapbeat.demo-switch`](unity/packages/com.hapbeat.demo-switch/README.md) は、前面の Unity APK が UDP 7710 の logical demo ID を受け、端末内 allowlist に登録した次の APK を起動する共通 package。M5 controller は独立した `hapbeat-demo-switch-controller-firmware` repo で管理し、[Demo Switch controller tool](https://devtools.hapbeat.com/tools/demo-switch-controller/) から書き込みと設定を行う。触覚 UDP 7700 とは独立しており、この controller は触覚・音声を送信しない。

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
