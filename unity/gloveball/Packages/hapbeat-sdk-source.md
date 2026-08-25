# Why `com.hapbeat.sdk` is a local path, not a git URL

`manifest.json` pulls the Hapbeat Unity SDK from the workspace checkout:

```json
"com.hapbeat.sdk": "file:../../../../../repos-sdk/hapbeat-unity-sdk"
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

The fix (a version-guarded `HapbeatEditorCompat.IdToObject` helper) is committed in the
workspace checkout but is not in any release yet. **Switch back to
`https://github.com/hapbeat/hapbeat-unity-sdk.git#<tag>` as soon as a released tag
contains it** — a git URL is the reproducible reference for anyone cloning this repo
without the SDK workspace beside it.
