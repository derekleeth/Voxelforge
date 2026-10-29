"""Renders Voxelforge's action bar spell icons (Roadmap 4.4).

Run inside Tools/Blender/SpellIcons.blend (Text Editor, or exec over MCP). build_all() rebuilds every icon:
  - models each spell as a bold, flat-shaded toy (three-tone toon shading) with an inverted-hull ink outline
  - renders it with a transparent background through an orthographic camera
  - lays it over comic "action rays" with halftone dots in the spell's element colour
  - writes Assets/_Voxelforge/Art/Textures/UI/Icons/<Ability>.png (256 x 256)

The PNG names match the AbilityDefinition asset names, so Unity's Voxelforge > Build Abilities hooks them up.
Afterwards in Unity: Voxelforge > Build Abilities.
"""
import math
import os
import random

import bmesh
import bpy
import numpy as np

REPO = os.path.abspath(os.path.join(os.path.dirname(bpy.data.filepath or __file__), "..", ".."))
ICON_DIR = os.path.join(REPO, "Assets", "_Voxelforge", "Art", "Textures", "UI", "Icons")
SIZE = 256
INK = (0.07, 0.04, 0.05)


# ---- Materials ----

def toon(name, shadow, mid, light):
    """Three flat tones from the light, like an inked and coloured comic panel."""
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    nodes.clear()
    diffuse = nodes.new("ShaderNodeBsdfDiffuse")
    to_rgb = nodes.new("ShaderNodeShaderToRGB")
    ramp = nodes.new("ShaderNodeValToRGB")
    emit = nodes.new("ShaderNodeEmission")
    out = nodes.new("ShaderNodeOutputMaterial")
    ramp.color_ramp.interpolation = "CONSTANT"
    stops = ramp.color_ramp.elements
    stops[0].position, stops[0].color = 0.0, (*shadow, 1)
    stops[1].position, stops[1].color = 0.18, (*mid, 1)
    stops.new(0.6).color = (*light, 1)
    links.new(diffuse.outputs[0], to_rgb.inputs[0])
    links.new(to_rgb.outputs[0], ramp.inputs[0])
    links.new(ramp.outputs[0], emit.inputs[0])
    links.new(emit.outputs[0], out.inputs[0])
    return mat


def ink():
    mat = bpy.data.materials.get("Ink") or bpy.data.materials.new("Ink")
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    nodes.clear()
    emit = nodes.new("ShaderNodeEmission")
    emit.inputs[0].default_value = (*INK, 1)
    out = nodes.new("ShaderNodeOutputMaterial")
    mat.node_tree.links.new(emit.outputs[0], out.inputs[0])
    mat.use_backface_culling = True
    return mat


def dress(obj, mat, outline=0.07, smooth=False):
    """Gives obj its toon material and an ink outline: an inflated, inside-out copy that only shows its back."""
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    obj.data.materials.append(ink())
    for poly in obj.data.polygons:
        poly.use_smooth = smooth
    hull = obj.modifiers.new("Ink", "SOLIDIFY")
    hull.thickness = outline
    hull.offset = 1.0
    hull.use_flip_normals = True
    hull.use_rim = False
    hull.material_offset = 1
    return obj


# ---- Shapes ----

def mesh_object(name, bm):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def bipyramid(name, radius, half_height, ring_height, sides=6):
    """A crystal: a ring of `sides` points with a point above and below."""
    bm = bmesh.new()
    top = bm.verts.new((0, 0, half_height))
    bottom = bm.verts.new((0, 0, -half_height))
    upper = [bm.verts.new((radius * math.cos(a), radius * math.sin(a), ring_height))
             for a in (i * 2 * math.pi / sides for i in range(sides))]
    lower = [bm.verts.new((v.co.x * 0.9, v.co.y * 0.9, -ring_height)) for v in upper]
    for i in range(sides):
        j = (i + 1) % sides
        bm.faces.new((upper[i], upper[j], top))
        bm.faces.new((upper[j], upper[i], lower[i], lower[j]))
        bm.faces.new((lower[j], lower[i], bottom))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return mesh_object(name, bm)


def primitive(op, name, **kwargs):
    op(**kwargs)
    obj = bpy.context.view_layer.objects.active
    obj.name = name
    return obj


def sphere(name, radius, location=(0, 0, 0), segments=32):
    return primitive(bpy.ops.mesh.primitive_uv_sphere_add, name, radius=radius, location=location,
                     segments=segments, ring_count=segments // 2)


def place(obj, location=(0, 0, 0), rotation=(0, 0, 0), scale=(1, 1, 1)):
    obj.location = location
    obj.rotation_euler = [math.radians(r) for r in rotation]
    obj.scale = scale
    return obj


# ---- Spells (camera looks along +Y: x is right, z is up on screen) ----

def mana_bolt():
    crystal = toon("ManaBolt_Crystal", (0.04, 0.25, 0.55), (0.15, 0.7, 1.0), (0.8, 1.0, 1.0))
    streak = toon("ManaBolt_Streak", (0.3, 0.7, 0.95), (0.75, 0.95, 1.0), (1, 1, 1))
    gem = dress(bipyramid("Bolt", 0.32, 0.95, 0.25), crystal)
    place(gem, (0.15, 0, 0.15), (0, 45, 20))
    for i, (offset, length) in enumerate(((-0.28, 0.75), (0.0, 1.0), (0.28, 0.6))):
        line = dress(primitive(bpy.ops.mesh.primitive_cylinder_add, f"Streak{i}", vertices=8, radius=0.05, depth=length), streak, 0.03)
        # Trailing down-left of the bolt, along its flight.
        along, across = -0.55 - length * 0.5, offset
        place(line, (0.15 + along * 0.707 + across * 0.707, 0.3, 0.15 + along * 0.707 - across * 0.707), (0, 45, 0))
    return (0.35, 0.85, 1.0)


def arcane_orb():
    orb = toon("ArcaneOrb_Orb", (0.25, 0.06, 0.45), (0.6, 0.25, 0.95), (0.95, 0.75, 1.0))
    ring = toon("ArcaneOrb_Ring", (0.5, 0.25, 0.75), (0.9, 0.7, 1.0), (1, 1, 1))
    dress(sphere("Orb", 0.58), orb, smooth=True)
    for i, tilt in enumerate((25, -35)):
        torus = primitive(bpy.ops.mesh.primitive_torus_add, f"Ring{i}", major_radius=0.92, minor_radius=0.045,
                          major_segments=48, minor_segments=8)
        dress(torus, ring, 0.03, smooth=True)
        place(torus, rotation=(18, tilt, 0))
    for i, (x, z, s) in enumerate(((-0.75, 0.7, 0.12), (0.8, -0.55, 0.1), (0.7, 0.75, 0.07))):
        spark = primitive(bpy.ops.mesh.primitive_ico_sphere_add, f"Spark{i}", subdivisions=1, radius=s)
        dress(spark, ring, 0.025)
        place(spark, (x, -0.3, z), (30, 45, 0))
    return (0.8, 0.4, 1.0)


def pyroblast():
    core = toon("Pyro_Core", (0.75, 0.2, 0.02), (1.0, 0.55, 0.08), (1.0, 0.95, 0.5))
    flame = toon("Pyro_Flame", (0.55, 0.06, 0.02), (0.95, 0.3, 0.05), (1.0, 0.7, 0.2))
    dress(place(sphere("Fireball", 0.52), (0.28, 0, 0.28)), core, smooth=True)
    # Flame tongues streaming down-left behind it.
    for i, (spread, length, radius) in enumerate(((-28, 1.15, 0.36), (0, 1.4, 0.42), (28, 1.05, 0.34), (-12, 0.8, 0.25))):
        cone = primitive(bpy.ops.mesh.primitive_cone_add, f"Flame{i}", vertices=12, radius1=radius, radius2=0.0, depth=length)
        dress(cone, flame, 0.035, smooth=True)
        angle = math.radians(225 + spread)
        d = 0.35 + length * 0.5
        # The cone's tip points away from the ball, along its tilt.
        place(cone, (0.28 + d * math.cos(angle), 0.2 + i * 0.05, 0.28 + d * math.sin(angle)), (0, 90 - math.degrees(angle), 0))
    for i, (x, z) in enumerate(((0.85, -0.2), (-0.1, 0.85))):
        ember = primitive(bpy.ops.mesh.primitive_ico_sphere_add, f"Ember{i}", subdivisions=1, radius=0.09)
        dress(place(ember, (x, -0.3, z)), core, 0.025)
    return (1.0, 0.45, 0.1)


def earth_shatter():
    rock = toon("Earth_Rock", (0.28, 0.17, 0.09), (0.55, 0.38, 0.22), (0.82, 0.68, 0.45))
    turf = toon("Earth_Turf", (0.15, 0.3, 0.08), (0.35, 0.6, 0.18), (0.6, 0.85, 0.35))
    dirt = toon("Earth_Dirt", (0.2, 0.11, 0.05), (0.36, 0.22, 0.11), (0.5, 0.33, 0.17))
    # A grass-topped block of ground, tipped toward us so the top shows, split down the middle.
    for side, x in (("L", -0.68), ("R", 0.68)):
        soil = primitive(bpy.ops.mesh.primitive_cube_add, f"Soil{side}", size=1)
        dress(place(soil, (x, 0.5, -0.92), (32, 0, 8 if side == "L" else -8), (1.15, 1.3, 0.38)), dirt)
        grass = primitive(bpy.ops.mesh.primitive_cube_add, f"Grass{side}", size=1)
        dress(place(grass, (x, 0.42, -0.7), (32, 0, 8 if side == "L" else -8), (1.15, 1.3, 0.1)), turf)
    rng = random.Random(4)
    # Blocky chunks bursting up out of the ground: it's a voxel world.
    for i, (x, z, s) in enumerate(((0, 0.1, 0.42), (-0.62, -0.15, 0.3), (0.6, -0.1, 0.32), (-0.35, 0.68, 0.22),
                                   (0.4, 0.62, 0.26), (0.02, 0.92, 0.16), (-0.85, 0.35, 0.14), (0.88, 0.38, 0.15))):
        chunk = primitive(bpy.ops.mesh.primitive_cube_add, f"Chunk{i}", size=s)
        dress(chunk, rock, 0.035)
        place(chunk, (x, -0.1, z), (rng.uniform(-40, 40), rng.uniform(-40, 40), rng.uniform(-40, 40)))
    return (0.85, 0.6, 0.3)


SPELLS = {
    "ManaBolt": mana_bolt,
    "ArcaneOrb": arcane_orb,
    "Pyroblast": pyroblast,
    "EarthShatter": earth_shatter,
}


# ---- Scene, render and the comic backdrop ----

def clear_objects():
    for obj in list(bpy.context.scene.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for me in list(bpy.data.meshes):
        if me.users == 0:
            bpy.data.meshes.remove(me)


def setup_scene():
    scene = bpy.context.scene
    try:
        scene.render.engine = "BLENDER_EEVEE"
    except TypeError:
        pass
    scene.render.resolution_x = scene.render.resolution_y = SIZE
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = True
    scene.render.filter_size = 0.8
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    if hasattr(scene, "eevee"):
        scene.eevee.taa_render_samples = 16

    cam_data = bpy.data.cameras.get("IconCamera") or bpy.data.cameras.new("IconCamera")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 2.7
    cam = bpy.data.objects.new("IconCamera", cam_data)
    scene.collection.objects.link(cam)
    cam.location = (0, -10, 0)
    cam.rotation_euler = (math.radians(90), 0, 0)
    scene.camera = cam

    sun_data = bpy.data.lights.get("IconSun") or bpy.data.lights.new("IconSun", "SUN")
    sun_data.energy = 3.0
    # No shadows: they speckle the flat toon tones.
    sun_data.use_shadow = False
    sun = bpy.data.objects.new("IconSun", sun_data)
    scene.collection.objects.link(sun)
    # From the top left, a little in front.
    sun.rotation_euler = (math.radians(40), math.radians(-35), math.radians(-20))


def backdrop(color):
    """Comic action rays from the middle with halftone dots, in the element's colour (rows bottom-up, RGBA 0-1)."""
    ys, xs = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32) + 0.5
    cx = cy = SIZE * 0.5
    dx, dy = xs - cx, ys - cy
    r = np.sqrt(dx * dx + dy * dy) / (SIZE * 0.5)
    angle = np.arctan2(dy, dx)
    rays = (np.floor((angle + math.pi) / (2 * math.pi) * 18) % 2).astype(np.float32)
    base = np.array(color, np.float32)
    dark = base * 0.35
    mid = base * 0.6
    img = np.empty((SIZE, SIZE, 4), np.float32)
    img[..., :3] = dark + (mid - dark) * (rays * 0.6 + (1 - np.clip(r, 0, 1)) * 0.6)[..., None]
    # Halftone dots on a 45 degree grid, biggest toward the top left.
    spacing = 9.0
    u = (xs + ys) * 0.7071 / spacing
    v = (xs - ys) * 0.7071 / spacing
    dist = np.sqrt((u - np.round(u)) ** 2 + (v - np.round(v)) ** 2) * spacing
    shade = np.clip(0.85 - np.sqrt(((xs - SIZE * 0.3) / SIZE) ** 2 + ((ys - SIZE * 0.72) / SIZE) ** 2) * 1.6, 0, 1)
    dots = np.clip(np.sqrt(shade) * spacing * 0.5 - dist + 0.5, 0, 1)
    img[..., :3] += (np.minimum(base * 1.25, 1) - img[..., :3]) * (dots * 0.55)[..., None]
    img[..., 3] = 1.0
    return img


def render_icon(name, build):
    clear_objects()
    setup_scene()
    color = build()
    scene = bpy.context.scene
    tmp = os.path.join(bpy.app.tempdir, f"icon_{name}.png")
    scene.render.filepath = tmp
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    bpy.ops.render.render(write_still=True)

    shot = bpy.data.images.load(tmp, check_existing=False)
    fg = np.array(shot.pixels[:], np.float32).reshape(SIZE, SIZE, 4)
    bpy.data.images.remove(shot)
    bg = backdrop(color)
    a = fg[..., 3:4]
    out = bg.copy()
    out[..., :3] = fg[..., :3] * a + bg[..., :3] * (1 - a)

    os.makedirs(ICON_DIR, exist_ok=True)
    path = os.path.join(ICON_DIR, f"{name}.png")
    image = bpy.data.images.get("IconOut") or bpy.data.images.new("IconOut", SIZE, SIZE, alpha=True)
    image.pixels[:] = out.ravel()
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    print("wrote", path)


def build_all():
    for name, build in SPELLS.items():
        render_icon(name, build)


if __name__ == "__main__":
    build_all()
