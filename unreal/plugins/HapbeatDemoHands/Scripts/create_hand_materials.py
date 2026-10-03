"""Editor (run in each demo project): the shared hand materials and the project's own skin textures.

Creates under /Game/HapbeatDemoHands (the path UHapbeatDemoHand / FHapbeatDemoHandMaterials expect):
- Materials/M_HandDepth: invisible copy that only writes Custom Depth (the depth pre-pass stand-in).
- Materials/M_SkinHand: textured skin, translucent, nearest layer only, short fade past the wrist.
- Materials/M_HandInside: opaque inner shell (back faces only, darker skin), seen only through the wrist opening,
  where it shows the inside of the hand instead of the scene behind (UHapbeatDemoHand::SetInsideVisible, off by default).
- Materials/M_GhostHand + M_GhostOutline: Quest-style dark fill with a light outline shell.
- Textures/T_MetaHand_{L,R}: from <project>/ThirdParty/MetaHands/Textures (baked with bake_meta_hand_textures.py
  from Meta's OpenXR hand meshes; derived from Meta's asset, so they stay in each project and are not shared here).
Run:  UnrealEditor-Cmd <project>.uproject -run=PythonScript -script=<project>/Plugins/HapbeatDemoHands/Scripts/create_hand_materials.py
Safe to rerun (create-once; delete an asset to rebuild it).
"""
import os
import unreal as u

ROOT = os.path.abspath(u.Paths.project_dir())
DEST = '/Game/HapbeatDemoHands/Textures'
MATS = '/Game/HapbeatDemoHands/Materials'
lib = u.EditorAssetLibrary
mel = u.MaterialEditingLibrary
tools = u.AssetToolsHelpers.get_asset_tools()

# Skin textures (optional: without them the hands fall back to the default white texture).
TEXTURES = os.path.join(ROOT, 'ThirdParty', 'MetaHands', 'Textures')
for side in ('L', 'R'):
    src = os.path.join(TEXTURES, f'T_MetaHand_{side}.png')
    if not os.path.exists(src):
        u.log_warning(f'META_HAND_TEXTURE missing {src} (run Scripts/bake_meta_hand_textures.py)'); continue
    t = u.AssetImportTask()
    t.filename = src; t.destination_path = DEST; t.destination_name = f'T_MetaHand_{side}'
    t.automated = True; t.replace_existing = True; t.save = True
    tools.import_asset_tasks([t])
    tex = u.load_asset(f'{DEST}/T_MetaHand_{side}')
    tex.set_editor_property('srgb', True)
    tex.set_editor_property('lod_group', u.TextureGroup.TEXTUREGROUP_CHARACTER)
    lib.save_loaded_asset(tex)


# Shared skin lighting (world space, Z up). Inputs: Albedo (linear), N (vertex normal), V (to camera). Front / back is read from the normal against the view, not from
# TwoSidedSign: the right-hand mesh is mirrored, so its triangle winding (and TwoSidedSign) is flipped.
SKIN_HLSL = r'''
float Face = dot(normalize(N), V) >= -0.5 ? 1.0 : -1.0;
float3 n = normalize(N) * Face;
float3 l = normalize(float3(0.3, -0.4, 1.0));
float ndl = dot(n, l);
float wrap = saturate((ndl + 0.5) / 1.5);
float hemi = 0.5 + 0.5 * n.z;
float3 light = lerp(float3(0.34, 0.31, 0.30), float3(0.58, 0.58, 0.62), hemi) + wrap * float3(0.62, 0.60, 0.56);
float3 sss = float3(0.45, 0.12, 0.06) * saturate(1.0 - abs(ndl) * 2.0) * 0.5;
float3 h = normalize(l + V);
float spec = pow(saturate(dot(n, h)), 10.0) * 0.04;
// No bright rim: a lighter silhouette read as see-through. The edge darkens a little instead and
// the skin outline shell draws the contour.
float edge = pow(1.0 - saturate(dot(n, V)), 3.0);
float inside = Face < 0 ? 0.85 : 1.0;
return (Albedo * (light + sss) * (1.0 - 0.18 * edge) + spec) * inside;
'''

# Nearest layer only: this pixel belongs to the hand surface closest to the eye (the invisible
# depth copy of the hand writes Custom Depth). Pix = PixelDepth, Cd = CustomDepth (cm).
NEAREST_HLSL = r'''
float eps = 0.6 + Pix * 0.004;
return saturate((Cd.r + eps - Pix) * 2.0);
'''
# Outline shell: only outside the hand's silhouette (in front of whatever lies behind the hand).
OUTSIDE_HLSL = r'''
return saturate((Cd.r - Pix - 0.3) * 2.0);
'''

# Fade along the forearm axis (cm from the wrist joint; the mesh ends 2.19 cm past it). The skin
# stays solid to just past the palm heel and is gone 1.2 cm past the wrist joint, before the end cap
# (seen as a dark disc when the fade reached it): a long see-through band over the palm heel read as a
# stray translucent object (2026-10-01).
SKIN_FADE = (-1.8, 3.0)
GHOST_FADE = (-3.0, 5.0)


def new_material(name, blend):
    path = f'{MATS}/{name}'
    if lib.does_asset_exist(path):
        return None
    m = tools.create_asset(name, MATS, u.Material, u.MaterialFactoryNew())
    m.set_editor_property('blend_mode', blend)
    m.set_editor_property('two_sided', True)
    m.set_editor_property('used_with_skeletal_mesh', True)
    m.set_editor_property('shading_model', u.MaterialShadingModel.MSM_UNLIT)
    return m


class Graph:
    def __init__(self, m):
        self.m = m

    def node(self, cls, **props):
        n = mel.create_material_expression(self.m, cls)
        for k, v in props.items(): n.set_editor_property(k, v)
        return n

    def link(self, a, b, pin='', out=''):
        assert mel.connect_material_expressions(a, out, b, pin)

    def custom(self, code, out_type, pins):
        c = self.node(u.MaterialExpressionCustom)
        c.set_editor_property('code', code)
        c.set_editor_property('output_type', out_type)
        items = []
        for pin in pins:
            ci = u.CustomInput(); ci.set_editor_property('input_name', pin); items.append(ci)
        c.set_editor_property('inputs', items)
        return c

    def fade(self, start, length):
        """1 over the hand, easing (smoothstep) to 0 over `length` cm from `start` along the arm."""
        pos = self.node(u.MaterialExpressionPreSkinnedPosition)
        wrist = self.node(u.MaterialExpressionVectorParameter, parameter_name='WristRef', default_value=u.LinearColor(0, 0, 0, 0))
        arm = self.node(u.MaterialExpressionVectorParameter, parameter_name='ArmDir', default_value=u.LinearColor(-1, 0, 0, 0))
        w3 = self.node(u.MaterialExpressionComponentMask, r=True, g=True, b=True, a=False); self.link(wrist, w3)
        a3 = self.node(u.MaterialExpressionComponentMask, r=True, g=True, b=True, a=False); self.link(arm, a3)
        rel = self.node(u.MaterialExpressionSubtract); self.link(pos, rel, 'A'); self.link(w3, rel, 'B')
        along = self.node(u.MaterialExpressionDotProduct); self.link(rel, along, 'A'); self.link(a3, along, 'B')
        st = self.node(u.MaterialExpressionScalarParameter, parameter_name='FadeStart', default_value=start)
        past = self.node(u.MaterialExpressionSubtract); self.link(along, past, 'A'); self.link(st, past, 'B')
        # PreSkinnedPosition is vertex-shader only: the distance is interpolated to pixels.
        past_px = self.node(u.MaterialExpressionVertexInterpolator); self.link(past, past_px)
        ln = self.node(u.MaterialExpressionScalarParameter, parameter_name='FadeLength', default_value=length)
        f = self.custom('float t = saturate(Past / Len); return 1.0 - t * t * (3.0 - 2.0 * t);',
                        u.CustomMaterialOutputType.CMOT_FLOAT1, ('Past', 'Len'))
        self.link(past_px, f, 'Past'); self.link(ln, f, 'Len')
        return f

    def depth_test(self, code):
        c = self.custom(code, u.CustomMaterialOutputType.CMOT_FLOAT1, ('Pix', 'Cd'))
        self.link(self.node(u.MaterialExpressionPixelDepth), c, 'Pix')
        cd = self.node(u.MaterialExpressionSceneTexture, scene_texture_id=u.SceneTextureId.PPI_CUSTOM_DEPTH)
        self.link(cd, c, 'Cd', 'Color')
        return c


def finish(m, name):
    mel.recompile_material(m); lib.save_loaded_asset(m)
    u.log(f'SKIN_MATERIAL {name} created')


# M_HandDepth: the invisible copy of the hand that only writes Custom Depth (the pawn keeps it out of
# the main pass). Two-sided because the mirrored right mesh has flipped winding.
m = new_material('M_HandDepth', u.BlendMode.BLEND_OPAQUE)
if m:
    finish(m, 'M_HandDepth')

# M_SkinHand: the whole visible skin, translucent but nearest layer only - so where it thins out at
# the wrist only the scene behind the hand shows through, never the hand's own far side (which
# otherwise reads as a line / seam).
m = new_material('M_SkinHand', u.BlendMode.BLEND_TRANSLUCENT)
if m:
    m.set_editor_property('translucency_lighting_mode', u.TranslucencyLightingMode.TLM_SURFACE)
    g = Graph(m)
    tex = g.node(u.MaterialExpressionTextureSampleParameter2D, parameter_name='SkinTex',
                 texture=u.load_asset('/Engine/EngineResources/WhiteSquareTexture'))
    normal_ws = g.node(u.MaterialExpressionVertexNormalWS); view_ws = g.node(u.MaterialExpressionCameraVectorWS)
    shade = g.custom(SKIN_HLSL, u.CustomMaterialOutputType.CMOT_FLOAT3, ('Albedo', 'N', 'V'))
    g.link(tex, shade, 'Albedo', 'RGB'); g.link(normal_ws, shade, 'N'); g.link(view_ws, shade, 'V')
    assert mel.connect_material_property(shade, '', u.MaterialProperty.MP_EMISSIVE_COLOR)
    # Even fade, no fresnel term: a denser silhouette inside the fade drew a translucent rim.
    op = g.custom('return F * Near;', u.CustomMaterialOutputType.CMOT_FLOAT1, ('F', 'Near'))
    g.link(g.fade(*SKIN_FADE), op, 'F'); g.link(g.depth_test(NEAREST_HLSL), op, 'Near')
    assert mel.connect_material_property(op, '', u.MaterialProperty.MP_OPACITY)
    finish(m, 'M_SkinHand')

# M_GhostHand (fill) + M_GhostOutline (shell): after Meta's own hand shader (Interaction SDK
# "Interaction/OculusHand": depth pre-pass, an outline shell pushed out along the normals drawn with
# front faces culled, then a flat unlit fill blended over it; both fade at the wrist). Here the
# depth pre-pass is the Custom Depth copy.
m = new_material('M_GhostHand', u.BlendMode.BLEND_TRANSLUCENT)
if m:
    g = Graph(m)
    normal_ws = g.node(u.MaterialExpressionVertexNormalWS); view_ws = g.node(u.MaterialExpressionCameraVectorWS)
    top = g.node(u.MaterialExpressionVectorParameter, parameter_name='ColorTop', default_value=u.LinearColor(.032, .034, .037, 1))
    bottom = g.node(u.MaterialExpressionVectorParameter, parameter_name='ColorBottom', default_value=u.LinearColor(.0137, .0145, .015, 1))
    col = g.custom('float f = pow(saturate(1.0 - dot(normalize(N), V)), 0.16); return lerp(Top.rgb, Bottom.rgb, f);',
                   u.CustomMaterialOutputType.CMOT_FLOAT3, ('N', 'V', 'Top', 'Bottom'))
    g.link(normal_ws, col, 'N'); g.link(view_ws, col, 'V'); g.link(top, col, 'Top'); g.link(bottom, col, 'Bottom')
    assert mel.connect_material_property(col, '', u.MaterialProperty.MP_EMISSIVE_COLOR)
    opacity = g.node(u.MaterialExpressionScalarParameter, parameter_name='Opacity', default_value=.62)
    op = g.custom('return Op * F * Near * saturate(dot(normalize(N), V) * 4.0 + 0.6);',
                  u.CustomMaterialOutputType.CMOT_FLOAT1, ('Op', 'F', 'Near', 'N', 'V'))
    g.link(opacity, op, 'Op'); g.link(g.fade(*GHOST_FADE), op, 'F'); g.link(g.depth_test(NEAREST_HLSL), op, 'Near')
    g.link(normal_ws, op, 'N'); g.link(view_ws, op, 'V')
    assert mel.connect_material_property(op, '', u.MaterialProperty.MP_OPACITY)
    finish(m, 'M_GhostHand')

m = new_material('M_GhostOutline', u.BlendMode.BLEND_TRANSLUCENT)
if m:
    g = Graph(m)
    normal_ws = g.node(u.MaterialExpressionVertexNormalWS); view_ws = g.node(u.MaterialExpressionCameraVectorWS)
    width = g.node(u.MaterialExpressionScalarParameter, parameter_name='OutlineWidth', default_value=.18)
    push = g.node(u.MaterialExpressionMultiply); g.link(normal_ws, push, 'A'); g.link(width, push, 'B')
    assert mel.connect_material_property(push, '', u.MaterialProperty.MP_WORLD_POSITION_OFFSET)
    color = g.node(u.MaterialExpressionVectorParameter, parameter_name='OutlineColor', default_value=u.LinearColor(.72, .74, .78, 1))
    assert mel.connect_material_property(color, '', u.MaterialProperty.MP_EMISSIVE_COLOR)
    opacity = g.node(u.MaterialExpressionScalarParameter, parameter_name='OutlineOpacity', default_value=.85)
    # Back faces of the shell only (front faces culled), and only outside the hand's silhouette.
    op = g.custom('return Op * F * Out * saturate(-dot(normalize(N), V) * 6.0 + 0.3);',
                  u.CustomMaterialOutputType.CMOT_FLOAT1, ('Op', 'F', 'Out', 'N', 'V'))
    g.link(opacity, op, 'Op'); g.link(g.fade(*GHOST_FADE), op, 'F'); g.link(g.depth_test(OUTSIDE_HLSL), op, 'Out')
    g.link(normal_ws, op, 'N'); g.link(view_ws, op, 'V')
    assert mel.connect_material_property(op, '', u.MaterialProperty.MP_OPACITY)
    finish(m, 'M_GhostOutline')

# M_HandInside: the hand's inner walls, opaque, back faces only (front faces clipped by the mask; the facing is
# read from the normal against the view because the mirrored right mesh flips TwoSidedSign). Hidden behind the
# skin from outside; through the wrist opening it closes the hand like the inside of a glove.
m = new_material('M_HandInside', u.BlendMode.BLEND_MASKED)
if m:
    g = Graph(m)
    tex = g.node(u.MaterialExpressionTextureSampleParameter2D, parameter_name='SkinTex',
                 texture=u.load_asset('/Engine/EngineResources/WhiteSquareTexture'))
    shade = g.node(u.MaterialExpressionScalarParameter, parameter_name='InsideShade', default_value=.42)
    col = g.node(u.MaterialExpressionMultiply); g.link(tex, col, 'A', 'RGB'); g.link(shade, col, 'B')
    assert mel.connect_material_property(col, '', u.MaterialProperty.MP_EMISSIVE_COLOR)
    normal_ws = g.node(u.MaterialExpressionVertexNormalWS); view_ws = g.node(u.MaterialExpressionCameraVectorWS)
    mask = g.custom('return dot(normalize(N), V) < 0.0 ? 1.0 : 0.0;', u.CustomMaterialOutputType.CMOT_FLOAT1, ('N', 'V'))
    g.link(normal_ws, mask, 'N'); g.link(view_ws, mask, 'V')
    assert mel.connect_material_property(mask, '', u.MaterialProperty.MP_OPACITY_MASK)
    finish(m, 'M_HandInside')

for name in ('M_HandDepth', 'M_SkinHand', 'M_GhostHand', 'M_GhostOutline', 'M_HandInside'):
    assert lib.does_asset_exist(f'{MATS}/{name}'), name
u.log('HAPBEAT_DEMO_HAND_MATERIALS_READY')
