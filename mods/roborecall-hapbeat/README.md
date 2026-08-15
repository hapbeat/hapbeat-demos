# Robo Recall × Hapbeat — UE4 Mod Kit 用プラグイン

Robo Recall（PC 版）の公式 **Robo Recall Mod Kit** に組み込んで、発砲・敵撃破・プレイヤー被弾のタイミングで
Hapbeat デバイスへ UDP の触覚コマンドを送るプラグイン。

このディレクトリだけを見て導入できるように書いてある。読む順番は上から順でよい。

> **前提の明示**
> Epic の Robo Recall 公式 modding ドキュメントは既に公開停止されており（2026-08 時点で全 URL が 403 /
> 現行ドキュメントへリダイレクト）、ゲーム側の Blueprint クラス名は公開情報として存在しない。
> したがって「プラグインを置いてビルドする」ところまでは本 README で完結するが、
> **どの Blueprint のどこに触覚ノードを差すか**は Mod Kit エディタを開いて自分で特定する必要がある。
> その探し方も本 README に書いてある（[エディタ内 Blueprint 配線手順](#エディタ内-blueprint-配線手順)）。

---

## 1. 完成形

```
発砲 / 撃破 / 被弾 の Blueprint イベントグラフ
        │  （エディタで手配線する）
        ▼
  Hapbeat Send Fire / Kill / Player Hit  ← このプラグインが提供する Blueprint ノード
        │
        ▼
  論理イベント名 "fire" / "kill" / "player_hit"
        │  （Config/HapbeatMod.txt で kit の event id にマップ）
        ▼
  UDP :7700 → Hapbeat デバイス
```

mod のコードに event id もワイヤ形式も出てこない。**論理イベント名だけ**を投げ、
実際に鳴らすクリップと強さは設定ファイル側で決まる（リビルド不要でチューニングできる）。

---

## 2. 必要なもの

| | 内容 | 備考 |
|---|---|---|
| ゲーム | Robo Recall（PC / Rift 版） | 実際に遊ぶ場合。Mod Kit だけでも最初のミッションは動く |
| ツール | **Robo Recall Mod Kit** | Epic Games Launcher の「Unreal Engine → Library」から取得。既定インストール先 `C:\Program Files\Epic Games\RoboRecallModKit` |
| コンパイラ | **Visual Studio 2015**（C++ ワークロード込み） | Mod Kit は UE 4.16 系のカスタムエンジンで、当時のツールチェーンが VS2015。新しい VS で通るかは要検証（[チェックリスト 5](#実機確認チェックリスト)） |
| ハード | Hapbeat デバイス 1 台以上 | PC と同一 LAN |
| ツール | Hapbeat Studio + hapbeat-helper | kit の書き込みに使う |
| kit | `kits/vr-shooter-kit/` | 本リポジトリ同梱 |

ディスク: Mod Kit は 20 GB 以上（この数値は二次情報。実際の要求量は Launcher の表示を見ること）。

---

## 3. プラグインを配置する

Mod Kit をインストールしたら、このディレクトリの `HapbeatMod` フォルダを **丸ごと**
Mod Kit のプラグインフォルダへコピーする。

```
C:\Program Files\Epic Games\RoboRecallModKit\RoboRecall\Plugins\HapbeatMod\
    HapbeatMod.uplugin
    Config\HapbeatMod.txt
    Source\HapbeatMod\
        HapbeatMod.Build.cs
        Public\*.h
        Private\*.cpp
```

PowerShell で:

```powershell
$Kit = "C:\Program Files\Epic Games\RoboRecallModKit"
Copy-Item -Recurse ".\HapbeatMod" "$Kit\RoboRecall\Plugins\HapbeatMod"
```

`Program Files` 配下なので管理者権限のコンソールが要る場合がある。
インストール先を変えている場合はそのパスに読み替える。

> **なぜ「プラグイン」なのか**
> この Mod Kit では **mod の単位が `"IsMod": true` を持つ UE プラグイン**。実際に配布された
> 既存 mod（RoboRevive / 3DRudder の Rudder）がどちらもこの形。本プラグインは後者、
> つまり「エンジンソースに手を入れない自己完結プラグイン」の形に倣っている。

---

## 4. ビルドする

### 4-1. Visual Studio プロジェクトを用意する

Mod Kit エディタ（`RoboRecallModKit\Engine\Binaries\Win64\UE4Editor.exe`）を起動し、
`RoboRecall.uproject` を開く。

C++ を含む mod を扱うと、エディタは Visual Studio ソリューションを生成して VS を起動しようとする。
最短経路は次のどちらか:

- **A. New Game Mod ウィザードを一度通す** — エディタの「New Game Mod」で **C++** の mod を 1 つ作る。
  これでソリューション（`RoboRecall.sln`）が生成され、VS が開く。作った空 mod はそのまま捨ててよい。
  以後は同じソリューションに `HapbeatMod` が含まれる。
- **B. 手動生成** — `RoboRecall.uproject` を右クリック → *Generate Visual Studio project files*。
  （このコンテキストメニューが Mod Kit の登録状態によっては出ないことがある。出なければ A を使う）

### 4-2. ビルド

VS でソリューションを開き、構成 **Development Editor / Win64** でビルドする。

> 既知の落とし穴: このバージョンのツールチェーンには、新規作成した mod プロジェクトが
> `RoboRecall` の代わりに VS の「スタートアッププロジェクト」にされてしまうバグの報告がある。
> VS からエディタが起動しない場合は、ソリューションエクスプローラで **RoboRecall** を右クリック →
> *Set as StartUp Project* にしてから実行する。

**ビルドが `HapbeatMod.Build.cs` のコンストラクタで失敗したら**、
`HapbeatMod.Build.cs` の先頭コメントを読むこと。この世代のエンジンは旧式の

```csharp
public HapbeatMod(TargetInfo Target)
```

を期待するのでそう書いてある。もし手元のエンジンが新しい形を要求するなら、

```csharp
public HapbeatMod(ReadOnlyTargetRules Target) : base(Target)
```

に差し替える。どちらかで必ず通る。

**`FUdpSocketReceiver has no member named 'Start'` で失敗したら**、
`Private/HapbeatSender.cpp` 冒頭の

```cpp
#define HAPBEAT_UDP_RECEIVER_HAS_START 1
```

を `0` にする。古い世代ではコンストラクタが受信スレッドを起動していたため `Start()` が無い。

**ヘッダが見つからない系のエラー**が出たら、該当行に `// VERIFY-4.16` のコメントが付いている。
そこに旧パス（フォルダ接頭辞なしの形）が書いてあるので差し替える。

---

## 5. エディタ内 Blueprint 配線手順

ここが本 mod の唯一の手作業。**プラグインは「ノードを提供する」だけで、自分からゲームを覗きに行かない。**
発砲・撃破・被弾のタイミングは、ゲーム側の Blueprint にノードを差して教えてやる必要がある。

### 5-1. 提供される Blueprint ノード

Blueprint のイベントグラフで右クリックして検索するとこれらが出る（カテゴリ **Hapbeat**）。
どれもターゲットピンの無い static ノードなので、どの Blueprint からでも呼べる。

| ノード | 論理イベント | 既定の触覚 |
|---|---|---|
| **Hapbeat Send Fire** | `fire` | `vr-shooter-kit.shot_recoil` |
| **Hapbeat Send Kill** | `kill` | `vr-shooter-kit.kill_confirm` |
| **Hapbeat Send Player Hit** | `player_hit` | `vr-shooter-kit.hit_heavy` |
| **Hapbeat Send Custom**（文字列引数） | 任意 | 設定ファイルで定義した任意のイベント |
| **Hapbeat Stop Event**（文字列引数） | 任意 | ループクリップの停止 |
| **Hapbeat Stop All** | — | 全停止 |
| **Hapbeat Set Enabled**（bool） | — | 実行中の on/off |
| **Hapbeat Alive Device Count**（純粋関数, int） | — | 直近 15 秒に応答したデバイス台数 |
| **Hapbeat Is Socket Open**（純粋関数, bool） | — | UDP ソケットが開けているか。`false` = そもそも 1 バイトも送れていない |
| **Hapbeat Reload Settings** | — | 設定ファイルの読み直し |

> **`fire` / `kill` / `player_hit` / `hit_light` はワンショット、`heartbeat` だけがループ。**
> 設定ファイルの既定に入っている `heartbeat` は kit 側で `loop=true`（`vr-shooter-kit.heartbeat`）なので、
> **開始と停止を対で配線しないとデバイスで鳴り続ける**。他のイベントには停止の配線は要らない。

### 5-2. 差す先の Blueprint を探す

Content Browser の検索窓と、右上の *Settings → Show Engine Content / Show Plugin Content* を有効にして探す。
当たりの付け方:

1. **武器（発砲）** — 検索語: `Weapon`, `Gun`, `Pistol`, `Revolver`, `BP_` 接頭辞。
   Mod Kit のテンプレートに「Custom Revolver」系の武器 mod があるはずなので、
   **そのテンプレートが複製元にしている武器 Blueprint がそのまま正解**。まずそれを開く。
2. **敵ボット（撃破）** — 検索語: `Bot`, `Enemy`, `Robot`, `Character`。
   `Destroyed` / `Death` / `Die` / `OnDestroyed` といったイベント、あるいは
   `Destroy Actor` を呼んでいる箇所の直前がフック点。
3. **プレイヤー（被弾）** — 検索語: `Pawn`, `Player`, `Hand`。
   Epic は Robo Recall のプレイヤーアバターを「Player Pawn」と呼んでいる。
   `Any Damage` / `Point Damage` / `Take Damage` / `Health` を扱っているノードの近くがフック点。
4. **低体力（heartbeat ループ）** — 3 と同じ Player Pawn / HUD 系を、
   検索語 `Health`, `LowHealth`, `Critical`, `Vignette`, `Heal` で当たる。
   探すのは「イベント」ではなく **状態の切り替わり**（体力が閾値を下回った瞬間 / 回復して戻った瞬間）。
   ゲーム側にそういう分岐が無ければ、被弾処理の後で残り体力を比較する分岐を自分で足してもよい。
   **開始点と終了点の 2 箇所**が要る（[5-3](#5-3-ノードを差す) 参照）。

> 1〜3 はワンショットなので配線は 1 箇所で完結する。**4 だけは 2 箇所**（開始と停止）。

Blueprint を開いたら **Event Graph** タブへ行き、`Ctrl+F` でグラフ内検索して上のキーワードを当たる。
「既にゲームがその瞬間にやっていること」（音を鳴らす、エフェクトを出す、体力を減らす）を見つけて、
**その実行ピンの列に割り込ませる**のが一番外さない。

### 5-3. ノードを差す

例（発砲）:

```
[ 既存の発砲処理 ] ──▶ [ 既存の次のノード ]
```
を
```
[ 既存の発砲処理 ] ──▶ [ Hapbeat Send Fire ] ──▶ [ 既存の次のノード ]
```
に変える。実行ピンを 1 つ経由させるだけで、戻り値も分岐も無い。

被弾側も同様に、体力を減らしている処理の直後に **Hapbeat Send Player Hit** を差す。
撃破側は、ボットが死ぬ処理（Destroy / Death 系）の直前に **Hapbeat Send Kill** を差す。

> 「敵に当てた」と「敵を倒した」を分けたい場合は、ヒット処理側に
> **Hapbeat Send Custom** で `hit_light` を差す（設定ファイルに既定で入っている）。

**低体力の heartbeat（ここだけ 2 箇所）**:

```
[ 体力が閾値を下回った ] ──▶ [ Hapbeat Send Custom ("heartbeat") ] ──▶ [ 既存の次のノード ]

[ 回復した / 体力が閾値に戻った ] ──▶ [ Hapbeat Stop Event ("heartbeat") ] ──▶ [ 既存の次のノード ]
[ 死亡 / ウェーブ終了 / リスタート ]  ──▶ [ Hapbeat Stop Event ("heartbeat") ]（または Hapbeat Stop All）
```

`heartbeat` は kit 側で `loop=true`（`vr-shooter-kit.heartbeat`）。
**停止を配線し忘れると、体力が戻っても・死んでも・PIE を止めても鳴り続ける**
（デバイスは STOP を受け取るまでループを続ける）。文字列は設定ファイルの論理イベント名と
完全一致させること（`event.heartbeat.id=` の左側）。

配線し忘れて鳴りっぱなしになったときは、Level Blueprint に **Hapbeat Stop All** を仮置きして 1 回叩けば止まる。

### 5-4. 直接編集するか、複製するか

この Mod Kit のワークフローが、出荷済み Blueprint の**その場編集**を許すのか、
**複製した mod 所有アセットへの差し替え**しか許さないのかは公開情報では確定できなかった。
実際に開いてみて判断する:

- **その場で編集して保存できる** → それが最短。そのまま進む。
- **保存できない / 読み取り専用扱い** → 対象 Blueprint を右クリック → *Duplicate* して
  mod 側のフォルダに置き、複製の方にノードを差す。この場合その mod は
  「触覚付きの武器／ボットに差し替える mod」になり、ゲーム内の Mods メニューから選択して有効化する形になる。

どちらでも配線作業そのものは同じ。違うのは配布・有効化の仕方だけ。

### 5-5. 動作確認（Play In Editor）

1. エディタ上部の **Play**（VR Preview でも通常プレビューでもよい）。
2. Output Log を開き、フィルタに `LogHapbeatMod` と入れる。
3. 期待するログ:
   - `HapbeatMod loaded.` — プラグインが読み込まれている
   - `Loaded settings from ...HapbeatMod.txt (N event bindings).` — 設定が読めている
   - `Hapbeat sender open (port 7700, app 'RoboRecall', unicast on).` — 最初の発砲でソケットが開く
4. 撃つ → デバイスが反応すれば完了。

反応しないときは **Hapbeat Is Socket Open** と **Hapbeat Alive Device Count** を画面に出す（Print String に繋ぐ）。
この 2 つで切り分けが付く:

| Is Socket Open | Alive Device Count | 意味 |
|---|---|---|
| `false` | `0` | **そもそも 1 バイトも送れていない**。ソケットが開けていない（ファイアウォールの許可ダイアログが出たまま / アダプタ未準備 / 設定が `enabled=false`）。Output Log に `Could not open the Hapbeat UDP socket` が出ているはず |
| `true` | `0` | 送れてはいるがデバイスから応答が無い（ネットワーク側）→ [7. うまく動かないとき](#7-うまく動かないとき) |
| `true` | `1` 以上 | 届いている。イベント id か kit の書き込みを疑う |

> ソケットが開けなかった場合、**10 秒後の次のイベントで自動的に開き直す**（1 発目がファイアウォールの
> ダイアログと重なっただけ、というケースでセッション全体を諦めないため）。
> すぐ試したいときは **Hapbeat Reload Settings** を叩けば即座に再試行する。

> **注意（PIE）**: ソケットはエディタ終了時にしか閉じない。PIE を止めても
> デバイス側の「接続中アプリ」表示は残る。気になるなら Level Blueprint の
> **Event End Play** に **Hapbeat Stop All** を繋ぐ。

---

## 6. Hapbeat 側の準備

1. **kit を書き込む** — Hapbeat Studio を開き、本リポジトリの `kits/vr-shooter-kit/` を読み込んで
   デバイスへ deploy する（Studio の Kit タブ → フォルダを開く → Deploy）。
   このプラグインが送る event id はすべて `vr-shooter-kit.*`。
2. **同一 LAN に置く** — PC（有線 / Wi-Fi どちらでもよい）とデバイスが同じサブネットにいること。
   PC 側は特別な設定不要。デバイスの Wi-Fi 設定は Studio から行う。
3. **疎通を先に確かめる** — ゲームを起動する前に、Studio からクリップを再生してデバイスが鳴ることを確認しておく。
   ここで鳴らなければ mod 側をいじっても意味がない。

送信は既定で **unicast**（PING に応答したデバイスへ直接送る / 応答が無ければ broadcast にフォールバック）。
複数 PC・複数プレイヤーで混線させたくないときは、設定ファイルの `player` / `group` を使う。

---

## 7. うまく動かないとき

| 症状 | 確認 |
|---|---|
| Output Log に `LogHapbeatMod` が全く出ない | プラグインが読み込まれていない。`Plugins\HapbeatMod\HapbeatMod.uplugin` の配置と、ビルドが通っているかを確認 |
| `Unknown logical event 'xxx'` | 設定ファイルにその論理イベントが無い。`event.xxx.id=` を足すか、ノード側の文字列を直す |
| `Could not create ...HapbeatMod.txt` | `Program Files` 配下で書き込めていない。組み込みの既定値がそのまま使われるので実害は無いが、編集したいならエディタを管理者で開くかファイルの権限を緩める |
| `... exists but could not be read` | 設定ファイルが他プロセスに掴まれている / 権限で読めない。**ファイルは書き換えずに既定値で動く**（編集済みの設定を失わないため）。掴んでいるエディタを閉じるか権限を直して **Hapbeat Reload Settings** |
| `Could not open the Hapbeat UDP socket` / **Hapbeat Is Socket Open** が `false` | ソケットが作れていない。Windows ファイアウォールの許可ダイアログが出たまま放置されていないか、ネットワークアダプタが有効か、設定が `enabled=false` になっていないかを確認。10 秒後の次のイベントで自動再試行する（急ぐなら **Hapbeat Reload Settings**） |
| Alive Device Count が 0 のまま（Is Socket Open は `true`） | デバイスと PC が別サブネット / Windows ファイアウォールが UE4Editor.exe の UDP を止めている / デバイスが Wi-Fi に繋がっていない。Studio 側で見えているかをまず確認 |
| 触覚が鳴りっぱなしで止まらない | ループする `heartbeat` の **Hapbeat Stop Event("heartbeat")** が配線されていない（[5-3](#5-3-ノードを差す)）。応急処置は **Hapbeat Stop All** |
| 撃つと 1 回だけ鳴ってその後鳴らない | `minIntervalMs` が大きすぎる可能性。既定 60 ms |
| 連射で触覚が団子になる | `minIntervalMs` を上げる（100〜150 ms 程度） |
| 強すぎる / 弱すぎる | `masterGain`、または個別の `event.<name>.gain` を調整 → **Hapbeat Reload Settings** ノードで再読込 |

---

## 8. 設定ファイル

`Plugins\HapbeatMod\Config\HapbeatMod.txt`（`key=value` 形式、`#` はコメント）。

```ini
enabled=true
appName=RoboRecall          # デバイスの OLED に出る名前。ワイヤ上 16 文字で切られる
player=-1                   # 1-99 で全送信の宛先を固定、-1 で無効
group=-1
masterGain=1.000            # 実際のゲイン = masterGain × event.<name>.gain
minIntervalMs=60            # 同じ論理イベントの最小送信間隔 (ms)。0 で無効
commandUnicast=true         # false にすると常に broadcast
port=7700
broadcastAddress=           # 空 = 自動。デバイスが見つからない時だけ手で書く（下記）

event.fire.id=vr-shooter-kit.shot_recoil
event.fire.gain=1.000
event.fire.enabled=true
```

- **1 つでも `event.` 行を書くと既定のイベントマップは丸ごと置き換わる。** 使うものを全部書くこと
- 変更は **Hapbeat Reload Settings** ノードか、エディタ再起動で反映
- `appName` は ASCII で書くこと（16 文字の切り詰め位置が非 ASCII だと他 SDK と 1 文字ずれ得る）

### デバイスが見つからないとき — `broadcastAddress`

`255.255.255.255` 宛のパケットは、**インターフェイスメトリックが最小のアダプタ 1 本からしか出ない**。
Hyper-V / WSL2 / Docker を入れた PC では、常時 Connected な仮想スイッチがそれになりがちで、
Hapbeat のいる Wi-Fi 網には一生届かない。**LAN ケーブルを挿していなくても起きる**のが分かりにくい点。

そこで既定では、各アダプタのアドレスから**サブネット宛のブロードキャスト**（例 `192.168.0.255`）を
組み立てて全部に探索を投げ、**デバイスが応答したサブネットに以後の送信を固定**する。
起動時のログに実際の宛先が出る:

```
LogHapbeatMod: Looking for devices on: 192.168.0.255, 255.255.255.255
LogHapbeatMod: Broadcasting to 192.168.0.255 (a device answered from 192.168.0.48).
```

自動判定は **/24（`255.255.255.0`）を仮定**している。家庭・オフィスのルータはこれを配るので通常は問題ないが、
**/16 や /25 のネットワークでは外れる**。その場合は実際のブロードキャストアドレスを書く:

```ini
broadcastAddress=192.168.255.255
```

> 補足: `hapbeat-unreal-sdk` には Windows の `GetAdaptersAddresses` / POSIX の `getifaddrs` で
> **実マスクを取得する完全な実装**（`Source/HapbeatSDK/Private/HapbeatNetInterfaces.cpp`）がある。
> 本 mod が /24 仮定に留めているのは、Mod Kit のエンジンでヘッダ配置と `iphlpapi.lib` の
> 追加が検証できないため。ビルドが通る環境が整ったら、そちらを移植するのが本筋。

---

## 9. ローカル検証（このリポジトリで実施済み）

Mod Kit が無い環境でも検証できる部分は検証してある。

**やったこと**: ワイヤ層（`HapbeatWire.h/.cpp`、UE 非依存の純 C++）を単体でコンパイルし、
**C# の正実装**（`mods/shared/HapbeatModCore/HapbeatProtocol.cs` + `HapbeatModClient.cs`）を
同じ入力で走らせた出力と 1 バイトずつ diff した。結果は**完全一致**。

再現手順:

```bash
cd Tools
g++ -std=c++11 -I ../HapbeatMod/Source/HapbeatMod/Public \
    ../HapbeatMod/Source/HapbeatMod/Private/HapbeatWire.cpp \
    HapbeatWireSelfTest.cpp -o wiretest
./wiretest > cpp.txt                                  # 内蔵アサートも同時に走る
dotnet run --project csharp-parity/CsharpParity.csproj --nologo -v q > cs.txt
diff cs.txt cpp.txt                                   # 差分なしなら OK
```

照合した内容:

- ヘッダ（magic / version / cmd / seq / payload_len のオフセットとリトルエンディアン）
- PLAY / STOP / STOP_ALL / PING / CONNECT_STATUS の各ペイロード（文字列の NUL 終端、
  int64・float32 のバイト順、app 名の 16 文字切り詰め）
- **上記 5 つ + PONG を `BuildPacket` で包んだ完成パケット**。ペイロードだけでなく
  `command_type` バイト（0x01 / 0x02 / 0x03 / 0x10 / 0x11 / 0x20）そのものが diff の対象に入っている
  （定数の取り違えを目視ではなく diff で落とすため）
- PONG の解析（拡張フィールドあり / レガシー 16 バイトのみ、どちらも）
- `AddressMatches` 26 ケース = **C# 正実装のテスト一式と同じケース表**
  （group セグメント一致 / group 省略 = 全 group / prefix + ワイルドカード併用 /
  末尾スラッシュの扱い / 部分ワイルドカード `pos_*` は**リテラル扱い**）。
  C# 側にある null 引数の 3 ケースだけは、C++ の `const std::string&` では表現できないため対象外
- `ResolveTarget` 11 ケース（player / group の差し込み位置）

フィールド単位の対照表は `Private/HapbeatWire.cpp` の冒頭コメントにある。

**検証できていないこと**: UE API 呼び出し（ソケット生成・受信スレッド・ティッカー・Blueprint 公開）は
Mod Kit が無いとコンパイルできない。4.16 世代で不確かな箇所には `// VERIFY-4.16` を付けてある。

---

## 実機確認チェックリスト

インストール後、最初にこれを潰すこと。1〜3 が本 mod の本体作業、4〜5 がビルド、6〜11 は環境依存。

1. **発砲 / 撃破 / 被弾に対応する Blueprint クラスとイベントグラフ上の位置を特定する**
   → 確認方法: [5-2](#5-2-差す先の-blueprint-を探す) の検索語で Content Browser を当たり、
   見つけた Blueprint に **Hapbeat Send Fire** を仮配線 → PIE で 1 発撃って Output Log
   （`LogHapbeatMod`）とデバイスの反応を見る。反応すればその位置で正しい。
2. **出荷済み Blueprint をその場で編集できるか、複製が必要か**
   → 確認方法: 対象 Blueprint を開いてノードを 1 つ足し、`Ctrl+S` で保存できるか試す。
   保存が拒否されたら複製方式（[5-4](#5-4-直接編集するか複製するか)）に切り替える。
3. **低体力状態の開始 / 終了に対応する Blueprint 上の分岐を特定し、`heartbeat` の開始と停止を対で配線する**
   → 確認方法: 低体力になるまでダメージを受け、`vr-shooter-kit.heartbeat` がループ開始することを確認 →
   回復（または死亡 / ウェーブ終了）で**確実に止まる**ことを確認。止まらなければ
   **Hapbeat Stop Event("heartbeat")** の配線漏れ（[5-3](#5-3-ノードを差す)）。
   ゲーム側に低体力の状態分岐が無い場合は「そもそも heartbeat を使わない」も選択肢
   （設定ファイルの `event.heartbeat.enabled=false`）。**この判断もここで下す。**
4. **`Build.cs` が旧式 `TargetInfo` コンストラクタを要求するか**
   → 確認方法: そのままビルド。コンストラクタ不一致で落ちたら
   `ReadOnlyTargetRules Target : base(Target)` に差し替えて再ビルド。どちらで通ったかを控えておく。
5. **VS2015 が必須か、新しい VS でも通るか**
   → 確認方法: 手元の VS でビルドしてみる。UBT がツールチェーンを見つけられない、
   あるいはリンクが通らない場合は VS2015（の C++ ビルドツール）を入れる。
6. **ソケットが実際に開けるか（ファイアウォール）と、失敗時の自動再試行が効くか**
   → 確認方法: 初回 PIE の 1 発目で Windows のファイアウォール許可ダイアログが出る想定。
   **わざとダイアログを放置したまま 1 発撃つ** → Output Log に
   `Could not open the Hapbeat UDP socket` が出て **Hapbeat Is Socket Open** が `false` になることを確認。
   その後ダイアログを許可し、10 秒後にもう一度撃って**自動的に開き直る**ことを確認する
   （[5-5](#5-5-動作確認play-in-editor)）。展示前に一度は通しておくこと。
7. **`.robo` ファイルの正体**（実 mod の repo で `.uproject` の隣にあるが形式不明）
   → 確認方法: テキストエディタで開いてみる。あわせてエディタの
   File → Package / Export 系メニューでテスト mod を書き出し、`.robo` が生成されるか観察する。
   配布形式が要るときだけ調べればよい。
8. **Mod Kit が standalone ビルド（Package/Cook）に対応しているか、エディタ内実行のみか**
   → 確認方法: File → Package Project を試す。メニューが無い / 失敗するなら、
   デモは Unreal Editor 上の Play で回すことになる。**展示ブースの段取りが変わるので早めに確認**。
9. **Mod Kit の実際のインストールサイズと、Epic Games Store 上で今も配信されているか**
   → 確認方法: Epic Games Launcher の Unreal Engine → Library を見る。
10. **Quest + Link で 2017 年の PC 版 Robo Recall が動くか**
   → 確認方法: Link 接続で実際に起動する。2019 年の Quest ネイティブ版
   「Robo Recall: Unplugged」は**別物で、この Mod Kit では mod できない**ので混同しないこと。
11. **`FUdpSocketBuilder` / `FUdpSocketReceiver` のこの世代でのシグネチャ**
   → 確認方法: ビルドエラーが出た箇所の `// VERIFY-4.16` コメントを見て、
   `RoboRecallModKit\Engine\Source\Runtime\Networking\Public\Common\` の実ヘッダと照らす。
   特に `FUdpSocketReceiver::Start()` の有無（[4-2](#4-2-ビルド) 参照）。

---

## ファイル一覧

```
roborecall-hapbeat/
  README.md                      ← これ
  HapbeatMod/                    ← Mod Kit の RoboRecall\Plugins\ へ丸ごとコピーする
    HapbeatMod.uplugin           　 IsMod: true / Runtime モジュール 1 つ
    Config/HapbeatMod.txt        　 既定設定（同梱。書き込めない環境でもこれが使われる）
    Source/HapbeatMod/
      HapbeatMod.Build.cs        　 旧式 TargetInfo コンストラクタ / Sockets+Networking+Projects
      Public/
        HapbeatWire.h            　 ワイヤ形式（純 C++、UE 非依存）
        HapbeatModConfig.h       　 設定スキーマ
        HapbeatModLog.h          　 ログカテゴリ
        HapbeatSender.h          　 ソケット / 受信 / keep-alive / unicast ルーティング
        HapbeatBlueprintLibrary.h　 Blueprint に出る唯一の面
      Private/
        HapbeatWire.cpp          　 ↑の実装 + C# 正実装との対照表コメント
        HapbeatModConfig.cpp
        HapbeatSender.cpp
        HapbeatBlueprintLibrary.cpp
        HapbeatModModule.cpp     　 モジュールの起動 / 終了
  Tools/                         ← 検証用。ゲームには不要
    HapbeatWireSelfTest.cpp      　 ワイヤ層の単体テスト + ダンプ
    csharp-parity/               　 C# 正実装で同じダンプを出す照合プログラム
```

## ライセンス・配布上の注意

- 本ディレクトリに入っているのは **Hapbeat 側の送信コードだけ**。Robo Recall / Mod Kit の
  コード・アセット・逆コンパイル結果は一切含まない。ゲーム側の型は Mod Kit のヘッダを
  **ビルド時に参照するだけ**で、本リポジトリにも配布物にも入らない。
- **他者の mod のコードは複製していない。** 既存 mod（LibreVR/RoboRevive、3DRudder の
  RoboRecallFreeLocomotion）は「Mod Kit の mod がどういうプラグイン構造を取るか」という
  事実確認にのみ参照し、実装はすべて新規に書いている。
- Mod Kit の利用そのものは Epic の規約に従うこと。ビルドした mod の配布可否・配布先も
  Epic 側の条件に従う。

## 参照

- `mods/shared/README.md` — 共通コア（C#）の仕様。設定スキーマと送信の挙動はこれに揃えてある
- `kits/vr-shooter-kit/manifest.json` — event id の正
- `hapbeat-contracts/specs/message-format.md` — ワイヤ形式の正
- `hapbeat-contracts/specs/device-addressing.md` — target / address のマッチ規則
