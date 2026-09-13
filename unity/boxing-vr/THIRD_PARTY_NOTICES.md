# Boxing asset provenance

1. Glove shapes, opponent mannequin, ring and gym: original procedural geometry authored in `Assets/Boxing/Editor/BoxingProject.cs`. No downloaded character, texture or animation assets. Unity built-in primitive meshes are used as construction primitives.
2. Soft/Hard haptic pulses and bell: original deterministic synthesis in `BoxingProject.MakeWave`, 16 kHz PCM16 mono. No sampled recordings. These generated Hapbeat-authored demo content assets may be used under the repository MIT license.
3. Unity Engine, Universal Render Pipeline, XR Hands, OpenXR, XR Interaction Toolkit and Unity UI: development/runtime packages resolved by Unity Package Manager; their bundled licenses apply. They are not represented as CC0 assets. Unity's built-in `LegacyRuntime.ttf` is used for UI text.
4. Hapbeat SDK and Demo Switch: Hapbeat source code; respective repository/package licenses apply.

The project's CC0 requirement applies to externally sourced visual/audio content. No such third-party content has been imported. No Ultimate Glove Ball/Meta game art or Unity XRI sample art is included.
