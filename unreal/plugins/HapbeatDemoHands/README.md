# Hapbeat Demo Hands (Unreal)

The tracked-hand look shared by the Hapbeat Unreal demos (first used in Safety Mill VR, then T-Rex Encounter).
Each demo keeps a pinned copy in `Plugins/HapbeatDemoHands` (see the demo's `Scripts/sync-hapbeat-demo-hands.ps1`),
like `HapbeatDemoSession`.

## What it does

- `FHapbeatHandRig`: drives Meta's OpenXR hand mesh (bones `XRHand_<EHandKeypoint>`) from the 26 OpenXR joints, and
  builds reference-pose samples with each finger bent in its own plane (illustrations, synthetic review hands).
- `UHapbeatDemoHand`: one drawn hand = three copies of the same posed mesh:
  a Custom Depth copy (never drawn, the depth pre-pass stand-in), an outline shell and the visible surface, which
  draw only the hand's nearest layer and fade out just past the wrist where Meta's mesh ends.
  Styles: `Skin` (textured, thin darker contour) and `Ghost` (Quest-style dark fill, light outline).
  A fourth copy, the opaque inner shell (Skin only, `SetInsideVisible(true)`, off by default), can show the inside of
  the hand through the wrist opening instead of the scene behind it.
  `GetOutlineMaterial()` / `GetSurfaceMaterial()` are the dynamic instances (e.g. tint the outline while grabbing).
  `SetSortPriorityBase(AHapbeatDemoSessionUi::HandSortPriority)` keeps the hands in front of the Demo Session panels.

## Per project

Third-party content stays in the project (it is not in this public repository):
1. Meta's OpenXR hand meshes (Meta Interaction SDK, `OpenXRLeftHand.fbx` / `OpenXRRightHand.fbx`), imported by the
   project as skeletal meshes.
2. Skin textures `T_MetaHand_{L,R}.png` baked from those meshes into `<project>/ThirdParty/MetaHands/Textures`:
   `blender -b -P Plugins/HapbeatDemoHands/Scripts/bake_meta_hand_textures.py` (run in the project folder).
3. `Scripts/create_hand_materials.py` (Unreal Python, run in the project) creates the materials under
   `/Game/HapbeatDemoHands/Materials` and imports the textures to `/Game/HapbeatDemoHands/Textures`.

In C++: take `FHapbeatDemoHandMaterials::FindInConstructor()` and the meshes/textures in the owner's constructor
(so they cook), then `UHapbeatDemoHand::Create(Owner, Mesh, SkinTexture, Materials, Style)` and call `Update(State)`
every frame. Keep the returned object in a `UPROPERTY`.
