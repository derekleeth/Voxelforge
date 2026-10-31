"""Renders the race window's portraits (Roadmap 4.7): each race's hero, head and shoulders, three-quarter view.

Run inside Blender (exec over MCP, or the Text Editor) after build_hero.py and build_thunderhoof.py have saved
Hero.blend and Thunderhoof.blend. It empties the open file, so start from a new one. render_all():
  - appends each race's rigged body from its .blend, and lowers the arms out of the T-pose
  - gives it the spell icons' comic ink outline (an inside-out hull) and soft, warm light, and colours the team
    cloth a weathered red (in game it takes each player's colour)
  - renders it with a transparent background to Assets/_Voxelforge/Art/Textures/UI/Portraits/Race<RaceId>.png
Afterwards in Unity: Voxelforge > Build Abilities, which puts them in the HUD skin.
"""
import math
import os

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT_DIR = os.path.join(REPO, "Assets", "_Voxelforge", "Art", "Textures", "UI", "Portraits")
WIDTH, HEIGHT = 512, 272
TEAM_CLOTH = (0.62, 0.22, 0.14)

# Race: (blend file, mesh object, rig object, framing centre z, framing width in metres).
RACES = {
    "Wayfarer": ("Hero.blend", "Hero", "HeroRig", 1.5, 1.5),
    "Thunderhoof": ("Thunderhoof.blend", "Thunderhoof", "ThunderhoofRig", 1.80, 1.7),
}


def _rotate_world(rig, bone_name, axis, degrees):
    pose = rig.pose.bones[bone_name]
    bpy.context.view_layer.update()
    pivot = (rig.matrix_world @ pose.matrix).to_translation()
    turn = Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(degrees), 4, axis) @ Matrix.Translation(-pivot)
    pose.matrix = rig.matrix_world.inverted() @ (turn @ rig.matrix_world @ pose.matrix)
    bpy.context.view_layer.update()


def _ink():
    mat = bpy.data.materials.get("PortraitInk") or bpy.data.materials.new("PortraitInk")
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    nodes.clear()
    emit = nodes.new("ShaderNodeEmission")
    emit.inputs[0].default_value = (0.07, 0.04, 0.05, 1)
    out = nodes.new("ShaderNodeOutputMaterial")
    mat.node_tree.links.new(emit.outputs[0], out.inputs[0])
    mat.use_backface_culling = True
    return mat


def _tint_team_cloth(body):
    """Multiplies the team-cloth slot's texture by TEAM_CLOTH, as Unity does with the player's colour."""
    mat = body.data.materials[1].copy()
    body.data.materials[1] = mat
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    texture = next(n for n in nt.nodes if n.type == "TEX_IMAGE")
    tint = nt.nodes.new("ShaderNodeMix")
    tint.data_type = "RGBA"
    tint.blend_type = "MULTIPLY"
    tint.inputs["Factor"].default_value = 1.0
    tint.inputs[7].default_value = (*TEAM_CLOTH, 1)
    nt.links.new(texture.outputs["Color"], tint.inputs[6])
    nt.links.new(tint.outputs[2], bsdf.inputs["Base Color"])


def _clear():
    """Empties the file. (Reading a new file mid-script leaves this script's context behind, so posing fails.)"""
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for collection in (bpy.data.meshes, bpy.data.armatures, bpy.data.materials, bpy.data.cameras, bpy.data.lights,
                       bpy.data.images):
        for block in list(collection):
            if block.users == 0:
                collection.remove(block)


def _load(race):
    blend, mesh_name, rig_name, _, _ = RACES[race]
    with bpy.data.libraries.load(os.path.join(HERE, blend), link=False) as (src, dst):
        dst.objects = [mesh_name, rig_name]
    for obj in dst.objects:
        bpy.context.scene.collection.objects.link(obj)
    body, rig = dst.objects
    # The skeleton came from Mixamo with an action on it, which would put the bones back when the render evaluates it.
    rig.animation_data_clear()
    bpy.context.view_layer.update()
    _tint_team_cloth(body)
    # Arms down and a little forward, out of the T-pose.
    _rotate_world(rig, "mixamorig:LeftArm", "Y", 68)
    _rotate_world(rig, "mixamorig:RightArm", "Y", -68)
    _rotate_world(rig, "mixamorig:LeftForeArm", "X", -25)
    _rotate_world(rig, "mixamorig:RightForeArm", "X", -25)
    _rotate_world(rig, "mixamorig:Head", "Z", -8)
    hull = body.modifiers.new("Ink", "SOLIDIFY")
    hull.thickness = 0.012
    hull.offset = 1.0
    hull.use_flip_normals = True
    hull.use_rim = False
    hull.material_offset = len(body.data.materials)
    body.data.materials.append(_ink())
    return body, rig


def _setup(centre_z, width):
    scene = bpy.context.scene
    try:
        scene.render.engine = "BLENDER_EEVEE"
    except TypeError:
        pass
    scene.render.resolution_x, scene.render.resolution_y = WIDTH, HEIGHT
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = True
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    world = scene.world or bpy.data.worlds.new("PortraitWorld")
    scene.world = world
    world.use_nodes = True
    background = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
    background.inputs[0].default_value = (0.55, 0.5, 0.45, 1)
    background.inputs[1].default_value = 0.6

    cam_data = bpy.data.cameras.new("PortraitCamera")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = width
    cam = bpy.data.objects.new("PortraitCamera", cam_data)
    scene.collection.objects.link(cam)
    # Three-quarter view from the hero's front right, a touch above.
    direction = Vector((0.45, -1.0, 0.12)).normalized()
    cam.location = Vector((0, 0, centre_z)) + direction * 8
    cam.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam

    key = bpy.data.objects.new("PortraitKey", bpy.data.lights.new("PortraitKey", "SUN"))
    key.data.energy = 3.2
    key.data.color = (1.0, 0.93, 0.82)
    key.rotation_euler = (math.radians(50), math.radians(-30), math.radians(-35))
    scene.collection.objects.link(key)
    rim = bpy.data.objects.new("PortraitRim", bpy.data.lights.new("PortraitRim", "SUN"))
    rim.data.energy = 1.5
    rim.data.color = (0.75, 0.85, 1.0)
    rim.rotation_euler = (math.radians(60), math.radians(40), math.radians(150))
    scene.collection.objects.link(rim)


def render(race):
    _clear()
    _load(race)
    _, _, _, centre_z, width = RACES[race]
    _setup(centre_z, width)
    os.makedirs(OUT_DIR, exist_ok=True)
    scene = bpy.context.scene
    scene.render.filepath = os.path.join(OUT_DIR, f"Race{race}.png")
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    bpy.ops.render.render(write_still=True)
    print("wrote", scene.render.filepath)


def render_all():
    for race in RACES:
        render(race)


if __name__ == "__main__":
    render_all()
