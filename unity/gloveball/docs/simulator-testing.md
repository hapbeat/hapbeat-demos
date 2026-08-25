# XR Device Simulator でのエディタ内検証

Quest 実機なしで、エディタの Play モードだけで Glove Ball Solo Demo を一通り触るための手順。
シミュレータは XR Interaction Toolkit 3.0.8 同梱の **XR Device Simulator** サンプル。

実際の Quest と Touch コントローラで確認する場合は、後述の
「Meta Horizon Link（Air Link）での Play Mode」を使う。

## Meta Horizon Link（Air Link）での Play Mode

### 初回設定

1. Unity のメニュー **GloveBall Demo > Configure Air Link Play Mode** を実行する
2. Meta Horizon Link 側で Meta を active OpenXR runtime にする
3. Quest から Air Link を接続し、PC VR のホーム画面まで入る
4. `Assets/GloveBallDemo/Scenes/Demo.unity` を開いて Play

設定コマンドは active build target を Windows Standalone に切り替え、Standalone 用の
OpenXR loader と Oculus Touch / Meta Quest Touch Plus / Touch Pro controller profile を有効にする。
Android 用 OpenXR 設定は削除・変更しない。APK の `BuildApk` は必要時に Android へ戻す。

シーン内の `XR Device Simulator (EditorOnly)` は最初は停止している。OpenXR初期化後に実HMD入力を
検出した場合は停止を維持し、検出しなければキーマウス用Simulatorを起動する。これにより、偽のHMDが
実機より先に汎用入力bindingを取ることを防ぐ。OpenXRはPlay開始時に初期化されるため、
**Air Link接続後にPlayを押す**。

Touch コントローラでは、手のひら前方の CatchVolume に **Incoming ball** が入っている間にグリップを押すと保持する。
保持時は球がグローブ内の `GripAnchor`（手のひら前方）へ snap し、人差し指のトリガーを押して最大 **1.5 秒**チャージして離すと投げる。
側面グリップを離すと、保持球は手の速度で物理的にリリースされる。
ランチャーの狙いはプレイヤーの水平前後・左右を基準に毎回ランダムにずれる。
左スティックはHMDの水平前方・右方向を基準に連続移動し、右スティックの左右は中央へ戻すまで一回だけ
45度のスナップターンを行う。

左 Menu または左右の Secondary（B/Y）でヘッドセット内メニューを開閉する。メニュー中はゲーム操作が中立化され、
保持球はpoolへ戻る。左右どちらかのスティック上下で項目を選び、Primary（A/X）で決定する。閉じた後は
押しっぱなしだったグリップ/トリガーを再利用せず、いったん離して次の押下からゲーム操作へ戻る。

### Air Link Play Modeで確認できないもの

- 描画はPCで行われるため、Quest単体APKのフレームレート・発熱・GPU負荷は確認できない
- HapbeatへのUDPはPCから送信されるため、Quest本体のWi-Fi送信経路は確認できない
- Android manifest、権限、サイドロード起動は確認できない

操作・視点・キャッチ/投擲・ゲーム進行をAir Linkで先に調整し、最後にAPKで上記3項目を確認する。
Editorの通常PlayはHapbeatへ実送信する。視覚・操作だけを見る場合はHapbeatをLANから外しておく。

## 準備

シミュレータのサンプルは Demo シーンにすでに注入済み（`XR Device Simulator (EditorOnly)`）。
サンプルを入れ直した / シーンを再生成した場合のみ、以下を実行する。

```powershell
# 1) サンプルを Assets/Samples/ へ import
.\tools\run-unity.ps1 -Method GloveBallDemo.Editor.BatchOps.ImportXrDeviceSimulator

# 2) シーンを再生成（シミュレータの注入はここで行われる）
.\tools\run-unity.ps1 -Method GloveBallDemo.Editor.BatchOps.BuildDemo
```

シミュレータのインスタンスは tag `EditorOnly`。ビルド時に丸ごと剥がれるので APK には入らない。

## 実行

1. `Assets/GloveBallDemo/Scenes/Demo.unity` を開く
2. Game ビューをクリックしてフォーカスを与える（キーボード入力がシミュレータに届く条件）
3. Play

Play すると視点が立ち姿勢の高さ（床から 1.6 m）に上がり、シミュレータの操作パネルが
Game ビューに重なって表示される。

グローブは初期状態では頭の位置に重なっている。`左Shift`（左手）/ `Space`（右手）を押しっぱなしにして
`W`（前）や `Q`（下）で手前に引き出すと見える位置に来る。

## キー操作（XRI サンプルの既定バインド）

### 何を動かすか（デバイスの選択）

| 操作 | 割当 |
|---|---|
| 頭（HMD）を動かす | 右マウスボタン 押しっぱなし |
| 左手コントローラを動かす | `左Shift` 押しっぱなし（一時） / `T` （トグル） |
| 右手コントローラを動かす | `Space` 押しっぱなし（一時） / `Y` （トグル） |
| 頭＋両手をまとめて動かす（体ごと） | `U` （トグル） |
| デバイスを順に切り替え | `Tab`（左手 → 右手 → 頭） |
| 操作をすべて解除 | `Esc` |

何も押していない状態では「体」を動かす（＝プレイヤーごと移動）。

### 動かし方

| 操作 | 割当 |
|---|---|
| 前後左右に移動 | `W` `A` `S` `D` |
| 上下に移動 | `E` （上） / `Q` （下） |
| 回転 | マウス移動（回転モード時） |
| 移動 ⇄ 回転 モード切替 | `R` （トグル） |
| 一時的に回転モードにする | `左Ctrl` または マウス中ボタン 押しっぱなし |
| 前後（Z 方向）に移動 / ロール | マウスホイール |
| 軸拘束 | `X` (x) / `C` (y) / `Z` (z) 押しっぱなし |
| 位置・回転をリセット | `V` |
| マウスカーソルのロック切替 | `\` |

### コントローラのボタン

操作中のコントローラ（`左Shift` / `Space` / `T` / `Y` で選んだ手）に対して効く。

| 操作 | 割当 |
|---|---|
| **グリップ（＝物理保持）** | `G` 押しっぱなし |
| **トリガー（＝投擲チャージ）** | 左マウスボタン 押しっぱなし |
| Primary / Secondary ボタン | `B` / `N` |
| メニュー | `M` |

このデモは **グリップとトリガー** を用途分離して読む。グリップは接触中の球を保持、トリガーは保持球のチャージ射出である。

### キャッチ、シールド、投げ返し

1. `左Shift`（または `Space`）を押しっぱなしにして手を選び、Incoming ball の進路へ手のひらを置く
2. CatchVolume に入ってグリップするとボールは GripAnchor へ snap する
3. `G` を押している間だけ接触球を保持する。`G` を離すと手の運動で物理的にリリースする
4. 保持球を bullseye に返すときは左マウス（トリガー）を押して溜め、離す。短押しは 5 m/s、1.5 秒で最大 20 m/s

## 検証観点チェックリスト

- [ ] **ウェーブ進行**: カウントダウン → ウェーブ 1〜3 → リザルト → 自動リスタート、が止まらずに回る
- [ ] **射出**: ランチャーがプレイヤーを狙って射出する。ウェーブが進むと速度・頻度が上がる
- [ ] **保持**: グリップ中だけ接触した Incoming ball が GripAnchor に追従し、解放で物理投擲されるか
- [ ] **GripAnchor**: 保持球が手のひら前方の GripAnchor に snap し、グリップのみで Grabbed ポーズになるか
- [ ] **反射**: 腕は Incoming ball を反射してライフを減らさず、胴体は反射してライフを減らすか
- [ ] **投擲**: Trigger performed でチャージを開始し、canceled でチャージ量どおりにボールが飛ぶ。リリース直後に自動キャッチへ戻らないか
- [ ] **的当て**: ランダム配置の bullseye に当たるとスコアが入る。ランチャーに当てると一時停止する
- [ ] **被弾**: 避けずに当たるとライフが減る。3 回でリザルトへ落ちる
- [ ] **スコアボード**: スコア / コンボ / ライフ / ウェーブ表示が実際の進行と一致する
- [ ] **メニュー**: Menu/B/Y で開閉し、スティック上下で選択、A/Xで決定できるか。メニュー中に投擲・移動・チャージが起きないか
- [ ] **Hapbeat ステータス行**: スコアボード下部のステータス表示に接続状態が出る
- [ ] **実機疎通**: PC と同一 LAN 上の Hapbeat に UDP が届き、キャッチ / 被弾 / 投擲 / 保持中ループ / ウェーブのジングルが鳴る
- [ ] **触覚の強度バランス**: 個々のイベントが強すぎ / 弱すぎないか。保持中ループ (`held_loop`) と接近警告 (`incoming_warn`) がうるさくないか

## 注意

- 視点の高さはリグの `XR Origin` が吸収する。構成は `XR Origin` → `Camera Offset`（ローカル y = 1.6 m）
  → `Main Camera` ＋ 左右の手。
  - **シミュレータ**（XR ローダ無し）: 入力サブシステムが存在しないので `Camera Offset` は 1.6 m のまま。
    シミュレート HMD は原点起点の姿勢を返すため、視点は立ち姿勢になる
  - **実機で Floor トラッキングが取れる場合**: `XROrigin` が `Camera Offset` を 0 に落とし、OpenXR が返す
    実身長込みの姿勢がそのまま視点高さになる
  - **実機で Floor が取れない場合**: Device モードにフォールバックし、`Camera Y Offset`（1.6 m）が適用される

  シミュレータ専用の補正は入れていない。実機とシミュレータで同じリグが動く。
- Game ビューにフォーカスが無いとキーボード入力がシミュレータに届かない。反応しないときはまず
  Game ビューをクリックする。
- **エディタで Play したときは触覚を実送信する**（この手順で実機疎通を確認できる）。一方、
  バッチ検証（`SmokePlay` / `VerifySimulator`）は既定で送信しない。無人実行が手元の Hapbeat を
  鳴らさないようにするためで、`HapbeatManager` ごと停止するので PING も出ない。バッチで実送信
  させたい場合のみ `-ExtraArgs @('-gbHapticsLive')` を付ける。
- バッチ実行はエディタの音をミュートするが、終了時に元の設定へ戻す
  （`EditorUtility.audioMasterMute` は EditorPrefs 経由で **全プロジェクト共有** のため）。
  もし音が出ないままになったら `.\tools\run-unity.ps1 -Method GloveBallDemo.Editor.BatchOps.UnmuteEditorAudio`
  で解除できる。
