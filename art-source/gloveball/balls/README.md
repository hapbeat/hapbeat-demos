# Original GloveBall ball models

Blender 5.2.1 LTS source assets, authored for this demo without third-party models.
All three balls have a nominal diameter of 0.22 m. This is a source-art scale;
Unity integration must match the existing ball prefab's visual diameter.

- `create_balls.py`: reproducible initial modeling and comparison-render script.
- `generated/balls.blend`: editable source, tracked with Git LFS.
- `generated/comparison.png`: look-development comparison.

Run Blender with `--background --factory-startup --python create_balls.py`.
To regenerate, pass `-- --output <new-directory>`; existing blend files are protected.
Manual edits belong in the blend file and are not implicitly reconstructed by the script.
Blender backup files are ignored; commit meaningful source revisions instead.
Other PCs need Git LFS (`git lfs pull`) to obtain actual binary assets.

These are look-development models, not integrated runtime assets. Foam and leather
detail uses procedural bump shaders. Bake these into texture maps before Unity use;
FBX alone does not preserve Blender shaders. Export runtime meshes/textures into
`unity/gloveball/Assets/GloveBallDemo/Art/Balls/` once approved, without replacing
the existing scene or changing gameplay/physics. Keep the blend source outside Assets
so Unity builds do not depend on a locally installed Blender.
