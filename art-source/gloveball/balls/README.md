# Original GloveBall ball models

Blender 5.2.1 LTS source assets, authored for this demo without third-party models.
All five balls have a nominal diameter of 0.22 m. This is a source-art scale;
Unity integration must match the existing ball prefab's visual diameter.

- `create_balls.py`: reproducible initial modeling and comparison-render script.
- `generated/balls.blend`: editable source, tracked with Git LFS.
- `generated/comparison.png`: look-development comparison.

Run Blender with `--background --factory-startup --python create_balls.py`.
To regenerate, pass `-- --output <new-directory>`; existing blend files are protected.
`-- --overwrite` explicitly regenerates the checked-in source; do not use it after
manual Blender edits unless replacing those edits is intended.
Manual edits belong in the blend file and are not implicitly reconstructed by the script.
Blender backup files are ignored; commit meaningful source revisions instead.
Other PCs need Git LFS (`git lfs pull`) to obtain actual binary assets.

`generated/runtime/` contains FBX meshes and baked 512px tangent-space normal maps.
Unity copies are in `unity/gloveball/Assets/GloveBallDemo/Art/Balls/` with URP materials.
Blender source stays outside Assets so Unity builds do not depend on Blender.

Models: Bowling (paired finger holes and lower thumb hole), Volleyball, Foam,
Basketball, Perforated (hollow plastic shell, 32 visible holes). Perforated holes are
visual only: all types retain the original solid sphere physics collider and 0.25kg
mass. Basketball is a heavier-feeling presentation category, not a physically heavier
simulation. No gameplay or scene regeneration is required.

## Runtime authoring

- In the scene select **BallPool**, then edit **Launch Ball Kinds**. Only listed types
  are selected at random on every pool checkout. One entry fixes the type; empty
  disables shots. Duplicate entries weight the selection. Default: all five.
- **BallImpactEventMap.asset** in `Assets/GloveBallDemo/Haptics/` contains 15 independent
  events: each kind × `l_arm_collide`, `r_arm_collide`, `body_collide`. Replace an
  entry's **Stream Clip** to tune it. Targets inherit the existing left/right/body
  target settings at initial installation. The original EventMap is not modified.
- `Art/Balls/<Kind>Impact.wav` is an independent placeholder copy per ball kind;
  replacing that WAV at the same path (preserve its `.meta`) updates all three
  corresponding impacts. Placeholder waveforms initially match the existing impact.
  Rebuild/reinstall the APK after changes. Speaker collision sounds and grab/charge
  events remain unchanged.
- The resource prefab `Assets/Resources/BallImpactBindings.prefab` wires the map into
  the relay at startup without editing the scene. Do not remove it.
- `GloveBall Demo/Install Ball Variants (no scene changes)` installs assets additively;
  it preserves an already configured ball prefab, materials and event entries.
