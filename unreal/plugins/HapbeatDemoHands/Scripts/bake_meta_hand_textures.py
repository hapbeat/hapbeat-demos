"""Blender: bake a skin texture for Meta's OpenXR hand meshes (ThirdParty/MetaHands/Meshes).

The stock meshes come without textures, so a uniformly coloured hand reads flat, like clay. This
bakes, on the mesh's own UVs (map1, no overlaps):
- ambient occlusion (Cycles) - shade between the fingers and in the joint folds;
- skin tone variation from the joint positions - warmer fingertips and knuckles, faint mottling.
No nails or wrinkles (tried 2026-10-01: at this mesh detail they read as uncanny, not as the user's
own hand). RGB = albedo (sRGB, AO multiplied in); a missing texture falls back to white.
Run from the demo project folder:  blender -b -P Plugins/HapbeatDemoHands/Scripts/bake_meta_hand_textures.py
Output: ThirdParty/MetaHands/Textures/T_MetaHand_{L,R}.png (derived from the Meta mesh; not committed)
"""
from pathlib import Path
import bpy
import numpy as np

ROOT = Path.cwd()  # the demo project (ThirdParty/MetaHands lives there)
MESHES = ROOT / 'ThirdParty' / 'MetaHands' / 'Meshes'
OUT = ROOT / 'ThirdParty' / 'MetaHands' / 'Textures'
SIZE = 1024
CM = 0.01  # the FBX imports in metres

SKIN = np.array([.87, .70, .58])        # sRGB base (East-Asian skin)
WARM = np.array([.86, .58, .50])        # fingertips, knuckles
PALE = np.array([.90, .75, .66])        # palm
FINGERS = ('Index', 'Middle', 'Ring', 'Little')


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def rasterize(obj):
    """Per texel (bottom-up rows, like Blender images): world position, normal, coverage."""
    me = obj.data
    me.calc_loop_triangles()
    uv = np.array([d.uv[:] for d in me.uv_layers['map1'].data])
    co = np.array([(obj.matrix_world @ v.co)[:] for v in me.vertices])
    nrm = np.array([c.vector[:] for c in me.corner_normals])
    rot = np.array(obj.matrix_world.to_3x3())
    nrm = nrm @ rot.T
    pos = np.zeros((SIZE, SIZE, 3)); nor = np.zeros((SIZE, SIZE, 3)); cov = np.zeros((SIZE, SIZE), bool)
    for t in me.loop_triangles:
        L = list(t.loops)
        p = uv[L] * SIZE
        x0, y0 = np.floor(p.min(0)).astype(int); x1, y1 = np.ceil(p.max(0)).astype(int)
        xs, ys = np.meshgrid(np.arange(x0, x1) + .5, np.arange(y0, y1) + .5)
        a, b, c = p
        d = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(d) < 1e-12:
            continue
        w0 = ((b[1] - c[1]) * (xs - c[0]) + (c[0] - b[0]) * (ys - c[1])) / d
        w1 = ((c[1] - a[1]) * (xs - c[0]) + (a[0] - c[0]) * (ys - c[1])) / d
        w2 = 1 - w0 - w1
        m = (w0 >= -1e-4) & (w1 >= -1e-4) & (w2 >= -1e-4)
        if not m.any():
            continue
        iy = np.clip(ys[m].astype(int), 0, SIZE - 1); ix = np.clip(xs[m].astype(int), 0, SIZE - 1)
        W = np.stack([w0[m], w1[m], w2[m]], 1)
        V = co[list(t.vertices)]
        pos[iy, ix] = W @ V
        n = W @ nrm[L]
        nor[iy, ix] = n / np.linalg.norm(n, axis=1, keepdims=True)
        cov[iy, ix] = True
    return pos, nor, cov


def dilate(img, cov, steps=8):
    """Grow covered texels outward so mip maps and filtering do not pull in the empty background."""
    img = img.copy(); cov = cov.copy()
    for _ in range(steps):
        acc = np.zeros_like(img); n = np.zeros(cov.shape)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            s = np.roll(np.roll(cov, dy, 0), dx, 1)
            acc += np.roll(np.roll(img, dy, 0), dx, 1) * s[..., None]; n += s
        grow = (~cov) & (n > 0)
        img[grow] = acc[grow] / n[grow][:, None]
        cov |= grow
    return img


def bake_ao(obj):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'; scene.cycles.samples = 96; scene.cycles.device = 'CPU'
    if scene.world is None:
        scene.world = bpy.data.worlds.new('World')
    scene.world.light_settings.distance = 1.5 * CM
    img = bpy.data.images.new('AO', SIZE, SIZE, alpha=False, float_buffer=True)
    mat = bpy.data.materials.new('Bake'); mat.use_nodes = True
    node = mat.node_tree.nodes.new('ShaderNodeTexImage'); node.image = img
    mat.node_tree.nodes.active = node
    obj.data.materials.clear(); obj.data.materials.append(mat)
    obj.data.uv_layers.active = obj.data.uv_layers['map1']
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True); bpy.context.view_layer.objects.active = obj
    bpy.ops.object.bake(type='AO', margin=8, use_clear=True)
    return np.array(img.pixels[:]).reshape(SIZE, SIZE, 4)[..., 0]


for side, name in (('L', 'OpenXRLeftHand'), ('R', 'OpenXRRightHand')):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(MESHES / f'{name}.fbx'))
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    obj = next(o for o in bpy.data.objects if o.type == 'MESH')
    J = {b.name[len('XRHand_'):]: np.array((arm.matrix_world @ b.head_local)[:]) for b in arm.data.bones}
    pos, nor, cov = rasterize(obj)
    ao = bake_ao(obj)

    # Hand frame: palm normal from the knuckle plane; the thumb sits on the palm side.
    wrist = J['Wrist']
    plane = np.cross(J['IndexProximal'] - wrist, J['LittleProximal'] - wrist); plane /= np.linalg.norm(plane)
    palm = plane * np.sign(np.dot(J['ThumbTip'] - wrist, plane) or 1.0)
    dorsal = -palm
    radial = J['IndexProximal'] - J['LittleProximal']; radial /= np.linalg.norm(radial)

    P = pos[cov]; N = nor[cov]
    col = np.tile(SKIN, (len(P), 1))
    rng = np.random.default_rng(7)
    # Faint mottling (smooth, a few cm scale).
    freq = rng.normal(size=(6, 3)) * 1.6 / CM
    mott = sum(np.sin(P @ f + rng.uniform(0, 6.28)) for f in freq) / 6
    col *= (1 + .025 * mott)[:, None]
    palmness = smooth(.1, .6, N @ palm)
    col = col * (1 - .35 * palmness[:, None]) + PALE * (.35 * palmness[:, None])

    chains = [('Thumb', ['ThumbMetacarpal', 'ThumbProximal', 'ThumbDistal', 'ThumbTip'])] + \
             [(f, [f + 'Metacarpal', f + 'Proximal', f + 'Intermediate', f + 'Distal', f + 'Tip']) for f in FINGERS]
    # Tone only: warmer fingertips and knuckles; the AO below adds the folds.
    for finger, names in chains:
        pts = [J[n] for n in names]
        fd = dorsal if finger != 'Thumb' else (.45 * dorsal + .9 * radial)
        d_tip = np.linalg.norm(P - pts[-1], axis=1)
        w = smooth(2.2 * CM, .4 * CM, d_tip) * .4
        col = col * (1 - w[:, None]) + WARM * w[:, None]
        for k in range(1, len(pts) - 1):
            u = pts[k + 1] - pts[k - 1]; u /= np.linalg.norm(u)
            fdk = fd - u * np.dot(fd, u); fdk /= np.linalg.norm(fdk)
            rel = P - pts[k]
            a = rel @ u                                  # along the finger
            radial_d = np.linalg.norm(rel - np.outer(a, u), axis=1)
            near = (radial_d < 1.6 * CM) & (np.abs(a) < .6 * CM)
            back = smooth(.15, .55, N @ fdk)
            span = .6 * CM if k == 1 and finger != 'Thumb' else .45 * CM   # MCP knuckle: broader
            bell = np.exp(-(a / span) ** 2) * near
            col = col * (1 - (.15 * bell * back)[:, None]) + WARM * (.15 * bell * back)[:, None]

    # Faint AO only: the bake pose (fingers spread) rarely matches the tracked hand, and strong
    # occlusion between the fingers read as dirt.
    shade = (1 - .3 * (1 - ao[cov]))[:, None]
    OUT.mkdir(parents=True, exist_ok=True)
    tex = np.zeros((SIZE, SIZE, 4)); tex[..., 3] = 1
    tex[cov, :3] = np.clip(col * shade, 0, 1)
    tex = dilate(tex, cov)
    name = f'T_MetaHand_{side}'
    img = bpy.data.images.new(name, SIZE, SIZE, alpha=True)
    img.pixels.foreach_set(tex.astype(np.float32).ravel())
    img.filepath_raw = str(OUT / f'{name}.png'); img.file_format = 'PNG'
    img.save()
    print('META_HAND_TEXTURE', side, 'ao mean', round(float(ao[cov].mean()), 3))
