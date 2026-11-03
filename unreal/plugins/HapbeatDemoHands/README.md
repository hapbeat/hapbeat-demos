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

- Placeholder hand: `UHapbeatDemoHand::Create` with a null mesh returns a hand drawn from engine shapes (spheres on the
  joints, cylinders between them, two instanced static mesh components) from the same tracked joints, so a clone without
  Meta's meshes still shows hands. Same calls: `Update`, `SetVisible`, `SetStyle` (colour only), `SetSortPriorityBase`;
  `GetRig()` is a built-in rough layout, so `BuildReferenceSample` works. `IsPlaceholder()` tells it apart: there
  `GetPoseMesh()` and `GetOutlineMaterial()` are null. Nothing changes when the mesh is there.

## Per project

Third-party content stays in the project (it is not in this public repository):
1. Meta's OpenXR hand meshes (Meta Interaction SDK, `OpenXRLeftHand.fbx` / `OpenXRRightHand.fbx`), imported by the
   project as skeletal meshes.
2. Skin textures `T_MetaHand_{L,R}.png` baked from those meshes into `<project>/ThirdParty/MetaHands/Textures`:
   `blender -b -P Plugins/HapbeatDemoHands/Scripts/bake_meta_hand_textures.py` (run in the project folder).
3. `Scripts/create_hand_materials.py` (Unreal Python, run in the project) creates the materials under
   `/Game/HapbeatDemoHands/Materials` and imports the textures to `/Game/HapbeatDemoHands/Textures`.

In C++: take `FHapbeatDemoHandMaterials::FindInConstructor()` and the meshes/textures in the owner's constructor
(so they cook), with `FHapbeatDemoHandMaterials::FindOptionalMesh(Path)` / `FindOptionalTexture(Path)` for what may not
be installed (null, without the `CDO Constructor ... Failed to find` error), then
`UHapbeatDemoHand::Create(Owner, Mesh, SkinTexture, Materials, Style, bRightHand)` and call `Update(State)` every frame.
Keep the returned object in a `UPROPERTY`.

The placeholder's material `M_HandPlaceholder` comes from `create_hand_materials.py` too; commit it in the project (it
contains nothing of Meta's). Without it the placeholder uses the engine shape's own material, without the sort priority
over the Demo Session panels.
