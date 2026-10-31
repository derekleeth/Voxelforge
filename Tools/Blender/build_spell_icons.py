"""Renders Voxelforge's action bar spell icons (Roadmap 4.4), the class crests (4.6) and the race crests (4.7).

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


# ---- Roadmap 4.6: class spells and class crests ----

def cube(name, size, location=(0, 0, 0), rotation=(0, 0, 0), scale=(1, 1, 1)):
    return place(primitive(bpy.ops.mesh.primitive_cube_add, name, size=size), location, rotation, scale)


def cylinder(name, radius, depth, location=(0, 0, 0), rotation=(0, 0, 0), vertices=16, scale=(1, 1, 1)):
    obj = primitive(bpy.ops.mesh.primitive_cylinder_add, name, vertices=vertices, radius=radius, depth=depth)
    return place(obj, location, rotation, scale)


def cone(name, radius, depth, location=(0, 0, 0), rotation=(0, 0, 0), vertices=12):
    obj = primitive(bpy.ops.mesh.primitive_cone_add, name, vertices=vertices, radius1=radius, radius2=0.0, depth=depth)
    return place(obj, location, rotation)


def prism(name, points, depth=0.18):
    """A flat shape (x, z points on the screen, anticlockwise) given a little thickness toward the camera."""
    bm = bmesh.new()
    front = [bm.verts.new((x, -depth / 2, z)) for x, z in points]
    back = [bm.verts.new((x, depth / 2, z)) for x, z in points]
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    n = len(points)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((front[i], back[i], back[j], front[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return mesh_object(name, bm)


def earth_blocks(prefix, cells, size=0.42, tilt=(18, 0, -28)):
    """Blocks of glowing-cracked Earthen Rock at (column, row) cells, seen a little from above and to the side."""
    from mathutils import Euler, Vector
    earth = toon("Earthen_Rock", (0.25, 0.15, 0.08), (0.48, 0.32, 0.18), (0.72, 0.52, 0.3))
    crack = toon("Earthen_Crack", (0.85, 0.42, 0.1), (1.0, 0.65, 0.2), (1.0, 0.9, 0.55))
    turn = Euler([math.radians(r) for r in tilt]).to_matrix()
    for i, (col, row) in enumerate(cells):
        at = turn @ Vector((col * size, 0, row * size))
        dress(cube(f"{prefix}{i}", size * 0.96, tuple(at), tilt), earth, 0.03)
        # A glowing crack across each block's front.
        front = at + turn @ Vector((0, -size * 0.5, 0))
        dress(cube(f"{prefix}Crack{i}", size, tuple(front), tilt, (0.75, 0.04, 0.06)), crack, 0.0)


def rune_hammer():
    steel = toon("Hammer_Steel", (0.3, 0.33, 0.4), (0.62, 0.66, 0.74), (0.95, 0.97, 1.0))
    gold = toon("Hammer_Gold", (0.6, 0.38, 0.05), (0.95, 0.72, 0.2), (1.0, 0.95, 0.6))
    wood = toon("Hammer_Wood", (0.25, 0.12, 0.05), (0.45, 0.25, 0.12), (0.65, 0.42, 0.22))
    glow = toon("Hammer_Rune", (0.9, 0.7, 0.2), (1.0, 0.92, 0.5), (1, 1, 1))
    # Leaning right: the handle runs up along (0.57, 0.82) and the head lies across its top end.
    tilt = (0, 35, 0)
    dress(cylinder("Handle", 0.08, 1.5, (-0.2, 0, -0.3), tilt, 8), wood)
    dress(cube("Head", 1, (0.25, 0, 0.36), tilt, (0.95, 0.5, 0.5)), steel)
    for i, x in enumerate((-0.25, 0.25)):
        dress(cube(f"Band{i}", 1, (0.25 + x * 0.82, 0, 0.36 - x * 0.57), tilt, (0.1, 0.56, 0.56)), gold, 0.03)
    dress(cube("Rune", 1, (0.25, -0.26, 0.36), tilt, (0.32, 0.02, 0.22)), glow, 0.02)
    dress(place(sphere("Pommel", 0.12), (-0.62, 0, -0.9)), gold, 0.03)
    # Speed streaks behind it: it has been thrown.
    for i, (z, length) in enumerate(((0.95, 0.6), (0.7, 0.9), (0.45, 0.5))):
        dress(cylinder(f"Streak{i}", 0.035, length, (-0.75 - length * 0.3, -0.2, z), (0, 90, 0), 6), glow, 0.02)
    return (1.0, 0.82, 0.3)


def bulwark_rune():
    gold = toon("Bulwark_Gold", (0.6, 0.38, 0.05), (0.95, 0.72, 0.2), (1.0, 0.95, 0.6))
    steel = toon("Bulwark_Steel", (0.3, 0.33, 0.4), (0.62, 0.66, 0.74), (0.95, 0.97, 1.0))
    glow = toon("Bulwark_Glow", (0.95, 0.75, 0.3), (1.0, 0.93, 0.6), (1, 1, 1))
    # A round shield face on, with a gold rim and a glowing rune on its face.
    dress(cylinder("Shield", 0.95, 0.18, (0, 0, 0), (90, 0, 0), 24), steel)
    torus = primitive(bpy.ops.mesh.primitive_torus_add, "Rim", major_radius=0.95, minor_radius=0.09, major_segments=40,
                      minor_segments=8)
    dress(place(torus, (0, -0.05, 0), (90, 0, 0)), gold, 0.03, smooth=True)
    stem = prism("RuneStem", [(-0.09, -0.6), (0.09, -0.6), (0.09, 0.6), (-0.09, 0.6)])
    dress(place(stem, (0, -0.15, 0)), glow, 0.03)
    for i, (x, z) in enumerate(((-0.3, 0.25), (0.3, 0.25), (-0.3, -0.25), (0.3, -0.25))):
        arm = prism(f"RuneArm{i}", [(-0.25, -0.06), (0.25, -0.06), (0.25, 0.06), (-0.25, 0.06)])
        dress(place(arm, (x * 0.6, -0.15, z), (0, 35 if x * z > 0 else -35, 0)), glow, 0.03)
    return (1.0, 0.8, 0.35)


def hallowed_forge():
    gold = toon("Forge_Gold", (0.6, 0.38, 0.05), (0.95, 0.72, 0.2), (1.0, 0.95, 0.6))
    iron = toon("Forge_Iron", (0.15, 0.15, 0.18), (0.32, 0.32, 0.36), (0.55, 0.55, 0.6))
    flame = toon("Forge_Flame", (0.85, 0.35, 0.05), (1.0, 0.65, 0.15), (1.0, 0.95, 0.55))
    # A golden ring on the ground seen at a slant, an anvil in it and holy flames rising round it.
    torus = primitive(bpy.ops.mesh.primitive_torus_add, "Ring", major_radius=1.0, minor_radius=0.08, major_segments=48,
                      minor_segments=8)
    dress(place(torus, (0, 0, -0.55), (62, 0, 0)), gold, 0.03, smooth=True)
    dress(cube("AnvilTop", 1, (0, 0, -0.08), (0, 0, 0), (0.75, 0.32, 0.18)), iron)
    dress(cube("AnvilFoot", 1, (0, 0, -0.38), (0, 0, 0), (0.35, 0.28, 0.42)), iron)
    dress(cone("AnvilHorn", 0.11, 0.35, (0.5, 0, -0.06), (0, 90, 0)), iron, 0.03)
    for i in range(6):
        a = i * math.pi / 3 + 0.3
        dress(cone(f"Flame{i}", 0.13, 0.55 + 0.15 * (i % 2),
                   (math.cos(a) * 0.95, math.sin(a) * 0.5, -0.4 + math.sin(a) * 0.14 + 0.3)), flame, 0.03, smooth=True)
    return (1.0, 0.65, 0.2)


def stone_spear():
    rock = toon("Spear_Rock", (0.28, 0.17, 0.09), (0.55, 0.38, 0.22), (0.82, 0.68, 0.45))
    crack = toon("Spear_Glow", (0.85, 0.42, 0.1), (1.0, 0.65, 0.2), (1.0, 0.9, 0.55))
    # Pointing up and right along (0.64, 0.77), the point on the shaft's top end.
    tilt = (0, 40, 0)
    dress(cylinder("Shaft", 0.13, 1.6, (-0.2, 0, -0.25), tilt, 6), rock)
    dress(cone("Point", 0.26, 0.85, (0.55, 0, 0.62), tilt, 6), rock)
    dress(cylinder("Glow", 0.14, 0.12, (0.3, 0, 0.33), tilt, 6), crack, 0.02)
    rng = random.Random(8)
    for i, (x, z, s) in enumerate(((-0.75, -0.75, 0.2), (-0.45, -1.0, 0.14), (-1.0, -0.35, 0.16), (0.05, -0.85, 0.11))):
        dress(cube(f"Chip{i}", s, (x, -0.2, z), (rng.uniform(-40, 40), rng.uniform(-40, 40), rng.uniform(-40, 40))),
              rock, 0.025)
    return (0.85, 0.55, 0.25)


def rising_pillar():
    earth_blocks("Pillar", [(0, -2), (0, -1), (0, 0), (0, 1)], size=0.5)
    up = toon("Pillar_Up", (0.85, 0.5, 0.15), (1.0, 0.75, 0.3), (1.0, 0.95, 0.65))
    arrow = prism("Arrow", [(-0.12, -0.3), (0.12, -0.3), (0.12, 0.1), (0.3, 0.1), (0.0, 0.45), (-0.3, 0.1), (-0.12, 0.1)])
    dress(place(arrow, (0.0, -0.4, 0.95)), up, 0.03)
    for i, x in enumerate((-0.75, 0.75)):
        dress(cylinder(f"Rush{i}", 0.04, 0.9, (x, -0.3, -0.1), (0, 0, 0), 6), up, 0.02)
    return (0.95, 0.6, 0.25)


def earthen_wall():
    earth_blocks("Wall", [(c, r) for r in (-1, 0, 1) for c in (-2, -1, 0, 1, 2)], size=0.42, tilt=(14, 0, -24))
    return (0.95, 0.6, 0.25)


def totem_post(prefix):
    wood = toon("Totem_Wood", (0.28, 0.14, 0.06), (0.52, 0.3, 0.14), (0.75, 0.5, 0.28))
    stone = toon("Totem_Stone", (0.3, 0.3, 0.33), (0.52, 0.52, 0.55), (0.75, 0.75, 0.78))
    dress(cube(f"{prefix}Base", 1, (0, 0, -1.0), (0, 0, 0), (0.8, 0.6, 0.18)), stone)
    dress(cube(f"{prefix}Post", 1, (0, 0, -0.35), (0, 0, 0), (0.42, 0.42, 1.2)), wood)


def wellspring_totem():
    totem_post("Well")
    heal = toon("Well_Heal", (0.15, 0.6, 0.25), (0.35, 0.9, 0.45), (0.8, 1.0, 0.8))
    water = toon("Well_Water", (0.1, 0.45, 0.6), (0.3, 0.8, 0.9), (0.85, 1.0, 1.0))
    bowl = toon("Well_Bowl", (0.2, 0.1, 0.05), (0.38, 0.22, 0.1), (0.55, 0.35, 0.18))
    for i, z in enumerate((-0.55, -0.3)):
        dress(cube(f"Band{i}", 1, (0, 0, z), (0, 0, 0), (0.46, 0.46, 0.07)), heal, 0.02)
    dress(cylinder("Bowl", 0.6, 0.28, (0, 0, 0.38), (0, 0, 0), 16), bowl)
    dress(cylinder("Water", 0.55, 0.06, (0, -0.03, 0.54), (14, 0, 0), 16), water, 0.02)
    drop = primitive(bpy.ops.mesh.primitive_uv_sphere_add, "Drop", radius=0.18, segments=16, ring_count=8)
    dress(place(drop, (0, -0.1, 0.95), (0, 0, 0), (1, 1, 1.4)), water, 0.03, smooth=True)
    dress(cone("DropTip", 0.13, 0.22, (0, -0.1, 1.2)), water, 0.03, smooth=True)
    # A green plus floating beside it: it heals.
    for i, (w, h) in enumerate(((0.36, 0.11), (0.11, 0.36))):
        dress(cube(f"Plus{i}", 1, (0.8, -0.3, 0.55), (0, 0, 0), (w, 0.1, h)), heal, 0.025)
    return (0.3, 0.9, 0.7)


def thunderhead_totem():
    totem_post("Thunder")
    wood = toon("Thunder_Dark", (0.18, 0.1, 0.06), (0.32, 0.19, 0.1), (0.48, 0.3, 0.17))
    gold = toon("Thunder_Gold", (0.6, 0.38, 0.05), (0.95, 0.72, 0.2), (1.0, 0.95, 0.6))
    bolt = toon("Thunder_Bolt", (0.85, 0.75, 0.15), (1.0, 0.95, 0.4), (1, 1, 1))
    dress(cube("Head", 1, (0, 0, 0.42), (0, 0, 0), (0.48, 0.42, 0.36)), wood)
    dress(cone("Beak", 0.11, 0.3, (0, -0.32, 0.38), (90, 0, 0), 4), gold, 0.03)
    for sx in (-1, 1):
        wing = prism(f"Wing{sx}", [(0, -0.12), (0.85, 0.15), (0.95, 0.45), (0.55, 0.32), (0, 0.15)])
        dress(place(wing, (sx * 0.18, 0, 0.3), (0, 0, 0), (sx, 1, 1)), wood, 0.03)
    # A lightning bolt striking down beside it.
    zig = prism("Bolt", [(0.15, 1.25), (-0.15, 0.55), (0.05, 0.55), (-0.25, -0.2), (0.25, 0.65), (0.05, 0.65), (0.35, 1.25)])
    dress(place(zig, (0.75, -0.4, -0.3), (0, 0, 0), (0.9, 1, 0.9)), bolt, 0.03)
    return (0.5, 0.7, 1.0)


def crest_adventurer():
    leather = toon("Crest_Pack", (0.3, 0.17, 0.08), (0.55, 0.34, 0.17), (0.78, 0.55, 0.32))
    steel = toon("Crest_Steel", (0.3, 0.33, 0.4), (0.62, 0.66, 0.74), (0.95, 0.97, 1.0))
    gold = toon("Crest_Gold", (0.6, 0.38, 0.05), (0.95, 0.72, 0.2), (1.0, 0.95, 0.6))
    # A traveller's pack with a sword through its straps.
    dress(cube("Pack", 1, (0, 0, -0.2), (0, 0, 0), (0.95, 0.5, 1.05)), leather)
    dress(cube("Flap", 1, (0, -0.2, 0.15), (0, 0, 0), (1.0, 0.2, 0.42)), leather, 0.03)
    dress(cube("Buckle", 1, (0, -0.32, 0.0), (0, 0, 0), (0.18, 0.05, 0.18)), gold, 0.02)
    dress(cube("Blade", 1, (0.1, -0.35, 0.15), (0, -40, 0), (0.16, 0.06, 1.9)), steel, 0.03)
    dress(cube("Guard", 1, (-0.25, -0.38, -0.26), (0, -40, 0), (0.55, 0.1, 0.1)), gold, 0.03)
    return (0.62, 0.5, 0.36)


def crest_paladin():
    rune_hammer()
    return (1.0, 0.78, 0.25)


def crest_geomancer():
    crystal = toon("Crest_Crystal", (0.55, 0.25, 0.05), (0.95, 0.6, 0.2), (1.0, 0.9, 0.55))
    rock = toon("Crest_Rock", (0.28, 0.17, 0.09), (0.55, 0.38, 0.22), (0.82, 0.68, 0.45))
    dress(cube("Rock", 1, (0, 0, -0.75), (10, 0, 15), (1.3, 0.8, 0.55)), rock)
    for i, (x, h, r, tilt) in enumerate(((0, 0.95, 0.32, 0), (-0.5, 0.6, 0.22, 25), (0.48, 0.7, 0.24, -22))):
        gem = dress(bipyramid(f"Crystal{i}", r, h, h * 0.25), crystal)
        place(gem, (x, -0.1, -0.1 + h * 0.3), (0, tilt, 0))
    return (0.88, 0.48, 0.2)


def crest_shaman():
    totem_post("Crest")
    wood = toon("Crest_Face", (0.18, 0.1, 0.06), (0.32, 0.19, 0.1), (0.48, 0.3, 0.17))
    paint = toon("Crest_Paint", (0.05, 0.45, 0.6), (0.2, 0.7, 0.85), (0.7, 0.95, 1.0))
    feather = toon("Crest_Feather", (0.6, 0.3, 0.1), (0.9, 0.55, 0.2), (1.0, 0.85, 0.5))
    dress(cube("Face", 1, (0, 0, 0.45), (0, 0, 0), (0.6, 0.45, 0.55)), wood)
    for sx in (-1, 1):
        dress(cube(f"Eye{sx}", 1, (sx * 0.14, -0.24, 0.52), (0, 0, 0), (0.12, 0.04, 0.08)), paint, 0.02)
        for i, tilt in enumerate((20, 45)):
            plume = cone(f"Feather{sx}{i}", 0.09, 0.6, (sx * (0.35 + 0.12 * i), 0, 0.85 + 0.05 * i), (0, sx * -tilt, 0))
            dress(plume, feather, 0.025, smooth=True)
    dress(cube("Mouth", 1, (0, -0.24, 0.3), (0, 0, 0), (0.3, 0.04, 0.06)), paint, 0.02)
    return (0.2, 0.7, 0.85)


# ---- Roadmap 4.7: racial abilities and race crests ----

def hoofquake():
    hoof = toon("Quake_Hoof", (0.06, 0.05, 0.05), (0.2, 0.17, 0.15), (0.42, 0.37, 0.33))
    fur = toon("Quake_Fur", (0.22, 0.11, 0.04), (0.42, 0.24, 0.11), (0.65, 0.45, 0.26))
    rock = toon("Quake_Rock", (0.28, 0.17, 0.09), (0.55, 0.38, 0.22), (0.82, 0.68, 0.45))
    shock = toon("Quake_Shock", (0.85, 0.6, 0.2), (1.0, 0.85, 0.4), (1.0, 1.0, 0.8))
    # A cloven hoof stamping down out of a shaggy leg, onto a ring of shock with blocks jumping up round it.
    dress(cylinder("Leg", 0.28, 0.9, (0, 0, 0.75), (0, 0, 0), 10), fur)
    dress(cylinder("Fetlock", 0.36, 0.25, (0, 0, 0.3), (0, 0, 0), 10), fur, 0.04)
    for sx in (-1, 1):
        dress(cube(f"Toe{sx}", 1, (sx * 0.19, -0.05, -0.02), (0, 0, sx * -6), (0.34, 0.6, 0.42)), hoof)
    torus = primitive(bpy.ops.mesh.primitive_torus_add, "Shock", major_radius=0.95, minor_radius=0.07, major_segments=40,
                      minor_segments=6)
    dress(place(torus, (0, 0.2, -0.32), (72, 0, 0)), shock, 0.03, smooth=True)
    torus = primitive(bpy.ops.mesh.primitive_torus_add, "Shock2", major_radius=1.22, minor_radius=0.04, major_segments=48,
                      minor_segments=6)
    dress(place(torus, (0, 0.3, -0.36), (72, 0, 0)), shock, 0.025, smooth=True)
    rng = random.Random(13)
    for i, (x, z, s) in enumerate(((-0.95, -0.05, 0.22), (0.98, 0.0, 0.24), (-0.7, 0.35, 0.15), (0.75, 0.4, 0.17),
                                   (-1.1, -0.55, 0.16), (1.1, -0.6, 0.18))):
        dress(cube(f"Block{i}", s, (x, -0.2, z), (rng.uniform(-40, 40), rng.uniform(-40, 40), rng.uniform(-40, 40))),
              rock, 0.03)
    return (0.85, 0.6, 0.3)


def second_wind():
    gust = toon("Wind_Gust", (0.2, 0.6, 0.35), (0.5, 0.9, 0.6), (0.9, 1.0, 0.9))
    leaf = toon("Wind_Leaf", (0.15, 0.35, 0.08), (0.35, 0.65, 0.18), (0.65, 0.9, 0.4))
    heal = toon("Wind_Heal", (0.15, 0.6, 0.25), (0.35, 0.9, 0.45), (0.8, 1.0, 0.8))
    # Three swirling gusts of breath curling round each other, with a leaf caught in them and a heal cross.
    for i, (radius, z, x) in enumerate(((0.7, 0.25, -0.1), (0.5, -0.35, 0.25), (0.35, 0.65, 0.45))):
        torus = primitive(bpy.ops.mesh.primitive_torus_add, f"Gust{i}", major_radius=radius, minor_radius=0.07,
                          major_segments=40, minor_segments=6)
        # Cut away a quarter so each one reads as a curl, not a ring.
        bm = bmesh.new()
        bm.from_mesh(torus.data)
        cut = [v for v in bm.verts if v.co.x > 0 and v.co.y < 0]
        bmesh.ops.delete(bm, geom=cut, context="VERTS")
        bm.to_mesh(torus.data)
        bm.free()
        dress(place(torus, (x, 0, z), (90, 0, 30 * i)), gust, 0.03, smooth=True)
    blade = prism("Leaf", [(0, -0.35), (0.16, -0.12), (0.2, 0.1), (0, 0.38), (-0.2, 0.1), (-0.16, -0.12)])
    dress(place(blade, (-0.7, -0.3, -0.55), (0, 35, 0)), leaf, 0.03)
    for i, (w, h) in enumerate(((0.36, 0.11), (0.11, 0.36))):
        dress(cube(f"Plus{i}", 1, (0.75, -0.3, -0.6), (0, 0, 0), (w, 0.1, h)), heal, 0.025)
    return (0.4, 0.85, 0.5)


def crest_wayfarer():
    wood = toon("Way_Wood", (0.25, 0.12, 0.05), (0.45, 0.25, 0.12), (0.65, 0.42, 0.22))
    brass = toon("Way_Brass", (0.6, 0.38, 0.05), (0.95, 0.72, 0.2), (1.0, 0.95, 0.6))
    flame = toon("Way_Flame", (0.85, 0.45, 0.05), (1.0, 0.75, 0.2), (1.0, 1.0, 0.7))
    # A road signpost with a lantern hanging from it: the way home.
    dress(cube("Post", 1, (-0.35, 0, -0.2), (0, 0, 0), (0.18, 0.18, 2.0)), wood)
    dress(prism("SignA", [(-0.25, 0.55), (0.75, 0.55), (0.95, 0.68), (0.75, 0.81), (-0.25, 0.81)]), wood)
    dress(place(prism("SignB", [(-0.45, 0.2), (0.55, 0.2), (0.55, 0.46), (-0.45, 0.46), (-0.65, 0.33)]), (-0.55, 0, 0)), wood)
    dress(cube("Arm", 1, (0.15, 0, -0.08), (0, 0, 0), (0.8, 0.1, 0.08)), wood, 0.03)
    dress(cylinder("Lantern", 0.17, 0.42, (0.45, 0, -0.45), (0, 0, 0), 6), brass)
    dress(cone("LanternTop", 0.22, 0.2, (0.45, 0, -0.16)), brass, 0.03)
    flame_obj = primitive(bpy.ops.mesh.primitive_uv_sphere_add, "Flame", radius=0.1, segments=12, ring_count=6)
    dress(place(flame_obj, (0.45, -0.18, -0.45), (0, 0, 0), (1, 1, 1.5)), flame, 0.02, smooth=True)
    return (0.32, 0.6, 0.4)


def crest_thunderhoof():
    horn = toon("Hoof_Horn", (0.55, 0.48, 0.32), (0.85, 0.79, 0.62), (1.0, 0.97, 0.88))
    fur = toon("Hoof_Fur", (0.22, 0.11, 0.04), (0.42, 0.24, 0.11), (0.65, 0.45, 0.26))
    bead = toon("Hoof_Bead", (0.03, 0.35, 0.38), (0.1, 0.62, 0.62), (0.6, 0.95, 0.95))
    feather = toon("Hoof_Feather", (0.45, 0.08, 0.03), (0.75, 0.2, 0.08), (1.0, 0.55, 0.35))
    # A round shaman's medallion with great curving horns above it and feathers hanging below.
    dress(cylinder("Disc", 0.62, 0.16, (0, 0, -0.1), (90, 0, 0), 20), fur)
    torus = primitive(bpy.ops.mesh.primitive_torus_add, "Rim", major_radius=0.62, minor_radius=0.06, major_segments=32,
                      minor_segments=6)
    dress(place(torus, (0, -0.05, -0.1), (90, 0, 0)), bead, 0.03, smooth=True)
    for sx in (-1, 1):
        points = [(sx * 0.35, 0.3), (sx * 0.8, 0.45), (sx * 1.05, 0.85), (sx * 0.92, 1.18)]
        for i in range(len(points) - 1):
            (x0, z0), (x1, z1) = points[i], points[i + 1]
            length = math.hypot(x1 - x0, z1 - z0)
            angle = math.degrees(math.atan2(x1 - x0, z1 - z0))
            radius = 0.16 - 0.045 * i
            middle = ((x0 + x1) / 2, 0, (z0 + z1) / 2)
            if i == len(points) - 2:  # The tip comes to a point.
                segment = cone(f"Horn{sx}{i}", radius, length * 1.15, middle, (0, angle, 0), 10)
            else:
                segment = cylinder(f"Horn{sx}{i}", radius, length * 1.1, middle, (0, angle, 0), 10)
            dress(segment, horn, 0.03, smooth=True)
        plume = cone(f"Feather{sx}", 0.11, 0.65, (sx * 0.25, -0.05, -0.95), (0, sx * 12, 180))
        dress(plume, feather, 0.025, smooth=True)
    # A turquoise hoofprint in the middle of the medallion.
    for sx in (-1, 1):
        dress(cube(f"Print{sx}", 1, (sx * 0.12, -0.12, -0.12), (0, sx * -8, 0), (0.16, 0.05, 0.36)), bead, 0.02)
    return (0.72, 0.38, 0.18)


SPELLS = {
    "ManaBolt": mana_bolt,
    "ArcaneOrb": arcane_orb,
    "Pyroblast": pyroblast,
    "EarthShatter": earth_shatter,
    "RuneHammer": rune_hammer,
    "BulwarkRune": bulwark_rune,
    "HallowedForge": hallowed_forge,
    "StoneSpear": stone_spear,
    "RisingPillar": rising_pillar,
    "EarthenWall": earthen_wall,
    "WellspringTotem": wellspring_totem,
    "ThunderheadTotem": thunderhead_totem,
    # Class crests for the class window (Roadmap 4.6), named Class<ClassDefinition asset>.
    "ClassAdventurer": crest_adventurer,
    "ClassRuneforgedPaladin": crest_paladin,
    "ClassGeomancerMage": crest_geomancer,
    "ClassShaman": crest_shaman,
    # Racials and race crests (Roadmap 4.7), the crests named Race<RaceId>.
    "Hoofquake": hoofquake,
    "SecondWind": second_wind,
    "RaceWayfarer": crest_wayfarer,
    "RaceThunderhoof": crest_thunderhoof,
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


def build_all(only=None):
    for name, build in SPELLS.items():
        if only is None or name in only:
            render_icon(name, build)


if __name__ == "__main__":
    build_all()
