# hapbeat-demos

Hapbeat のビルド済みデモアプリを配布するリポジトリ。開発環境を用意せずに触覚フィードバックを体験するためのものです。

**すべてのデモに Hapbeat 実機が必要です。** デバイスが無いと何も起きません。

## ダウンロード

最新のビルドは [Releases](https://github.com/hapbeat/hapbeat-demos/releases/latest) から取得できます。

| ファイル | 内容 | 対象 |
|---|---|---|
| `hapbeat-handdemo_all.apk` | XR Interaction Toolkit の Hands Interaction Demo に Hapbeat の触覚を追加したもの。掴む・押す・スナップ・こするの各操作に haptics が返る | Meta Quest（ハンドトラッキング対応機） |

## インストール方法

adb / SideQuest / ストアのリリースチャンネルの 3 通りの手順を、ドキュメントポータルで説明しています。

→ **[XRI デモを APK で試す](https://devtools.hapbeat.com/docs/sdk-integration/unity-sdk/xri-handdemo-apk/)**

Hapbeat と Quest を**同じ Wi-Fi** に接続してから起動してください。

## ライセンス

各デモに含まれるサードパーティアセットの著作権表示は、デモごとに以下に記載します。

### hapbeat-handdemo

Unity 製の **XR Interaction Toolkit** のサンプルアセットを含みます。

- XR Interaction Toolkit copyright © Unity Technologies
- ライセンス: [Unity Companion License](http://www.unity3d.com/legal/licenses/Unity_Companion_License)

## 関連

- [Hapbeat devtools portal](https://devtools.hapbeat.com/) — ドキュメント一式
- [hapbeat-unity-sdk](https://github.com/Hapbeat/hapbeat-unity-sdk) — 自分の Unity プロジェクトに触覚を組み込む場合はこちら
