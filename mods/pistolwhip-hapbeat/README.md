# PistolWhipHapbeat — Pistol Whip に Hapbeat の触覚を後付けする mod

Steam PCVR 版 **Pistol Whip** に MelonLoader mod を入れて、発砲・被弾・リロード・低体力・
死亡・そして **BGM の拍** を Hapbeat デバイスへ触覚として送る。

ゲーム本体は改変しない。mod DLL を 1 つ置くだけで、外すときは削除するだけで元に戻る。

> **ゲームを持っている人向けの手順書。** 上から順にやれば導入できる。
> ただし、この mod の**フック対象は現行 Steam ビルドで未検証**。
> 最後の「[実機確認チェックリスト](#実機確認チェックリスト)」に、
> 初回導入時に確認すべきことをまとめてある。

---

## 1. 必要なもの

| | 内容 |
|---|---|
| ゲーム | Pistol Whip（Steam PCVR 版。Quest 単体版は非対応） |
| OS | Windows 10 / 11 |
| ビルド環境 | [.NET SDK 8 以降](https://dotnet.microsoft.com/download)（`dotnet --version` が通ること） |
| MelonLoader | 自動インストーラ（後述） |
| 触覚デバイス | Hapbeat 本体 1 台以上。**PC と同じ Wi-Fi / LAN** に接続済み |
| 触覚 Kit | このリポジトリの `kits/vr-shooter-kit`（デバイスへ書き込む音源一式） |

Hapbeat 本体の Wi-Fi 設定がまだなら、先に
[Hapbeat Studio](https://studio.hapbeat.com) で初期設定を済ませておく。

---

## 2. どのゲーム内イベントが触覚になるか

| ゲーム内の出来事 | 論理イベント名 | 既定の Kit イベント | 既定ゲイン |
|---|---|---|---|
| 銃を撃つ（弾がある時だけ） | `shot` | `vr-shooter-kit.shot_recoil` | 1.0 |
| 近接（殴り）が当たる | `melee` | `vr-shooter-kit.hit_light` | 0.9 |
| リロード完了 | `reload` | `vr-shooter-kit.reload_click` | 0.8 |
| 自分が被弾する | `player_hit` | `vr-shooter-kit.hit_heavy` | 1.0 |
| アーマーを失う（低体力に入る） | `low_health_start` | `vr-shooter-kit.heartbeat`（ループ再生） | 0.9 |
| 回復する（低体力から復帰） | `low_health_stop` | 上のループを停止 | — |
| 死亡 | `death` | `vr-shooter-kit.hit_heavy` | 1.0 |
| BGM の拍 | `beat` | `vr-shooter-kit.beat_pulse` | 0.5 |

**論理イベント名 → Kit イベント・ゲイン・on/off の対応は設定ファイル側にある。**
mod を作り直さずに、メモ帳で触覚の割り当てや強さを変えられる（[§6](#6-設定ファイル)）。

---

## 3. 導入手順

### 3-1. MelonLoader を入れる

1. <https://melonwiki.xyz/#/?id=automated-installation> から自動インストーラを落とす。
2. 起動し、**Unity Game** に `Pistol Whip.exe` を指定して Install。
   （既定パス例: `C:\Program Files (x86)\Steam\steamapps\common\Pistol Whip\Pistol Whip.exe`）
3. **一度ゲームを普通に起動し、メインメニューまで進んでから終了する。**
   初回だけ MelonLoader がゲームの型情報を C# から使える形に変換するため、
   起動に数分かかることがある。**この工程を飛ばすと次のビルドが必ず失敗する。**
4. ゲームフォルダに以下ができていることを確認する。

```
Pistol Whip\
  MelonLoader\
    net6\               … MelonLoader.dll / 0Harmony.dll / Il2CppInterop.Runtime.dll
    Il2CppAssemblies\   … Assembly-CSharp.dll など (3 の工程で生成される)
  Mods\                 … ここに mod を置く
  UserData\             … 設定ファイルの置き場所
```

### 3-2. mod をビルドする

このフォルダ（`mods/pistolwhip-hapbeat`）で PowerShell を開き、

```powershell
dotnet build src\PistolWhipHapbeat.csproj -c Release
```

ゲームを既定パス以外に入れている場合は、インストール先を渡す:

```powershell
dotnet build src\PistolWhipHapbeat.csproj -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Pistol Whip"
```

`MelonLoader.dll が見つかりません` と出たら、パス指定か 3-1 の工程 3 が抜けている。

成功すると `src\bin\Release\PistolWhipHapbeat.dll` ができる。

### 3-3. mod を置く

`PistolWhipHapbeat.dll` を **ゲームフォルダの `Mods\`** にコピーする。以上。

### 3-4. Hapbeat デバイスに音源を書き込む

1. <https://studio.hapbeat.com> を開き、Hapbeat Helper を起動してデバイスを認識させる。
2. Kit として `kits/vr-shooter-kit` を読み込み、デバイスへ Deploy する。
3. Studio 上で `shot_recoil` などを再生し、デバイスが震えることを確認する。

（Kit の詳細は `kits/vr-shooter-kit/README.md`）

### 3-5. 起動して確認する

ゲームを起動すると MelonLoader のコンソールウィンドウが出る。次のような行が出れば成功:

```
[PistolWhipHapbeat] [Hapbeat] Wrote default settings to ...\UserData\hapbeat_settings.json
[PistolWhipHapbeat] [Hapbeat] UDP client open (appName='PistolWhip'). ...
[PistolWhipHapbeat] [Hapbeat] Hooked Gun.Fire.
[PistolWhipHapbeat] [Hapbeat] Hooked PlayerHUD.OnArmorLost.
[PistolWhipHapbeat] [Hapbeat] Game hooks: 10 applied, 0 unavailable.
[PistolWhipHapbeat] [Hapbeat] Beat sync armed (waiting for a song to load).
```

- Hapbeat 本体の画面に接続元として **`PistolWhip`** が表示される。
- 初回起動時、Windows ファイアウォールが通信許可を聞いてきたら **許可**する
  （プライベートネットワークにチェック）。
- 曲を選んで撃てば触覚が来る。

---

## 4. うまくいかないとき

| 症状 | 見るところ |
|---|---|
| コンソールに `[Hapbeat]` の行が 1 つも出ない | `Mods\` に DLL があるか。MelonLoader 自体が起動しているか |
| `Game hooks: 0 applied, 10 unavailable` | ゲーム側のクラス名が変わっている。→ [実機確認チェックリスト](#実機確認チェックリスト) |
| 一部だけ `Hook unavailable:` と出る | その行のイベントだけ無効。他は動く |
| 触覚が全く来ない / Hapbeat 画面に `PistolWhip` が出ない | PC と Hapbeat が同じネットワークか。ファイアウォールで `Pistol Whip.exe` の UDP がブロックされていないか |
| 接続はしているが無音 | Studio で Kit を Deploy 済みか。デバイス音量が 0 でないか |
| 拍だけ来ない | `Song loaded. Koreography event ids: ...` がコンソールに出ているか（[§7](#7-bgm-拍のチューニング)） |
| 拍が多すぎる / うるさい | [§7](#7-bgm-拍のチューニング) で ID を絞るか `beatEnabled: false` |
| 曲の特定の区間で拍が**鳴りっぱなし**（ブザーのように連続する） | 拍でない区間イベント（歌詞・オーバードライブ等）を拍として拾っている。[実機確認チェックリスト](#実機確認チェックリスト) 4-3 |
| `Beat sync produces no haptic:` の警告が出て拍が全く来ない | 拍と区間の区別が付かないため安全側で止めている。[実機確認チェックリスト](#実機確認チェックリスト) 4-3 |
| `Hook unavailable: ...(ambiguous: N overloads ...)` | 同名メソッドが複数あり、どれを差し込むか決められていない。[実機確認チェックリスト](#実機確認チェックリスト) 1 |
| 低体力の鼓動が鳴りっぱなし | ゲームを正常終了させると停止コマンドが飛ぶ。強制終了した場合は Studio から停止 |

MelonLoader のログは `Pistol Whip\MelonLoader\Latest.log` にも残る。
問題を報告するときはこのファイルを添える。

---

## 5. アンインストール

`Mods\PistolWhipHapbeat.dll` を削除する。設定を消したい場合は
`UserData\hapbeat_settings.json` も削除する。
MelonLoader ごと外す場合は自動インストーラの Un-Install を使う。

---

## 6. 設定ファイル

初回起動時に `Pistol Whip\UserData\hapbeat_settings.json` が自動生成される
（`UserData` が無い環境では `Mods\` 直下）。編集後は**ゲームを再起動**すると反映される。

```json
{
  "appName": "PistolWhip",
  "group": -1,
  "player": -1,
  "masterGain": 1.0,
  "minIntervalMs": 60,
  "events": {
    "shot": { "eventId": "vr-shooter-kit.shot_recoil", "gain": 1.0, "enabled": true },
    "melee": { "eventId": "vr-shooter-kit.hit_light", "gain": 0.9, "enabled": true },
    "reload": { "eventId": "vr-shooter-kit.reload_click", "gain": 0.8, "enabled": true },
    "player_hit": { "eventId": "vr-shooter-kit.hit_heavy", "gain": 1.0, "enabled": true },
    "low_health_start": { "eventId": "vr-shooter-kit.heartbeat", "gain": 0.9, "enabled": true },
    "low_health_stop": { "eventId": "vr-shooter-kit.heartbeat", "gain": 0.9, "enabled": true },
    "death": { "eventId": "vr-shooter-kit.hit_heavy", "gain": 1.0, "enabled": true },
    "beat": { "eventId": "vr-shooter-kit.beat_pulse", "gain": 0.5, "enabled": true }
  },
  "beatEnabled": true,
  "beatEventIds": [],
  "logDiscoveredBeatIds": true
}
```

| キー | 意味 |
|---|---|
| `appName` | Hapbeat 本体の画面に出る接続元名。16 文字まで |
| `player` / `group` | 複数人・複数台で使うときの宛先固定（1〜99）。`-1` で無効 |
| `masterGain` | 全体の強さ倍率。実際の強さは `masterGain × 各 gain` |
| `minIntervalMs` | **同じ**イベントの最小送信間隔 (ms)。連射で埋まるのを防ぐ。`0` で無効 |
| `events` | 論理イベント → Kit イベント / 強さ / 有効 |
| `beatEnabled` | BGM 拍の触覚を出すか |
| `beatEventIds` | 拍として使う Koreography イベント ID（空 = 曲に入っている全部） |
| `logDiscoveredBeatIds` | 曲ロード時に見つけた ID をログに出すか |

**イベントを 1 つ切りたい**なら `"enabled": false`。
**触覚を差し替えたい**なら `eventId` を別の Kit イベントにする
（`vr-shooter-kit` に入っているもの: `shot_recoil` / `hit_light` / `hit_heavy` /
`reload_click` / `heartbeat` / `beat_pulse` / `kill_confirm` / `slash` / `block_thud`）。

> `low_health_start` と `low_health_stop` は**同じ `eventId` にしておくこと**。
> 停止コマンドは「今鳴っているそのクリップ」を指定して止めるため。

JSON を壊すと、そのファイルはそのまま残したうえで既定値で起動し、
コンソールに警告が 1 行出る（設定が消えることはない）。

---

## 7. BGM 拍のチューニング

Pistol Whip の拍情報は Koreographer という仕組みで曲ごとに持たれていて、
**「拍」を表す ID 名は曲ごとにサウンド担当が自由に付けている**。
決め打ちできないので、この mod は曲ロード時に ID を全部拾って全部購読する。

曲を 1 曲プレイすると、コンソールにこう出る:

```
[Hapbeat] Song loaded. Koreography event ids: bass beat, lyrics, overdrive
[Hapbeat] Beat sync listening on 3 event id(s).
```

拍が多すぎる・変なタイミングで来る場合は、**それらしい ID だけに絞る**:

```json
"beatEventIds": ["bass beat"]
```

- 拍が全く来ない → `Song loaded.` の行自体が出ていないなら、Koreographer のフックが
  効いていない（[実機確認チェックリスト](#実機確認チェックリスト)の 4 番）。
- 曲によって ID 名が違うので、複数曲でデモするなら共通して存在する ID を選ぶか、
  `beatEventIds` を空（自動）のままにして `minIntervalMs` で密度を抑える。
- 拍は要らない → `"beatEnabled": false`。

---

## 8. 動作の仕組み（読まなくても導入できる）

```
Pistol Whip (IL2CPP)
  └ MelonLoader + Harmony で Gun.Fire などの直後に処理を差し込む
        └ PistolWhipHapbeat.dll
              └ 論理イベント名 ("shot" 等) を投げるだけ
                    └ HapbeatModCore (共通送信コア)
                          └ UDP でコマンド送信 → Hapbeat デバイス
```

- 送信プロトコルの知識は **すべて共通コア** (`mods/shared/HapbeatModCore`) 側にある。
  この mod のコードには Kit イベント ID もパケット構造も出てこない。
- 2 秒ごとに PING と接続状態を送り、応答したデバイスへ**ユニキャスト**で送る
  （応答が無ければブロードキャストに落ちる）。
- **フック対象は実行時に名前で探す。** 見つからないフックはその 1 つだけ無効になり、
  警告が出て、他のイベントはそのまま動く。ゲーム更新でクラス名が変わっても
  mod 全体が起動不能になることはない。

### ソース構成

| ファイル | 役割 |
|---|---|
| `src/PistolWhipHapbeatMod.cs` | MelonLoader エントリ。設定読込・UDP クライアント・後始末 |
| `src/HookInstaller.cs` | Harmony パッチの適用（1 つずつ・失敗を隔離） |
| `src/GameHooks.cs` | 各フックの本体（ゲーム内イベント → 論理イベント） |
| `src/BeatSync.cs` | Koreographer 連携（拍） |
| `src/GameReflection.cs` | ゲーム側メンバーの安全な読み取り |
| `src/AmmoTracker.cs` | 空撃ち判定用の残弾追跡 |
| `src/PistolWhipSettings.cs` | 共通設定スキーマ + この mod 固有の拍設定 |
| `src/LogicalEvents.cs` | 論理イベント名の定数 |
| `stubs/` | ゲームを持たない環境でのビルド検証用ダミー型（製品ビルドには含まれない） |
| `compile-check/` | 同上の検証プロジェクト |

開発者向けの検証（ゲーム不要）:

```powershell
dotnet build compile-check          # 構文・型の整合チェック (警告 0 で通ること)
dotnet run --project ..\shared\HapbeatModCore.Tests   # 共通コアのプロトコルテスト
```

---

## 実機確認チェックリスト

**この mod は Pistol Whip を持っていない環境で書かれている。**
以下は「所有者が最初の 1 回だけ確認すべきこと」。ここが通れば以降は普通に使える。

### 1. フックが当たるか（最重要）

ゲーム起動時のコンソールで確認する。

```
[Hapbeat] Game hooks: 10 applied, 0 unavailable.
```

`unavailable` が 0 でなければ、その行の前に
`Hook unavailable: Gun.Fire (type 'Gun' not found)` のような警告が出ている。
対象は以下の 10 個:

| クラス | メソッド | 対応イベント |
|---|---|---|
| `Gun` | `Fire` | 発砲 |
| `Gun` | `Reload` | リロード |
| `GunAmmoDisplay` | `Update` | 残弾追跡（空撃ち除外用・触覚なし） |
| `MeleeWeapon` | `ProcessHit` | 近接 |
| `Reloader` | `SetReloadMethod` | リロード方式の記録（触覚なし） |
| `Projectile` | `ShowPlayerHitEffects` | 被弾 |
| `Player` | `ProcessKillerHit` | 死亡 |
| `PlayerHUD` | `OnArmorLost` | 低体力開始 |
| `PlayerHUD` | `playArmorGainedEffect` | 低体力終了 |
| `PlayerHUD` | `OnPlayerDeath` | 死亡 |

**見つからない場合の調べ方**:

1. [ILSpy](https://github.com/icsharpcode/ILSpy/releases) か
   [dnSpyEx](https://github.com/dnSpyEx/dnSpy/releases) で
   `Pistol Whip\MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll` を開く。
2. 上の表のクラス名で検索し、**現行ビルドでの正しいクラス名・メソッド名**を調べる。
3. `src/HookInstaller.cs` の `InstallAll` にある `Patch(harmony, "Gun", "Fire", ...)` の
   文字列を実際の名前に直してビルドし直す。
   名前空間が付いている場合は `"Namespace.Class"` の形で書く。

**`ambiguous: N overloads (...)` と出た場合**:

同名メソッドが複数あり、どれに差し込むか決められないので、そのフックだけ見送っている
（誤ったオーバーロードに黙って差し込むのを避けるため）。警告行に候補のシグネチャが
そのまま並ぶので、意図するものの**引数の個数**を `InstallAll` の `Patch(...)` 末尾に足す:

```csharp
Patch(harmony, "Gun", "Fire", "AfterGunFire", 0);   // 引数 0 個の Fire() を狙う
```

`Gun.Reload` と `Reloader.SetReloadMethod` は既に `1` を指定済み。
逆に `takes N argument(s), expected 1` の警告が出た場合は、ゲーム側の引数が
変わっているので `src/GameHooks.cs` の `__args[0]` の読み方を確認する。

### 2. `hand` / `currentBulletCount` / `hasArmor` / `reloadTriggered` の存在

これらはリフレクションで読んでいるため、無くてもクラッシュせず「そのガードが効かない」だけになる。

- 空撃ちでも `shot` が鳴る → `GunAmmoDisplay.currentBulletCount` か `Gun.hand` の名前違い。
- アーマーが減るたびに鼓動が始まる → `PlayerHUD.hasArmor` の名前違い。
- どちらも 1 の手順で実名を調べ、`src/GameHooks.cs` 内の文字列を直す。

### 3. MelonLoader のバージョン

- 現行の MelonLoader（0.6 系以降、net6 + Il2CppInterop）を想定している。
- 既存の他社製 Pistol Whip 用触覚 mod は 2023 年で更新が止まっており、
  当時「0.6.5 で落ちる」との記載もあった。**この mod は最新版で新規ビルドする前提**。
- 起動しない場合は MelonLoader を 1 つ前のバージョンに落として試す価値がある。

### 4. Koreographer（BGM 拍）が想定どおりか

拍は他社製 mod に前例が無く、この mod で新規に実装した部分なので、ここが一番不確実。

1. **ビルドが `SonicBloom.Koreo` で失敗する場合**
   - `Assembly-CSharp.dll` を ILSpy で開き、`Koreographer` クラスを検索する。
   - 名前空間が `Il2CppSonicBloom.Koreo` になっていたら:
     `dotnet build src\PistolWhipHapbeat.csproj -c Release -p:KoreoIl2CppNamespace=true`
   - `Assembly-CSharp.dll` ではなく別の DLL に入っていたら、
     `src/PistolWhipHapbeat.csproj` の `Assembly-CSharp` の `<Reference>` を
     その DLL 名に変える。
   - どうしても解決しない / 拍は要らない場合:
     `-p:DisableBeatSync=true` で拍機能を丸ごと外してビルドできる（他の触覚は動く）。
2. **ビルドは通るが拍が来ない場合**
   - `Song loaded. Koreography event ids: ...` が出ているか確認する。
     出ていなければ `Koreographer.LoadKoreography` のフックが当たっていない。
   - `Beat sync disabled: could not convert the callback delegate` が出ていれば、
     `RegisterForEventsWithTime` の引数型（デリゲート型）が想定と違う。
     ILSpy で `Koreographer` の登録系メソッドのシグネチャを確認する。
   - ID は拾えているのに無音なら、`beat` の `enabled` / `gain` と Kit の Deploy を確認する。
3. **拍が「単発」か「区間」かの判定（要確認・推定を含む）**
   - Koreographer のイベントには**単発 (one-off)** と**区間 (span)** の 2 種類があり、
     区間イベントは**その区間の間ずっと毎フレーム発火する**。拍として使えるのは単発の方。
   - mod は `IsOneOff()` → 開始/終了サンプル値の順に読んで判定するが、
     **現行ビルドでの実際のメンバー名は未確認**（公開ドキュメント上のイベントモデルから
     推定）。名前が違って判定できない場合は、**鳴らさない側に倒す**。
   - **症状 A: コンソールに `Beat sync produces no haptic:` が出て拍が来ない**
     → 判定不能だったということ。ILSpy で `KoreographyEvent` を開き、
     単発判定に使えるメソッド／サンプル位置のフィールド名を調べ、
     `src/BeatSync.cs` の `ShapeMembers`（または `IsOneOff` の呼び出し名）に足す。
   - **症状 B: 曲の特定区間で拍が鳴りっぱなしになる**
     → 区間イベントを単発と誤判定している（別の同名フィールドを読んでいる可能性）。
     暫定対処は `beatEventIds` にその区間の ID を**含めない**こと。
     `logDiscoveredBeatIds: true` のまま 1 曲流すと ID 一覧が出るので、
     鳴りっぱなしになる箇所の ID（`lyrics` / `overdrive` 等それらしい名前）を外し、
     拍らしい ID だけを列挙する。恒久対処は症状 A と同じくメンバー名の実名確認。

### 5. 触覚の強さ・密度

実機で触ってから `masterGain` と各 `gain`、`minIntervalMs` を詰める。
特に **拍 + 発砲が重なる曲では触覚が飽和しやすい**ので、`beat` の gain は
低め（既定 0.5）から上げていく。

---

## 制作メモ

- **他者の mod のコードは一切流用していない。** 参考にしたのは公開されている
  mod が「どのクラスのどのメソッドをフックしているか」という事実のみで、
  実装はすべて新規に書いている（参照した repo にライセンス表記が無いため）。
- ゲームの逆コンパイル結果は本リポジトリに含めていない。
- 拍（Koreographer 連携）は前例が無く、公開されている Koreographer の
  ドキュメント上の API に基づく新規実装。
