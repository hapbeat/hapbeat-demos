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

## 2. 既存 VR ゲーム向け mod（`mods/`）

市販の VR ゲームに、ゲーム内イベント（発砲・被弾・敵撃破など）で Hapbeat へ UDP コマンドを送る
mod を後付けする。**ゲーム本体は各自で用意する必要がある**（このリポジトリにゲームのデータや
コードは一切含まれない）。いずれも PC VR 向け。

| mod | 対象ゲーム | 方式 | 触覚が返るイベント |
|---|---|---|---|
| [`mods/pistolwhip-hapbeat`](mods/pistolwhip-hapbeat) | Pistol Whip (Steam) | MelonLoader | 発砲・近接・リロード・被弾・低体力・死亡・**BGM の拍** |
| [`mods/roborecall-hapbeat`](mods/roborecall-hapbeat) | Robo Recall (Rift PC 版) | 公式 Mod Kit の UE プラグイン | 発砲・敵撃破・被弾（Blueprint から呼び出す） |
| [`mods/bladesorcery-hapbeat`](mods/bladesorcery-hapbeat) | Blade & Sorcery (PCVR) | 公式 BasSDK の Scripted Mod | 敵ヒット・敵撃破・パリィ・被弾・低体力・囲まれ |

導入手順・ビルド方法・実機確認チェックリストは、各 mod の README を参照。

送信処理は [`mods/shared`](mods/shared) の共通コアに集約してある（プロトコル実装・宛先解決・
keep-alive・設定ファイル）。mod 側は論理イベント名を投げるだけで、event ID と強度は
`hapbeat_settings.json` で差し替えられる。

## 3. 触覚 Kit（`kits/`）

| Kit | 内容 |
|---|---|
| [`kits/vr-shooter-kit`](kits/vr-shooter-kit) | 上記 3 mod が共通で使うクリップ 9 本（発砲・被弾・鼓動・拍など）と manifest |

Kit は [Hapbeat Studio](https://studio.hapbeat.com/) からデバイスへ deploy する。手順は Kit の README を参照。

## ライセンス

このリポジトリに含まれる Hapbeat 製のソースコード（`mods/` の各 mod と共通コア、`kits/` の
生成スクリプト）は **MIT ライセンス**（[LICENSE](LICENSE)）で提供する。

ただし **Releases で配布するビルド済みデモアプリは対象外** で、それらに同梱される
サードパーティアセットは各々のライセンスに従う。デモごとの著作権表示は以下のとおり。

### hapbeat-handdemo

Unity 製の **XR Interaction Toolkit** のサンプルアセットを含む。

- XR Interaction Toolkit copyright © Unity Technologies
- ライセンス: [Unity Companion License](http://www.unity3d.com/legal/licenses/Unity_Companion_License)

### VR ゲーム向け mod

このリポジトリに含まれるのは Hapbeat 側の送信コードのみで、対象ゲームのコード・アセット・
逆コンパイル結果は一切含まない。mod の利用はゲーム本体の EULA に従うこと（非公式 mod の
導入を制限しているタイトルもある）。Blade & Sorcery 向けの mod は、Warpfrog の BasSDK
ライセンスにより**無償配布に限る**。

## 関連

- [Hapbeat devtools portal](https://devtools.hapbeat.com/) — ドキュメント一式
- [hapbeat-unity-sdk](https://github.com/Hapbeat/hapbeat-unity-sdk) — 自分の Unity プロジェクトに触覚を組み込む場合はこちら
