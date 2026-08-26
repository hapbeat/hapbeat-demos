# Why `com.hapbeat.sdk` is pinned to a Git commit

`manifest.json` pins the Hapbeat Unity SDK to the exact commit that contains the
Unity 6000.0 compatibility fix:

```json
"com.hapbeat.sdk": "https://github.com/hapbeat/hapbeat-unity-sdk.git#21e56fe3875f942d8230e91bffbaaf5aa963803c"
```

The published `v0.4.0` tag cannot be used here. Its editor assembly calls
`EditorUtility.EntityIdToObject`, an API that **does not exist in Unity 6000.0 LTS** —
the version this project is pinned to for Ultimate Glove Ball asset compatibility.
The symbol is absent from `6000.0.59f2/Editor/Data/Managed/UnityEditor.dll` and present
in `6000.3.12f1`'s. On 6000.0 it fails as:

```
Editor\HapbeatEventMapPlaySnapshot.cs(88,41): error CS0117:
'EditorUtility' does not contain a definition for 'EntityIdToObject'
```

`Hapbeat.Editor` failing takes the whole project's compilation with it, so referencing
only the SDK runtime is not a workaround either.

The fixed commit uses the version-guarded `HapbeatEditorCompat.IdToObject` helper.
Pinning its immutable commit ID lets a standalone clone restore the package without a
neighbouring SDK checkout while keeping the Unity version compatibility reproducible.
