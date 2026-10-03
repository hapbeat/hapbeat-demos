"""Creates the plugin content the Demo Session panels load (create-once: existing assets are kept).

Run it in the editor of a demo that has the plugin (Plugins/HapbeatDemoSession) and the Python Editor Script
plugin, headless with the editor closed:

    UnrealEditor-Cmd.exe <demo>.uproject -run=pythonscript -script=<demo>/Plugins/HapbeatDemoSession/Scripts/create_content.py -unattended -nullrhi -NoSound

then copy <demo>/Plugins/HapbeatDemoSession/Content back to unreal/plugins/HapbeatDemoSession/Content.

- Materials/M_HapbeatDemoSessionPanel: the engine's translucent widget material (Widget3DPassThrough, the parent
  of Widget3DPassThrough_Translucent) two-sided and without a depth test, so the panels are never hidden by the scene.
- Materials/M_HapbeatDemoSessionRay: unlit translucent controller ray (drawn after the panels).
- Audio/S_HapbeatDemoSessionClick: the Unity package's DemoSessionClickSound (2.4 kHz, 1 ms attack, 5 ms
  exponential decay, 30 ms, peak 0.4, the last millisecond ramped to silence), 48 kHz mono 16-bit.
"""
import math
import os
import struct
import tempfile
import wave

import unreal as u

ROOT = '/HapbeatDemoSession'
lib = u.EditorAssetLibrary
mel = u.MaterialEditingLibrary
tools = u.AssetToolsHelpers.get_asset_tools()
# The panels sort among the demos' translucent hands (HandSortPriority): same pass as a new material's.
PASS = u.get_default_object(u.Material).get_editor_property('translucency_pass')


def save(asset):
    mel.recompile_material(asset)
    assert lib.save_loaded_asset(asset)


panel_path = f'{ROOT}/Materials/M_HapbeatDemoSessionPanel'
if not lib.does_asset_exist(panel_path):
    m = lib.duplicate_asset('/Engine/EngineMaterials/Widget3DPassThrough', panel_path)
    assert m, 'duplicate failed'
    m.set_editor_property('blend_mode', u.BlendMode.BLEND_TRANSLUCENT)
    m.set_editor_property('two_sided', True)
    m.set_editor_property('disable_depth_test', True)
    m.set_editor_property('translucency_pass', PASS)
    save(m)

ray_path = f'{ROOT}/Materials/M_HapbeatDemoSessionRay'
if not lib.does_asset_exist(ray_path):
    m = tools.create_asset('M_HapbeatDemoSessionRay', f'{ROOT}/Materials', u.Material, u.MaterialFactoryNew())
    m.set_editor_property('shading_model', u.MaterialShadingModel.MSM_UNLIT)
    m.set_editor_property('blend_mode', u.BlendMode.BLEND_TRANSLUCENT)
    m.set_editor_property('translucency_pass', PASS)
    colour = mel.create_material_expression(m, u.MaterialExpressionConstant3Vector)
    # The Unity ray cursor's colour (sRGB 0.6, 0.95, 1.0) in linear.
    colour.set_editor_property('constant', u.LinearColor(.319, .891, 1, 1))
    opacity = mel.create_material_expression(m, u.MaterialExpressionConstant)
    opacity.set_editor_property('r', .9)
    assert mel.connect_material_property(colour, '', u.MaterialProperty.MP_EMISSIVE_COLOR)
    assert mel.connect_material_property(opacity, '', u.MaterialProperty.MP_OPACITY)
    save(m)

click_path = f'{ROOT}/Audio/S_HapbeatDemoSessionClick'
if not lib.does_asset_exist(click_path):
    rate, duration, frequency, attack, decay, peak = 48000, .03, 2400., .001, .005, .4
    count = round(duration * rate)
    frames = bytearray()
    for i in range(count):
        t = i / rate
        envelope = t / attack if t < attack else math.exp(-(t - attack) / decay)
        tail = min(1., max(0., (count - 1 - i) / (attack * rate)))
        frames += struct.pack('<h', round(32767 * peak * envelope * tail * math.sin(2 * math.pi * frequency * t)))
    wav = os.path.join(tempfile.mkdtemp(), 'S_HapbeatDemoSessionClick.wav')
    with wave.open(wav, 'wb') as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(rate)
        f.writeframes(bytes(frames))
    task = u.AssetImportTask()
    task.set_editor_property('filename', wav)
    task.set_editor_property('destination_path', f'{ROOT}/Audio')
    task.set_editor_property('destination_name', 'S_HapbeatDemoSessionClick')
    task.set_editor_property('automated', True)
    task.set_editor_property('save', True)
    tools.import_asset_tasks([task])
    assert lib.does_asset_exist(click_path), 'click import failed'

u.log('HAPBEAT_DEMO_SESSION_CONTENT_CREATED')
