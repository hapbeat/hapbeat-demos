"""Create original ball look-development assets. Run with Blender 5.2 LTS.

blender --background --factory-startup --python create_balls.py
Existing output is never overwritten: use a new --output directory to regenerate.
"""
import argparse
import math
from pathlib import Path
import sys
import bpy
from mathutils import Vector

parser = argparse.ArgumentParser()
parser.add_argument('--output', type=Path, default=Path(__file__).parent / 'generated')
parser.add_argument('--overwrite', action='store_true', help='Explicitly regenerate authored outputs.')
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
out = args.output.resolve()
if (out / 'balls.blend').exists() and not args.overwrite:
    raise RuntimeError('Output exists; choose a new --output directory to preserve manual edits.')
out.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

def material(name, color, roughness):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = (*color, 1)
    bsdf.inputs['Roughness'].default_value = roughness
    return mat

def texture_bump(mat, scale, strength, distance):
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    noise = nodes.new('ShaderNodeTexNoise')
    noise.inputs['Scale'].default_value = scale
    noise.inputs['Detail'].default_value = 2
    bump = nodes.new('ShaderNodeBump')
    bump.inputs['Strength'].default_value = strength
    bump.inputs['Distance'].default_value = distance
    links.new(noise.outputs['Fac'], bump.inputs['Height'])
    links.new(bump.outputs['Normal'], nodes.get('Principled BSDF').inputs['Normal'])

def sphere(name, x, mat, radius=0.11):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=64, ring_count=32, radius=radius, location=(x, 0, 0.12))
    obj = bpy.context.object
    obj.name = name
    obj.data.materials.append(mat)
    for poly in obj.data.polygons:
        poly.use_smooth = True
    return obj

bowling = material('Bowling | polished navy resin', (0.018, 0.035, 0.12), 0.18)
ball = sphere('BowlingBall', -0.29, bowling)
# Three real recessed finger holes, angled toward the comparison camera.
for dx, dz, radius in [(-0.018, 0.038, 0.010), (0.018, 0.038, 0.010), (0, -0.024, 0.013)]:
    direction = Vector((dx, -0.085, dz)).normalized()
    pos = ball.location + direction * 0.097
    bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=radius, depth=0.074, location=pos)
    cutter = bpy.context.object
    cutter.rotation_euler = direction.to_track_quat('Z', 'Y').to_euler()
    modifier = ball.modifiers.new('Finger recess', 'BOOLEAN')
    modifier.operation = 'DIFFERENCE'
    modifier.object = cutter
    bpy.context.view_layer.objects.active = ball
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    bpy.data.objects.remove(cutter, do_unlink=True)
bevel = ball.modifiers.new('Soft hole edges', 'BEVEL')
bevel.width = 0.0008
bevel.segments = 2

seam = material('Volleyball | recessed seams', (0.025, 0.03, 0.045), 0.8)
volley = sphere('VolleyballCore', 0, seam, 0.1088)
panel_mats = [material('Volleyball | ivory', (0.9, 0.88, 0.77), 0.52),
              material('Volleyball | blue', (0.015, 0.16, 0.65), 0.52),
              material('Volleyball | yellow', (0.95, 0.64, 0.025), 0.52)]
for mat in panel_mats:
    texture_bump(mat, 170, 0.22, 0.0003)
# Eighteen curved panels: three strips on each face of a spherified cube.
for axis in range(3):
    for sign in [-1, 1]:
        for strip in range(3):
            verts, faces = [], []
            steps = 14
            for j in range(steps + 1):
                for i in range(steps + 1):
                    u = -1 + 2 * (strip + i / steps) / 3
                    v = -1 + 2 * j / steps
                    u = (-1 + 2 * (strip + 0.5) / 3) + (u - (-1 + 2 * (strip + 0.5) / 3)) * 0.982
                    v *= 0.994
                    p = [0, 0, 0]
                    p[axis] = sign
                    p[(axis + 1) % 3] = u
                    p[(axis + 2) % 3] = v
                    verts.append(tuple(Vector(p).normalized() * 0.11))
            for j in range(steps):
                for i in range(steps):
                    a = j * (steps + 1) + i
                    face = (a, a + 1, a + steps + 2, a + steps + 1)
                    faces.append(face if sign > 0 else tuple(reversed(face)))
            mesh = bpy.data.meshes.new('Curved panel')
            mesh.from_pydata(verts, [], faces)
            mesh.update()
            obj = bpy.data.objects.new(f'VolleyballPanel_{axis}_{sign}_{strip}', mesh)
            bpy.context.collection.objects.link(obj)
            obj.location = volley.location
            obj.data.materials.append(panel_mats[(axis + strip) % 3])
            for poly in mesh.polygons:
                poly.use_smooth = True

foam = material('Foam | matte orange', (0.95, 0.21, 0.035), 0.93)
texture_bump(foam, 85, 0.8, 0.002)
sphere('FoamBall', 0.29, foam)

rubber = material('Basketball | orange pebbled rubber', (0.58, 0.12, 0.018), 0.82)
texture_bump(rubber, 135, 0.45, 0.0008)
sphere('Basketball', 0.58, rubber)
for axis in range(3):
    bpy.ops.mesh.primitive_torus_add(major_radius=0.1091, minor_radius=0.0015,
                                   major_segments=96, minor_segments=6, location=(0.58, 0, 0.12))
    obj = bpy.context.object
    obj.name = 'BasketballSeam'
    obj.rotation_euler[axis] = math.pi / 2
    obj.data.materials.append(seam)

plastic = material('Perforated | lime plastic', (0.55, 0.8, 0.025), 0.36)
perforated = sphere('PerforatedBall', 0.87, plastic)
inner = sphere('Inner cutter', 0.87, plastic, 0.104)
mod = perforated.modifiers.new('Hollow shell', 'BOOLEAN')
mod.operation = 'DIFFERENCE'
mod.object = inner
bpy.context.view_layer.objects.active = perforated
bpy.ops.object.modifier_apply(modifier=mod.name)
bpy.data.objects.remove(inner, do_unlink=True)
for i in range(32):
    z = 1 - 2 * (i + 0.5) / 32
    phi = i * math.pi * (3 - math.sqrt(5))
    direction = Vector((math.sqrt(1-z*z)*math.cos(phi), math.sqrt(1-z*z)*math.sin(phi), z))
    bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.0105, depth=0.04,
                                      location=perforated.location + direction * 0.108)
    cutter = bpy.context.object
    cutter.rotation_euler = direction.to_track_quat('Z', 'Y').to_euler()
    mod = perforated.modifiers.new('Vent hole', 'BOOLEAN')
    mod.operation = 'DIFFERENCE'
    mod.object = cutter
    bpy.context.view_layer.objects.active = perforated
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(cutter, do_unlink=True)

# Export only ball geometry. Bake procedural surface normals; no Blender dependency in Unity.
export_dir = out / 'runtime'
export_dir.mkdir(exist_ok=True)
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 8
for prefix, x in [('Bowling', -0.29), ('Volleyball', 0), ('Foam', 0.29), ('Basketball', 0.58), ('Perforated', 0.87)]:
    objects = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o.name.startswith(prefix)]
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.convert(target='MESH')
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = prefix
    for index, mat in enumerate(obj.data.materials):
        if mat is None:
            obj.data.materials[index] = obj.data.materials[0]
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(island_margin=0.02)
    bpy.ops.object.mode_set(mode='OBJECT')
    normal = bpy.data.images.new(prefix + '_Normal', width=512, height=512)
    normal.colorspace_settings.name = 'Non-Color'
    for mat in obj.data.materials:
        node = mat.node_tree.nodes.new('ShaderNodeTexImage')
        node.image = normal
        mat.node_tree.nodes.active = node
    bpy.ops.object.bake(type='NORMAL', margin=8)
    normal.filepath_raw = str(export_dir / (prefix + '_Normal.png'))
    normal.file_format = 'PNG'
    normal.save()
    normal.pack()
    original = obj.location.copy()
    obj.location = (0, 0, 0)
    bpy.ops.export_scene.fbx(filepath=str(export_dir / (prefix + '.fbx')), use_selection=True,
                             object_types={'MESH'}, bake_anim=False, axis_forward='-Z', axis_up='Y')
    obj.location = original

floor_mat = material('Preview floor', (0.055, 0.065, 0.085), 0.8)
bpy.ops.mesh.primitive_plane_add(size=200)
bpy.context.object.name = 'PreviewFloor'
bpy.context.object.data.materials.append(floor_mat)
for x, label in [(-0.29, 'BOWLING'), (0, 'VOLLEYBALL'), (0.29, 'FOAM'), (0.58, 'BASKETBALL'), (0.87, 'PLASTIC')]:
    bpy.ops.object.text_add(location=(x, -0.17, 0.004))
    obj = bpy.context.object
    obj.name = 'PreviewLabel_' + label
    obj.data.body = label
    obj.data.align_x = 'CENTER'
    obj.data.size = 0.022
    obj.data.materials.append(panel_mats[0])

bpy.ops.object.camera_add(location=(0.48, -1.65, 0.95))
camera = bpy.context.object
camera.rotation_euler = (Vector((0.29, 0, 0.09)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
camera.data.type = 'ORTHO'
camera.data.ortho_scale = 1.62
bpy.context.scene.camera = camera
for loc, power, size in [((-0.3, -0.4, 0.9), 80, 0.7), ((0.5, 0.2, 0.6), 65, 0.5)]:
    bpy.ops.object.light_add(type='AREA', location=loc)
    light = bpy.context.object
    light.data.energy = power
    light.data.shape = 'DISK'
    light.data.size = size
    light.rotation_euler = (Vector((0, 0, 0.1)) - light.location).to_track_quat('-Z', 'Y').to_euler()
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 32
scene.render.resolution_x = 1000
scene.render.resolution_y = 560
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.filepath = str(out / 'comparison.png')
scene.world.color = (0.2, 0.2, 0.2)
scene['asset_stage'] = 'Runtime FBX and baked tangent normals in runtime/.'
scene['ball_diameter_m'] = 0.22
bpy.ops.wm.save_as_mainfile(filepath=str(out / 'balls.blend'))
bpy.ops.render.render(write_still=True)
print('BALL_ASSETS_CREATED', out)
