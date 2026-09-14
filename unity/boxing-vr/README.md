# Hapbeat Boxing

Unity **6000.3.12f1** の90秒VRスパーリングデモ。左右のグローブで攻撃・ガードし、頭を動かして相手のパンチを避けます。3カウント後にラウンドが始まり、終了時に得点と再開メニューを表示します。Hapbeatが未接続でもゲームは動作します。

## Editor + XR Interaction Simulator（HMD不要）

1. `Hapbeat Boxing > Editor Input > Simulator` を選び、Playを押します。Gameビューをクリックしてキーボード入力を渡してください。
2. ゲームの入力は `Controllers` のまま使います。`Desktop` は別の簡易操作モードです。
3. `Hapbeat Boxing > Editor Input > Controls` に操作ガイドがあります。TabでFPS／デバイス操作を切替、Hで頭、`[`／`]`で左右デバイスを選択し、WASDで移動、Q/Eで上下移動、右マウスドラッグ／矢印キーで回転、Rでリセットします。仮想コントローラーの1がA/X、2がB/Yです。Escapeでゲームメニュー、Enterで決定もできます。

Unity XRI 3.3.1の標準シミュレーターPrefab・入力設定を使用します。独自のテスト姿勢をゲームへ直接渡す機能ではなく、実機と同じInput System経由で頭・両手・ボタンを読みます。操作ガイドはEditorウィンドウに表示し、サンプルの装飾UIは取り込んでいません。

シミュレーター選択時はEditorのネイティブOpenXR起動を無効にします。Air Linkへ戻す時はPlayを止め、`Hapbeat Boxing > Editor Input > Air Link` を選んでください。選択はこのPC・このプロジェクト専用です。AndroidのOpenXR設定は変えず、シミュレーター資産は `Assets/Boxing/Editor/Simulator` に隔離してAPKへ含めません。

## Editor + Quest Air Link

1. この `boxing-vr` フォルダーをUnity 6000.3.12f1で開き、`Assets/Boxing/Scenes/Boxing.unity` を開きます。
2. Meta Horizon LinkでQuestをAir Link接続します。WindowsのOpenXR runtimeはMeta Horizon Linkを選択してください。
3. `Hapbeat Boxing > Editor Input > Air Link` を選び、Unityのactive build targetをWindowsにしてPlayを押します。起動時メニューの `START 90s ROUND` を選択します。

左右Touchコントローラーが既定です。A/Xで決定、左スティックで選択、Menu/B/Yでメニューを開閉します。Handsモード、またはコントローラー未追跡時は選択肢を1.5秒見つめても操作できます。追跡中のコントローラー選択を視線で上書きしません。起動時のHMD位置・向きをリング上の開始位置に合わせます。必要ならメニューから `RECENTER` を選択します。

周囲の物を片付け、現実の物体や人を殴らない範囲で試してください。コントローラーのストラップを使い、強く振り切らず弱いパンチから確認します。速度応答は力の測定ではなくゲーム用の演出です。

`INPUT: Hands` にするとXR Handsの手関節からグローブを動かします。握った手でパンチし、両手を1.2秒開くとメニューが出ます。Meta Horizon Linkの手追跡利用には対応する開発機能を有効にする必要があります。手の交差・遮蔽で追跡が途切れる場合はControllersへ切り替えてください。追跡不能時・切替直後には攻撃判定を停止し、復帰移動をパンチとして扱いません。

Meta公式: [Link開発設定](https://developers.meta.com/horizon/documentation/unity/unity-link/)、[手追跡](https://developers.meta.com/horizon/documentation/unity/unity-handtracking-overview/)。

## 操作と調整

- プレイヤーの判定は左右グローブと頭。プレイヤーの腕は描画・判定しません。相手には見た目用の腕・脚があります。
- 相手はジャブ、クロス、フックを繰り返し、小さく前後左右へ動きます。狙いは予備動作開始時に固定し、パンチ中は頭を追尾しません。
- メニュー表示、Questのシステムメニュー・フォーカス喪失、追跡喪失、開始点から1m以上離れた時は対戦時間・敵・触覚を停止します。システムメニューから戻った時は `RESUME` で明示的に再開します。
- `Assets/Boxing/BoxingTuning.asset` でラウンド時間、攻撃間隔、予備動作、判定サイズ、速度→ゲインのカーブ・閾値を変更できます。
- `IMPACT: WeakHard` は2.5m/sを境に波形を変更し、その中でも相対速度に応じてゲインを変えます。`Continuous` は同じ波形のゲインだけを連続変更します。既定は0.25m/s未満を無視し、6m/sで最大ゲインです。質量や力の測定ではなく、接触する2物体の相対速度の大きさを使います。
- `Assets/Boxing/Haptics/BoxingEventMap.asset` の6エントリーで、各部位の弱打・強打の波形、絶対ゲイン、送信先を編集できます。接触検知と波形選択は分離されています。

## Hapbeat

既定の送信先はGloveBallと同じ `*/pos_l_wrist`、`*/pos_r_wrist`、`*/pos_neck` です。デバイスのposition設定を合わせ、PC（Air Link時）またはQuest（APK時）と同じ到達可能なLANに接続してください。1台で試す場合はEventMapのtargetをそのデバイスのpositionに合わせます。

Unity SDKのStreamClipを使うため、事前のKit転送は不要です。左右・頭それぞれの弱打／強打に独立したトリガーがあります。`GainMultiplier` に接触時の速度応答を渡し、EventMap側のゲインと掛け合わせます。自動検証はSDK GameObjectをAwake前に無効化し、PC音声も実機送信も行いません。通常のEditor Playは音声・触覚ONです。

## Demo Switch

logical IDは `boxing`、Android packageは `com.hapbeat.boxing`、Activityは `com.unity3d.player.UnityPlayerGameActivity`。既存のDemo Switch packageを使い、GloveBall (`jp.hapbeat.gloveballdemo`) とHand Demoへの切替先を登録しています。

MCUのCに `boxing` を設定し、切替元APKのallowlistにもboxingを追加して再ビルドする必要があります。MCUの設定だけでは未インストールAPKやUnity Editorを起動できません。既存APKと同じ認証設定を使い、共有secretをリポジトリへ保存しないでください。

## 検証・ビルド

Unity Editorを閉じて、PowerShellから実行します。Unityの場所が異なる場合は `-UnityExe` を指定します。

```powershell
./tools/run-unity.ps1 -Task Tests
./tools/run-unity.ps1 -Task Simulator
./tools/run-unity.ps1 -Task InputTests
./tools/run-unity.ps1 -Task SimulatorSmoke
./tools/run-unity.ps1 -Task Smoke
./tools/run-unity.ps1 -Task Capture
./tools/run-unity.ps1 -Task Windows
./tools/run-unity.ps1 -Task Android
./tools/verify-apk.ps1
```

`Logs/` にテスト結果・実行ログ・画像が出ます。Smokeは実際のPlay Modeで90秒ラウンド、パンチ、ガード、回避、被弾、メニュー停止を再生し、3部位・弱打／強打・ゲイン変化・触覚送信ゼロ・エラーゼロを検証します。`Builds/Windows/HapbeatBoxing.exe`、`Builds/HapbeatBoxing.apk` が生成先です。

InputTestsはUnity公式InputTestFixtureを使うPlay Modeの入力テストです。SimulatorSmokeは標準Prefabへキーボードイベントを渡し、頭・左右の個別移動、ボタン、追跡喪失／復帰を実際のゲーム入力で確認します。全自動検証は無音・実機送信なしです。ビルドでは詳細レポートからEditorシミュレーター資産の混入も検査します。

各コマンドは終了コードだけでなく起動・インポートログも検査します。Unityは設定ファイルの構文エラー後もテストやScene検証を続ける場合があるため、`BOXING_SCENE_VALID` 単独では成功と扱いません。任意の起動ログは `./tools/assert-unity-log.ps1 -LogPath ./Logs/AirLinkEditor.log` で確認できます。Unityのシリアライズ済みファイルを一括で末尾空白除去しないでください。空のレイヤー名は明示的な空文字列として保持します。

3本のAPKを `Builds/DemoSwitch/` に用意した場合は、USBデバッグを許可したQuestに `./tools/install-demo-switch.ps1` で一括更新できます。既存アプリのデータは消去せず、署名が異なる場合も自動アンインストールしません。インストール後にQuestでいずれかのデモを起動してからMCUを操作します。

Editorのみの `INPUT: Desktop` はQ/Eでパンチ、Spaceでガード、A/Dで左右へ回避、Sでダッキング、右マウスドラッグで視線、Enterでメニュー決定、Escapeでメニューです。HMD実機での手追跡品質や実際の触覚の強さはこのモードでは検証できません。

`Create Initial Scene` は初回だけ雛形を作成します。生成後のScene・Material・EventMapが正本で、既存Sceneを作り直しません。

## ソースとライセンス

XR設定は既存Hapbeat VR Templateを基にしています。SDKはworkspace内の `repos-sdk/hapbeat-unity-sdk` を参照します。別環境では同じworkspace構造を用意するか `Packages/manifest.json` のSDK参照を変更してください。描画・音・触覚素材は自作で、第三者の非CC0キャラクターやUnityサンプルartを含めていません。[素材情報](THIRD_PARTY_NOTICES.md)を参照してください。
