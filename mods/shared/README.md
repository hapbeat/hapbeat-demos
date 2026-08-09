# HapbeatModCore — VR mod 共通送信コア

既存 VR ゲームに Hapbeat 触覚を後付けする mod が使う、最小 UDP 送信コアの **C# 正実装**。

**共有のしかたは mod によって違う。**

| mod | 実行環境 | このコアとの関係 |
|---|---|---|
| Pistol Whip | MelonLoader (net6, C#) | **この C# ソースをそのままコンパイルして取り込む** |
| Blade &amp; Sorcery | ThunderRoad (net48, C#) | **同上** |
| Robo Recall | UE4 Mod Kit (C++ プラグイン) | C# を読めないので **独立した C++ 実装**（`HapbeatWire.h` / `.cpp`）を持ち、<br>この C# 正実装とバイト単位で一致することを**パリティテストで検証**する |

つまり「3 mod が 1 つのコードを共有している」のではない。C# の 2 mod がソースを共有し、
Robo Recall は同じワイヤ形式を C++ に移植したうえで、出力バイト列を突き合わせて等価性を担保する
（`mods/roborecall-hapbeat/Tools/` の自己テストと C# 側ダンプの diff）。
**共通なのは設定スキーマとワイヤプロトコルであって、コードそのものではない。**

## 役割

mod 側のフックコードは **論理イベント名** を投げるだけでよい:

```csharp
_client.Fire("shot");   // 発砲
_client.Fire("hit");    // 被弾
```

event id・ゲイン・on/off は設定 JSON 側にあり、mod をリビルドせずにチューニングできる。
プロトコル知識（パケット構造 / アドレッシング / unicast ルーティング）は
すべてこのコアに閉じている。

| ファイル | 内容 |
|---|---|
| `HapbeatModCore/HapbeatProtocol.cs` | パケット組み立て・解析。hapbeat-unity-sdk からの移植（ワイヤ仕様は同一） |
| `HapbeatModCore/HapbeatModClient.cs` | 送信 + 受信スレッド + keep-alive + 既知デバイステーブル + レート制限 |
| `HapbeatModCore/HapbeatModSettings.cs` | 設定 JSON のロード / 保存（外部依存なしの最小パーサ同梱） |
| `HapbeatModCore.Tests/` | net9.0 コンソールテスト（ローカル検証用） |

## 論理イベント名は mod ごとの語彙

`Fire()` に渡すキーは **各 mod が自分のフック事情に合わせて決める語彙**であり、
mod 間で統一されていない。統一されているのは設定スキーマ（`eventId` / `gain` / `enabled`）と
ワイヤプロトコルの方で、キー名を揃える必要は無い。

同じ「低体力ループ」でも、フックできるものが違うのでキーの切り方が違う:

| mod | キー | 切り方の理由 |
|---|---|---|
| Pistol Whip | `low_health_start` / `low_health_stop` | 開始と停止で別々のフック点があるので 2 キー。<br>どちらも同じ event id (`vr-shooter-kit.heartbeat`) に束ねてある |
| Blade &amp; Sorcery | `low_health_start` | 体力を毎回ポーリングして自前で開始/終了を判定するので 1 キー。<br>停止はこのキーの `FireStop` を流用する |
| Robo Recall | `heartbeat` | Blueprint から手で配線する都合上、ノードに書く名前は短い方がよい 1 キー |

新しい mod を足すときは、無理に既存名へ寄せるより **そのゲームで説明のつく名前**を選ぶ。
名前は設定 JSON のキーとしてユーザーの目にも触れる。

## 各 mod からの取り込み方（C# の 2 mod）

**DLL を配布せずソースファイルを共有する。** mod と一緒に余計な依存 DLL を配らないため。
各 mod の csproj に以下を追加する:

```xml
<ItemGroup>
  <Compile Include="..\shared\HapbeatModCore\*.cs" LinkBase="HapbeatModCore" />
</ItemGroup>
```

コードは netstandard2.0 互換の C# 7.3 までに抑えてある
（MelonLoader の net6 でも、Blade &amp; Sorcery の net472 Unity mono でも通る）。
`System.Text.Json` / Newtonsoft は使っていない。

Robo Recall（C++）はこの経路を使えないので、`HapbeatProtocol.cs` を C++ に移植した
`HapbeatWire.h` / `.cpp` を持つ。**このファイルを変更したら、C++ 側も追従させてパリティテストを
通し直すこと**（手順は `mods/roborecall-hapbeat/README.md` のローカル検証節）。

## 使い方

```csharp
using Hapbeat.ModCore;

// mod の DLL と同じ場所に hapbeat_settings.json。無ければ既定値で自動生成される
string path = Path.Combine(modDirectory, "hapbeat_settings.json");
var settings = HapbeatModSettings.LoadOrCreate(path, "PistolWhip", msg => Log(msg));

var client = new HapbeatModClient(settings);
client.Log = msg => Log(msg);   // 省略可
client.OpenBroadcast();          // port 7700。受信 + keep-alive スレッドが起動する

client.Fire("shot");                  // 任意スレッドから呼べる
client.FireStop("low_health_start");  // loop クリップの停止（レート制限なし）

client.Close();                  // CONNECT_STATUS(connected=false) を送ってから閉じる
```

- `Fire()` は **戻り値 bool**。`false` = 未定義 / 無効 / レート制限で抑止、の意味。
- `AliveDeviceCount` で「今 PONG を返しているデバイス台数」を取れる（HUD / ログ用）。

## 設定ファイル (`hapbeat_settings.json`)

以下は形を示すための抜粋。`events` の**キーは mod ごとに違う**ので、実際の一覧は
mod が初回起動時に書き出したファイルを見ること（各 mod の README にも載せてある）。

```json
{
  "appName": "MyMod",
  "group": -1,
  "player": -1,
  "masterGain": 1.0,
  "minIntervalMs": 60,
  "events": {
    "shot":      { "eventId": "vr-shooter-kit.shot_recoil", "gain": 1.0, "enabled": true },
    "hit":       { "eventId": "vr-shooter-kit.hit_heavy",   "gain": 1.0, "enabled": true },
    "reload":    { "eventId": "vr-shooter-kit.reload_click","gain": 0.8, "enabled": true },
    "heartbeat": { "eventId": "vr-shooter-kit.heartbeat",   "gain": 0.9, "enabled": true },
    "beat":      { "eventId": "vr-shooter-kit.beat_pulse",  "gain": 0.5, "enabled": true }
  }
}
```

- `appName` — デバイス OLED に出る接続元アプリ名。ワイヤ上 16 文字で切られる（DEC-029）
- `player` / `group` — 1〜99 で全送信の target を上書き、`-1` で無効
  （unity-sdk の Address Override と同じ意味論）
- `masterGain` — 全イベント共通の倍率。実際のゲインは `masterGain × events[].gain`
- `minIntervalMs` — **同一論理イベント**の最小送信間隔。連射フックのパケットストーム抑止。`0` で無効
- `events` を JSON に書いた場合、既定のマップを**丸ごと置き換える**（バインドを削除できる）
- 壊れた JSON はファイルを残したまま既定値で起動し、警告を 1 行出す

## 送信の挙動（ここは仕様であって実装詳細ではない）

- **unicast 優先**: PONG で学習済みのデバイスへ unicast。0 台なら broadcast にフォールバック。
  target 不一致のデバイスは除外、アドレス未報告のデバイスは fail-open で送る。
  全台不一致でも broadcast へ落とす（stale アドレスで STOP が消えるとループが止まらないため）
- **keep-alive は PING を必ず送る**（2 秒周期 + CONNECT_STATUS）。PONG は PING にしか返らないので、
  送らないと宛先表が TTL で空になり永久に broadcast へ戻る
- Windows の `SIO_UDP_CONNRESET` 抑止あり（電源 OFF のデバイスへの unicast が
  ICMP 経由で受信スレッドを殺すのを防ぐ）

## テスト実行

```
dotnet run --project mods/shared/HapbeatModCore.Tests
```

全 PASS で exit code 0、1 件でも失敗すれば非 0。検証内容:

- ヘッダ / PLAY / STOP / STOP_ALL / PING / CONNECT_STATUS のバイトレイアウトを
  contracts `specs/message-format.md` の期待値とオフセット単位で突き合わせ
- PONG 解析（拡張フィールドあり / レガシー 16 byte）
- `AddressMatches` / `ResolveTarget` / `NormalizeOverride`（unity-sdk のテストから移植）
- 設定 JSON の round-trip / 既定値生成 / 壊れ JSON 耐性
- `Fire()` のレート制限

## 参照

- `hapbeat-contracts/specs/message-format.md` — ワイヤフォーマット（正）
- `hapbeat-contracts/specs/device-addressing.md` — target / address のマッチ規則
- 移植元: `hapbeat-unity-sdk/Runtime/HapbeatProtocol.cs` / `HapbeatClient.cs`
