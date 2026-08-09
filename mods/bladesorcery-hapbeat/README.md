# HapbeatBS — Blade & Sorcery 用 Hapbeat 触覚 mod (PCVR 1.0)

Blade & Sorcery (PCVR / Steam 1.0) の戦闘イベントを Hapbeat デバイスへ Wi-Fi UDP で送る
scripted mod。公式 SDK の `ThunderScript` として動く単一 DLL で、追加の依存 DLL は配らない。

送るもの:

| 場面 | 論理イベント名 | 既定のクリップ | 既定 gain |
|---|---|---|---|
| 敵にヒット (プレイヤーの攻撃) | `enemy_hit` | `vr-shooter-kit.slash` | 0.9 |
| 敵を撃破 | `enemy_kill` | `vr-shooter-kit.kill_confirm` | 0.9 |
| パリィ | `parry` | `vr-shooter-kit.block_thud` | 1.0 |
| 弾き返し (deflect) | `deflect` | `vr-shooter-kit.block_thud` | 0.8 |
| プレイヤー被弾 / 死亡 | `player_hit` | `vr-shooter-kit.hit_heavy` | 1.0 |
| 低体力 (残 20% 以下でループ開始) | `low_health_start` | `vr-shooter-kit.heartbeat` | 0.9 |
| 囲まれた (半径 3 m に 3 体以上) | `surrounded` | `vr-shooter-kit.beat_pulse` | 0.6 |

クリップ・強度・on/off は mod の隣に置かれる `hapbeat_settings.json` で決まる。
**再ビルドせずにチューニングできる**（→ [設定ファイル](#設定ファイル-hapbeat_settingsjson)）。

> **Nomad (Quest) は対象外。** Nomad で scripted mod を動かすには本体を "Scripting Beta"
> ブランチに切り替える必要があり、PCVR 用にビルドした DLL がそのまま動く保証がない。
> 本 mod は PCVR (Windows / Steam) 版のみを対象とする。

---

## 0. 必要なもの

- **Blade & Sorcery PCVR 1.0** (Steam) がインストール済みであること
- **.NET SDK 8 以上**（`dotnet --version` で確認。Visual Studio は不要）
- Hapbeat デバイス 1 台以上 + **hapbeat-helper** または **Hapbeat Studio**（kit の書き込み用）
- PC とデバイスが**同じ LAN / 同じ Wi-Fi** にいること（UDP ブロードキャストが通る必要がある）
- （任意・推奨）**ILSpy**（→ [ILSpy による裏取り](#5-ilspy-による裏取り必須-1-回)）

このディレクトリの中身:

```
bladesorcery-hapbeat/
  manifest.json        ゲームが読む mod メタデータ（DLL と同じ階層に置く）
  src/
    HapbeatBS.csproj   配布用 DLL のビルド定義（ゲームの DLL を参照する）
    HapbeatBSMod.cs    ThunderScript 本体（フック → 論理イベント送出）
    ThunderRoadApi.cs  ThunderRoad API へのアダプタ（未確定シグネチャはここに隔離）
  stubs/               ThunderRoad / UnityEngine の最小スタブ（compile-check 専用・非配布）
  compile-check/       ゲーム無しでコンパイルを通すための検証プロジェクト
```

---

## 1. ゲームのインストール先を指定する

ビルドには Blade & Sorcery 本体の DLL が要る（`BladeAndSorcery_Data\Managed\` にある）。
これらは**コピーせず参照するだけ**で、ビルド成果物にも git にも入らない。

インストール先の例:

```
C:\Program Files (x86)\Steam\steamapps\common\Blade & Sorcery
D:\SteamLibrary\steamapps\common\Blade & Sorcery
```

Steam の「ライブラリ → Blade & Sorcery を右クリック → 管理 → ローカルファイルを閲覧」で確認できる。
`BladeAndSorcery_Data\Managed\ThunderRoad.dll` があるフォルダの**1 つ上**が指定するパス。

指定方法は 2 つ。どちらでもよい。

- **毎回コマンドラインで渡す**（推奨・ファイルを汚さない）
- `src/HapbeatBS.csproj` の `<GameDir Condition="'$(GameDir)' == ''">…</GameDir>` の既定値を書き換える

パスが違うと、型エラーの山ではなく次の 1 行で止まるようにしてある:

```
error : ThunderRoad.dll not found under "…". Set GameDir to your Blade & Sorcery install folder, …
```

---

## 2. ビルド

PowerShell でこのリポジトリのルート（`hapbeat-demos/`）から:

```powershell
dotnet build mods/bladesorcery-hapbeat/src -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Blade & Sorcery"
```

成果物: `mods/bladesorcery-hapbeat/src/bin/Release/HapbeatBS.dll`
（`manifest.json` も同じ場所にコピーされる）

### ゲームが無い環境での検証

ゲーム本体が無くてもコードの整合はチェックできる。スタブと突き合わせてビルドする:

```powershell
dotnet build mods/bladesorcery-hapbeat/compile-check
```

これが通れば「mod のコードが C# として正しく、共通コアとリンクでき、`stubs/` に書いた
シグネチャと一致している」ことまでは言える。**実際の `ThunderRoad.dll` がそのシグネチャで
あるかは別問題**で、それは次章の ILSpy 手順で確認する。

### コンパイルが通らないときの逃げ道

`onCreatureAttackParry` / `onDeflect` / `[ModOption]` はシグネチャが公開されておらず、
ゲーム側の実装と食い違う可能性が残る。それぞれ機能フラグで切り離してあるので、
該当箇所でエラーが出たら**一旦外してビルドを通し**、後から直せる:

```powershell
# パリィと deflect を外す（ゲーム内設定メニューは残す）
dotnet build mods/bladesorcery-hapbeat/src -c Release -p:GameDir="…" -p:HapbeatBsFeatures=HAPBEAT_BS_MODOPTIONS

# 3 つ全部外す（最小構成: 敵ヒット / 撃破 / 被弾 / 低体力 / 囲まれ だけ）
dotnet build mods/bladesorcery-hapbeat/src -c Release -p:GameDir="…" -p:HapbeatBsFeatures=none
```

（`none` は「どのフラグも立てない」という意味の合図。空文字を渡すと既定値に戻ってしまうので使わない）

| フラグ | 外すと消える機能 |
|---|---|
| `HAPBEAT_BS_MODOPTIONS` | ゲーム内 Mod 設定メニューの Hapbeat 項目（JSON 設定は残る） |
| `HAPBEAT_BS_PARRY` | `parry` イベント |
| `HAPBEAT_BS_DEFLECT` | `deflect` イベント |

---

## 3. ゲームへの導入

mod フォルダを作り、DLL と `manifest.json` を**フラットに**並べる。
サブフォルダに入れ子にすると mod が認識されない。

```
<Blade & Sorcery>\BladeAndSorcery_Data\StreamingAssets\Mods\HapbeatBS\
    HapbeatBS.dll
    manifest.json
    hapbeat_settings.json   ← 初回起動時に自動生成される
```

PowerShell での例:

```powershell
$game = "D:\SteamLibrary\steamapps\common\Blade & Sorcery"
$dest = "$game\BladeAndSorcery_Data\StreamingAssets\Mods\HapbeatBS"
New-Item -ItemType Directory -Force $dest
Copy-Item mods/bladesorcery-hapbeat/src/bin/Release/HapbeatBS.dll  $dest -Force
Copy-Item mods/bladesorcery-hapbeat/manifest.json                  $dest -Force
```

ゲームを起動し、**メインメニューの Mods 一覧に `HapbeatBS` が出ていること**を確認する。
出ていなければ [トラブルシュート](#8-トラブルシュート)へ。

---

## 4. Hapbeat 側の準備

1. **kit を書き込む** — `kits/vr-shooter-kit/` を Hapbeat Studio で開き、対象デバイスへ deploy する。
   この mod が送る event id はすべて `vr-shooter-kit.*`（上の表）で、kit が入っていないと
   デバイスは受信しても鳴らない。
2. **同じ LAN に置く** — PC とデバイスを同じ Wi-Fi / サブネットに接続する。
   mod は 2 秒周期で PING + CONNECT_STATUS をブロードキャストし、応答した(PONG した)デバイスへ
   以降 unicast する。
3. **接続確認** — ゲーム内で mod がロードされると、デバイスの OLED に接続元アプリ名として
   **`BladeSorcery`** が出る。ここまで出れば通信経路は成立している。
4. **Studio / helper と併用してよい** — mod は OS が割り当てるポートで受信するので、
   Studio や helper が同時に動いていても衝突しない。

---

## 5. ILSpy による裏取り（必須・1 回）

Warpfrog は `ThunderRoad.dll` の C# API リファレンスを公開していない。本 mod が使っている
イベントの**存在**は稼働中の他 mod のソースで確認済みだが、**引数の型・個数・順序は推定**である。
推定箇所はすべて `src/ThunderRoadApi.cs` の 1 ファイルに閉じ込め、コメント `VERIFY-ILSPY` で
印を付けてある。

```powershell
# 確認すべき箇所を列挙する
Select-String -Path mods/bladesorcery-hapbeat/src/*.cs -Pattern "VERIFY-ILSPY"
```

手順:

1. [ILSpy](https://github.com/icsharpcode/ILSpy) を入れて
   `<Blade & Sorcery>\BladeAndSorcery_Data\Managed\ThunderRoad.dll` を開く
2. 下表の 7 項目を実物と突き合わせる
3. 食い違っていたら **`src/ThunderRoadApi.cs` と `stubs/ThunderRoadStubs.cs` の両方**を直す
   （スタブを直さないと compile-check が実物と違うものを検証し続ける）

| # | 確認する対象 | 本 mod の推定 |
|---|---|---|
| 1 | `EventManager.onCreatureHit` のデリゲート | `(Creature, CollisionInstance, EventTime)` |
| 2 | `EventManager.onCreatureKill` のデリゲート | `(Creature, Player, CollisionInstance, EventTime)` |
| 3 | `EventManager.onCreatureAttackParry` / `onDeflect` のデリゲート | `(Creature, Creature, CollisionInstance, EventTime)` / `(Creature, Item, Creature, EventTime)` |
| 4 | `Creature.OnDamageEvent` / `OnKillEvent` のデリゲート | `(CollisionInstance, EventTime)` |
| 5 | 攻撃元がプレイヤーかの判定 | `CollisionInstance.IsDoneByPlayer()` |
| 6 | 死亡フラグ / `EventTime` のメンバ | `Creature.isKilled` / `EventTime.OnStart`, `OnEnd` |
| 7 | `[ModOption]` 系属性の**存在と引数** | `ModOption` / `ModOptionCategory(string, int)` / `ModOptionOrder(int)` / `ModOptionTooltip(string)` / `ModOptionSlider`（いずれも BasSDK ドキュメントに名前のみ記載。引数の形は未確認） |

補足:

- `EventManager.OnPlayerPrefabSpawned` と `Creature.allActive`、`Player.local.creature`、
  `currentHealth` / `maxHealth` は稼働中 mod のソースで使用が確認済み（確度高）。
- 5 が違っていた場合、`ThunderRoadApi.IsCausedByPlayer` は**不明なら `false` を返す**設計にしてある。
  誤って全 NPC 同士の斬り合いで発火するより、自分の攻撃を取りこぼす方がデモとして安全なため。
- 2（kill の `Player` 引数）は「プレイヤー起因の撃破のみ non-null」という前提で
  `ThunderRoadApi.IsKillByPlayer` が使っている。もし常に `Player.local` が入る実装だった場合、
  環境死や NPC 同士の撃破でも `enemy_kill` が鳴るので、その場合はこの分岐を捨てて
  `IsCausedByPlayer` 一本にする（同メソッド内にコメント済み）。
- 3（parry / deflect）は **source / target のどちらがプレイヤー側か**まで確定できていないため、
  「プレイヤーがどちらかに含まれるか」の対称判定にしてある。ILSpy で役割が確定したら
  プレイヤー自身のパリィだけに絞れる。
- **Master gain スライダに範囲指定は付けていない。** 範囲を持つのは `ModOptionUI` だが引数が
  未公開のため、推測で書いてビルドを壊すより実装側で 0〜2 にクランプする方を選んでいる
  （`HapbeatBSMod.MasterGainMax`）。7 が確認できたら属性側に移してよい。
- 敵味方（faction）の判定は行っていない。`surrounded` は「近くにいる非プレイヤーの生存個体」を数える。
  アリーナ系のシーンでは実質すべて敵なので問題にならない。

---

## 6. 動作確認（ゲーム内）

囲まれる状況が確実に作れるのは **Arena** と **Crystal Hunt の Wave Assault**。
どちらもゲーム内蔵で、カスタムレベルを作る必要はない。

1. **Arena**（最短）
   - メインメニューから Arena を選び、任意の武器で開始
   - 敵を斬る → `enemy_hit`、倒す → `enemy_kill`
   - 敵の攻撃を武器で受ける → `parry`
   - 被弾する → `player_hit`
   - 体力が 2 割を切る → `low_health_start` のループ開始。回復して 3 割を超えると停止
   - 敵が 3 体以上、半径 3 m に寄る → `surrounded`（5 秒に 1 回まで）
2. **Crystal Hunt → Wave Assault**
   - 波状に敵が湧くアリーナ部屋。全方位から来るので `surrounded` の確認に向く

ログはゲームの Player.log（`%USERPROFILE%\AppData\LocalLow\WarpFrog\BladeAndSorcery\Player.log`）に
`[HapbeatBS]` 付きで出る。起動時に設定ファイルのパスが 1 行出れば mod は動いている。

### ゲーム内設定メニュー

Mod 設定の **Hapbeat** カテゴリに 3 項目が出る（`HAPBEAT_BS_MODOPTIONS` 有効時）:

| 項目 | 意味 |
|---|---|
| Enabled | 触覚出力の全体 on/off |
| Master gain | 全イベント共通の強度倍率（0〜2）。イベント個別の gain に掛かる |
| Surrounded | 「囲まれた」パルスの on/off |

VR 内で文字入力をさせたくないので、細かいイベント表は JSON 側にしてある。

---

## 7. 設定ファイル (`hapbeat_settings.json`)

初回起動時に mod フォルダへ自動生成される。編集後は**ゲームの再起動**で反映される。

```json
{
  "appName": "BladeSorcery",
  "group": -1,
  "player": -1,
  "masterGain": 1.0,
  "minIntervalMs": 60,
  "events": {
    "enemy_hit":        { "eventId": "vr-shooter-kit.slash",        "gain": 0.9, "enabled": true },
    "enemy_kill":       { "eventId": "vr-shooter-kit.kill_confirm", "gain": 0.9, "enabled": true },
    "parry":            { "eventId": "vr-shooter-kit.block_thud",   "gain": 1.0, "enabled": true },
    "deflect":          { "eventId": "vr-shooter-kit.block_thud",   "gain": 0.8, "enabled": true },
    "player_hit":       { "eventId": "vr-shooter-kit.hit_heavy",    "gain": 1.0, "enabled": true },
    "low_health_start": { "eventId": "vr-shooter-kit.heartbeat",    "gain": 0.9, "enabled": true },
    "surrounded":       { "eventId": "vr-shooter-kit.beat_pulse",   "gain": 0.6, "enabled": true }
  }
}
```

| キー | 意味 |
|---|---|
| `appName` | デバイス OLED に出る接続元名。ワイヤ上 16 文字で切られる |
| `player` / `group` | 1〜99 で全送信の宛先を上書き、`-1` で無効。複数台を別プレイヤーに割り当てるときに使う |
| `masterGain` | 全体倍率。ゲーム内メニューの Master gain がこの値を上書きする |
| `minIntervalMs` | **同じ**論理イベントの最小送信間隔 (ms)。乱戦での連打を抑える。`0` で無効 |
| `events` | 論理イベント名 → クリップの割り当て。**書いた内容で既定を丸ごと置き換える** |

- `enabled: false` にすればそのイベントだけ黙る。キーごと消しても同じ（起動時にログへ 1 行出る）
- JSON が壊れているときは**この mod 用の既定値**（上表の 7 イベント）で起動し、ファイルはそのまま
  残す（打ち間違いでチューニングを失わない）。ログに `Failed to load settings from …` が 1 行出る
- `masterGain × events[].gain` が実際の強度になる。デバイス側では kit の intensity がさらに掛かる

### 強度の詰め方（デモ向け）

1. まず `masterGain` を 1.0 のまま、Arena で一通り触る
2. 「刺さりすぎる / 弱すぎる」イベントだけ `gain` を ±0.2 単位で動かす
3. 全体が強い・弱いだけならゲーム内の Master gain スライダで済ませる（再起動不要）
4. 乱戦でうるさく感じたら `minIntervalMs` を 60 → 100〜150 に上げる

---

## 8. トラブルシュート

| 症状 | 見るところ |
|---|---|
| Mods 一覧に出ない | `StreamingAssets\Mods\HapbeatBS\` の**直下**に `HapbeatBS.dll` と `manifest.json` があるか。フォルダが二重になっていないか |
| 一覧に出るが何も起きない | Player.log に `[HapbeatBS] Loaded.` があるか。無ければ `Startup failed:` の行を読む |
| デバイスの OLED に `BladeSorcery` が出ない | PC とデバイスが同じ LAN か。Windows ファイアウォールがゲームの UDP 送信を遮っていないか。ルータのクライアント分離 (AP isolation) が有効になっていないか |
| OLED には出るが鳴らない | kit `vr-shooter-kit` がデバイスに入っているか（Studio で確認）。イベントが `enabled: false` になっていないか |
| 敵を斬っても鳴らない / 逆に鳴りっぱなし | ILSpy チェックの #1・#5。プレイヤー起因判定が実物と違う可能性が高い |
| 低体力のループが止まらない | ゲームを終了すれば mod が停止コマンドを送る。それでも残るならデバイスの電源を入れ直す |
| ビルドが `ThunderRoad.dll not found` | `-p:GameDir=…` のパス。末尾に `\BladeAndSorcery_Data` まで付けない |
| ビルドが型エラーで落ちる | [コンパイルが通らないときの逃げ道](#コンパイルが通らないときの逃げ道)でフラグを外し、ILSpy で直す |

---

## 9. 実機確認チェックリスト

**未検証のまま残っている項目**。ゲーム所有環境で 1 度ずつ潰すこと。
上 8 件が通れば ILSpy の推定が実物と合っていたことの証明になる。

- [ ] `dotnet build src -c Release -p:GameDir=…` が実物の `ThunderRoad.dll` に対して通る
      （通らなければ ILSpy チェックへ）
- [ ] `[ModOption]` 系属性が実物に存在し、書いた引数の形で通る（ILSpy チェック #7）。
      通らなければ `-p:HapbeatBsFeatures=…` から `HAPBEAT_BS_MODOPTIONS` を外して回避できる
- [ ] 敵を斬ったときだけ `enemy_hit` が鳴る（NPC 同士の斬り合いでは鳴らない）
- [ ] 敵撃破で `enemy_kill` が 1 回だけ鳴る（`EventTime.OnStart` / `OnEnd` の二重発火が無い）
- [ ] **自分が倒していない敵の死**（落下death・炎上・NPC 同士）では `enemy_kill` が鳴らない
- [ ] 武器で受けたときに `parry`、弾いたときに `deflect` が鳴る
- [ ] **自分が関与していない** NPC 同士のパリィ / deflect では鳴らない（囲まれ状況で確認）
- [ ] `hapbeat_settings.json` をわざと壊すと、この mod 用の既定イベント（`enemy_hit` 等）で
      起動しログに 1 行出る（`shot` / `reload` 等の別 mod 用の既定に落ちない）
- [ ] 自分が斬られたときに `player_hit` が鳴る
- [ ] 体力 20% 以下で heartbeat がループし、回復（30% 超）で止まる
- [ ] 敵 3 体以上に囲まれると `surrounded` が鳴り、5 秒に 1 回を超えない
- [ ] レベル遷移・プレイヤー死亡→リスポーン後もイベントが鳴り続ける（購読が生き残っている）
- [ ] ゲーム終了時に heartbeat のループがデバイス側で止まる
- [ ] `[ModOption]` の 3 項目がゲーム内 Mod 設定に表示され、値が保存される
- [ ] Master gain スライダが**その場で**強度に効く（再起動不要）
- [ ] `hapbeat_settings.json` が mod フォルダに生成される
      （生成先が別の場所になる場合は `GetModDirectory()` のフォールバックが働いている）
- [ ] デバイス 2 台以上でも取りこぼしなく鳴る（unicast ルーティングの確認）
- [ ] 途中でデバイスの電源を切っても、ゲーム側が固まったり無音になったりしない

---

## ライセンス・配布上の注意

- 本 mod は BasSDK / ThunderRoad のコードを一切含まない。ゲームの DLL は**参照するだけ**で、
  ビルド成果物にも本リポジトリにも入らない。
- Warpfrog の SDK ライセンスは、改変版ゲーム / mod の配布を**無償の場合に限り**許可している。
  この mod を再配布する場合は無償で行うこと。
- 参考にした他社製 mod（bhaptics / OWO 等）のコードは複製していない。フック対象の名前という
  事実のみを参照し、実装はすべて新規に書いている。

## 関連

- 共通送信コアの仕様: `mods/shared/README.md`
- kit の中身: `kits/vr-shooter-kit/README.md`
- ワイヤフォーマット: `hapbeat-contracts/specs/message-format.md`
