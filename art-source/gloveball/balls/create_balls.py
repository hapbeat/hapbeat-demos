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
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
out = args.output.resolve()
if (out / 'balls.blend').exists():
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
for dx, dz, radius in [(-0.026, 0.035, 0.012), (0.015, 0.046, 0.012), (0.018, -0.003, 0.016)]:
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

floor_mat = material('Preview floor', (0.055, 0.065, 0.085), 0.8)
bpy.ops.mesh.primitive_plane_add(size=200)
bpy.context.object.name = 'PreviewFloor'
bpy.context.object.data.materials.append(floor_mat)
for x, label in [(-0.29, 'BOWLING'), (0, 'VOLLEYBALL'), (0.29, 'FOAM')]:
    bpy.ops.object.text_add(location=(x, -0.17, 0.004))
    obj = bpy.context.object
    obj.name = 'PreviewLabel_' + label
    obj.data.body = label
    obj.data.align_x = 'CENTER'
    obj.data.size = 0.022
    obj.data.materials.append(panel_mats[0])

bpy.ops.object.camera_add(location=(0.42, -1.15, 0.7))
camera = bpy.context.object
camera.rotation_euler = (Vector((0, 0, 0.09)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
camera.data.type = 'ORTHO'
camera.data.ortho_scale = 1.08
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
scene['asset_stage'] = 'Look development; procedural materials need baking before Unity export.'
scene['ball_diameter_m'] = 0.22
bpy.ops.wm.save_as_mainfile(filepath=str(out / 'balls.blend'))
bpy.ops.render.render(write_still=True)
print('BALL_ASSETS_CREATED', out)
