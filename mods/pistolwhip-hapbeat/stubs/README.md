# stubs/ — コンパイル検証専用のダミー型

**製品ビルド (`src/PistolWhipHapbeat.csproj`) には含まれない。**
`compile-check/CompileCheck.csproj` だけがこれをコンパイルする。

## なぜあるか

この mod がビルドに必要とする DLL（MelonLoader / 0Harmony / Il2CppInterop.Runtime /
MelonLoader が生成する `Assembly-CSharp.dll`）は、**Pistol Whip を所有していて
MelonLoader を導入した PC にしか存在しない**。
ゲームを持たない環境でも「構文・型・API 呼び出しの整合」を CI 的に確認できるように、
mod が実際に触る **API 表面だけ** を最小限に写した型定義を置いてある。

## 含めている範囲

| 名前空間 | 型 | 用途 |
|---|---|---|
| `MelonLoader` | `MelonMod` / `MelonInfoAttribute` / `MelonGameAttribute` / `MelonLogger` | mod のエントリポイントとログ |
| `HarmonyLib` | `Harmony` / `HarmonyMethod` / `AccessTools` | 手動パッチ適用 |
| `Il2CppInterop.Runtime` | `DelegateSupport` | 管理デリゲート → Il2Cpp デリゲート変換 |
| `SonicBloom.Koreo` | `Koreographer` / `Koreography` / `KoreographyEvent` / `DeltaSlice` / `KoreographyEventCallbackWithTime` | BGM 拍 (Koreographer) 連携 |

ゲーム側の戦闘系クラス（`Gun` / `PlayerHUD` など）のスタブは**無い**。
mod はそれらを型として参照せず、実行時に名前で解決してリフレクションで読むため
（理由は `src/HookInstaller.cs` / `src/GameReflection.cs` のコメント）。
フック対象の一覧は README のフック表が正。

## 限界（compile-check が保証しないこと）

- **実型との一致は保証しない。** ここでの `Koreographer.RegisterForEventsWithTime` は
  「その名前・その引数で呼べる」ことしか確認していない。実 DLL 側で名前空間・シグネチャ・
  ジェネリック制約が違えば、所有者の環境でのビルドで初めて落ちる
  （README の「実機確認チェックリスト」参照）。
- 実行時挙動（Harmony が実際にパッチできるか、Il2Cpp デリゲート変換が通るか）は
  一切検証していない。
