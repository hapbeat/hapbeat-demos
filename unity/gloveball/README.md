# Hapbeat GloveBall Demo

Meta の Ultimate Glove Ball アリーナ資産を使った、Meta Quest 向けの 1 人プレイ Hapbeat デモ。source repository の checkpoint `69f4c19` から、Unity プロジェクトの再現可能なソーススナップショットを収録している。

## 必要な環境

- Git と [Git Large File Storage (Git LFS)](https://git-lfs.com/)
- Unity Hub
- Unity **6000.0.59f2**（別バージョンへ自動更新しない）
- Unity 6000.0.59f2 の Android Build Support
  - Android Software Development Kit (SDK) & Native Development Kit (NDK) Tools
  - OpenJDK
- OpenXR 対応の Meta Quest 2 / 3 / 3S（実機確認時）

Unity Package Manager はプロジェクトを開くと `Packages/manifest.json` から Universal Render Pipeline、Input System、XR Interaction Toolkit、XR Plug-in Management、OpenXR などを復元する。Meta XR SDK は使用しない。

## 初回セットアップ

1. リポジトリを clone し、LFS を有効化して大容量アセットを取得する。

   ```powershell
   git lfs install
   git lfs pull
   ```

2. 開発用 workspace では、`hapbeat-demos` と `hapbeat-unity-sdk` を次の相対配置にする。

   ```text
   hapbeat-sdk-workspace/
     repos-sdk/hapbeat-unity-sdk/
     repos-tools/hapbeat-demos/unity/gloveball/
   ```

3. 移管直後の `Packages/manifest.json` は、未リリースの Unity 6000.0 互換修正を使うため、SDK を次のローカル checkout で暫定参照する。

   ```json
   "com.hapbeat.sdk": "file:../../../../../repos-sdk/hapbeat-unity-sdk"
   ```

4. Unity Hub でこの `unity/gloveball` ディレクトリを Unity 6000.0.59f2 のプロジェクトとして開き、package import と script compilation の完了を待つ。
5. Project Settings > Player > Other Settings > Active Input Handling が **Input System Package (New)** のみになっていることを確認する。Both と Input Manager (Old) は使用しない。
6. Android を active build target にし、Project Settings > XR Plug-in Management > Android で OpenXR が有効であることを確認する。Quest Pro 向け eye tracking requirement は有効化しない。

## 安全な検証とビルド

Unity Editor を閉じてから、プロジェクトルートで次の順に実行する。

```powershell
.\tools\run-unity.ps1 -Method GloveBallDemo.Editor.BatchOps.CompileCheck
.\tools\run-unity.ps1 -RunTests EditMode
.\tools\run-unity.ps1 -Method GloveBallDemo.Editor.BatchOps.ReportSceneHealth -ExtraArgs @('-gbScene','Assets/GloveBallDemo/Scenes/Demo.unity')
.\tools\run-unity.ps1 -Method GloveBallDemo.Editor.BatchOps.BuildApk
```

Unity Hub の標準配置以外へインストールした場合は、各コマンドへ Unity 6000.0.59f2 の実行ファイルを明示する。

```powershell
.\tools\run-unity.ps1 -Method GloveBallDemo.Editor.BatchOps.CompileCheck -UnityExe 'D:\Unity\6000.0.59f2\Editor\Unity.exe'
```

移管時に checked-in された Scene、Prefab、Input Actions、Hapbeat EventMap が現在のスナップショットの正本であり、セットアップや通常検証では削除・再生成しない。`BatchOps.BuildDemo` は破壊的な maintenance-only コマンドのため、新たな作業指示なしに実行しない。自動検証では `-gbHapticsLive` を付けず、PC の音声と Hapbeat 実機への触覚送信を無効のままにする。APK の Quest 実機での起動、フレームレート、操作、触覚は自動検証とは別に確認する。

## 別 PC での再現

1. Unity 6000.0.59f2 と同版の Android Build Support 一式を Unity Hub で導入する。
2. 上記の workspace 配置で両 repository を clone し、`hapbeat-demos` で `git lfs install` と `git lfs pull` を実行する。
3. Unity を開く前に `Packages/manifest.json` の SDK path が存在することを確認する。
4. 初回 import 後、compile check、EditMode tests、scene health、APK build の順に実行する。`BuildDemo` は実行しない。
5. `git status --short` で、意図しない ProjectSettings や生成 asset の差分が残っていないことを確認する。

## 公開前の依存固定

ローカル `file:` 参照は workspace 開発専用で、単独 clone からは再現できない。public push の前に Unity 6000.0 互換修正を含む Hapbeat Unity SDK の immutable Git tag を発行し、`Packages/manifest.json` と `Packages/packages-lock.json` を次の形式へ更新して、clean clone から再検証する。

```json
"com.hapbeat.sdk": "https://github.com/hapbeat/hapbeat-unity-sdk.git#vX.Y.Z"
```

第三者アセットのライセンスは [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) を参照する。
