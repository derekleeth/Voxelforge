"""Builds Voxelforge's loot (Roadmap 5.5): the mob materials (hides, a pelt, tusks, fangs, a horn, crystal shards and
four elemental essences) and stand-in models for the first gear (caps, hoods, a crown, jerkins, a hauberk, leggings,
greaves, boots, a cudgel, a wand, a blade, the Legendary maul Skyrender, a buckler and a shield). 5.9 replaces the gear
art; these only need to read clearly in a bag icon, in the hand and, from 5.5 step E, on the hero.

Run inside Blender (Text Editor, or exec over MCP) from Tools/Blender/Loot.blend. build_all() rebuilds everything:
  - paints one 1024 x 1024 atlas into Art/Textures/Loot/Loot_Atlas.png, plus a glow map (Loot_Glow.png) that is black
    except for crystals, essences and the Legendary's molten runes
  - builds every model, laid out in a row to look at
  - exports one FBX per model into Art/Models/Loot/
LootBuilder.cs (Voxelforge > Build Loot) turns them into items.

Mesh contract: as build_props.py (1 unit = 1 m, Z up here, Blender +Y is Unity +Z), plus
  - weapons: origin where the hand grips, handle along +Z, as the tools
  - materials and armour: origin at the bottom centre; armour faces +Y (Unity +Z, the way a hero faces)
Shapes and the mesh builder are build_props.py's; only the atlas is this file's own.
"""
import importlib
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(bpy.data.filepath or __file__))
import build_props as P  # noqa: E402

importlib.reload(P)

ART = P.ART
ATLAS_PATH = os.path.join(ART, "Textures", "Loot", "Loot_Atlas.png")
GLOW_PATH = os.path.join(ART, "Textures", "Loot", "Loot_Glow.png")
MODEL_DIR = os.path.join(ART, "Models", "Loot")

SIZE, CELL = P.SIZE, P.CELL

GRID = {
    "hide_boar": (0, 0), "fur": (1, 0), "hide_rhino": (2, 0), "bone": (3, 0),
    "crystal": (0, 1), "earth": (1, 1), "frost": (2, 1), "storm": (3, 1),
    "arcane": (0, 2), "wood": (1, 2), "iron": (2, 2), "leather": (3, 2),
    "rare_steel": (0, 3), "epic_metal": (1, 3), "gold": (2, 3), "molten": (3, 3),
}


def region(name):
    col, row = GRID[name]
    return col * 0.25, 1.0 - (row + 1) * 0.25, (col + 1) * 0.25, 1.0 - row * 0.25


rgb = P.rgb


# ----------------------------------------------------------------------------------------------------------------
# Painting
# ----------------------------------------------------------------------------------------------------------------

def paint_hide(rng, base, dark, bristle):
    """Tanned hide: blotchy, with short dark bristle strokes."""
    n = P.noise(CELL, 5, rng, 3)
    hairs = P.noise(CELL, 48, rng, 1)
    colour = P.tint(base, 0.75 + 0.45 * n)
    strokes = np.clip((hairs - 0.6) * 5, 0, 1)[..., None] * bristle
    colour = colour * (1 - strokes) + dark[None, None, :] * strokes
    return colour, np.zeros_like(colour)


def paint_fur(rng):
    """Pale wolf fur: long streaks along the grain."""
    x, y = P.grid(CELL)
    streaks = P.noise(CELL, 40, rng, 1)[:, :1].repeat(CELL, axis=1).T
    n = P.noise(CELL, 4, rng)
    colour = P.tint(rgb(214, 220, 228), 0.7 + 0.25 * n + 0.25 * (streaks - 0.5))
    tips = np.clip((np.sin(y * 18 + n * 6) - 0.6) * 2, 0, 1)[..., None]
    colour = colour * (1 - 0.35 * tips) + rgb(120, 132, 150)[None, None, :] * 0.35 * tips
    return colour, np.zeros_like(colour)


def paint_bone(rng):
    x, y = P.grid(CELL)
    n = P.noise(CELL, 4, rng)
    colour = P.tint(rgb(236, 224, 196), 0.8 + 0.25 * n)
    colour *= (0.85 + 0.15 * y)[..., None]  # Darker at the root.
    return colour, np.zeros_like(colour)


def paint_glow(rng, inner, outer, glow=0.9):
    """A crystal or essence: two colours swirled together, with sparkles; it glows."""
    n = P.noise(CELL, 3, rng, 2)
    facets = P.noise(CELL, 8, rng, 1)
    k = np.clip(n * 1.4 - 0.2, 0, 1)[..., None]
    colour = outer * (1 - k) + inner * k
    colour = colour * (0.75 + 0.5 * facets[..., None])
    sparkle = np.clip((P.noise(CELL, 32, rng, 1) - 0.8) * 8, 0, 1)[..., None]
    colour = colour * (1 - sparkle) + sparkle
    return colour, colour * glow


def paint_molten(rng):
    """Skyrender's head: dark storm iron cracked with glowing orange-gold veins."""
    n = P.noise(CELL, 6, rng, 4)
    veins = np.abs(P.noise(CELL, 5, rng, 3) - 0.5)
    crack = np.clip((0.06 - veins) * 22, 0, 1)[..., None]
    rock = P.tint(rgb(54, 50, 62), 0.7 + 0.5 * n)
    hot = rgb(255, 170, 40)
    colour = rock * (1 - crack) + hot[None, None, :] * crack
    return colour, hot[None, None, :] * crack


def paint_atlas(seed=55):
    rng = np.random.default_rng(seed)
    painters = {
        "hide_boar": lambda: paint_hide(rng, rgb(150, 98, 60), rgb(70, 44, 28), 0.7),
        "fur": lambda: paint_fur(rng),
        "hide_rhino": lambda: paint_hide(rng, rgb(124, 122, 130), rgb(70, 66, 74), 0.25),
        "bone": lambda: paint_bone(rng),
        "crystal": lambda: paint_glow(rng, rgb(120, 240, 255), rgb(150, 70, 245)),
        "earth": lambda: paint_glow(rng, rgb(190, 255, 120), rgb(70, 140, 40)),
        "frost": lambda: paint_glow(rng, rgb(235, 250, 255), rgb(80, 170, 255)),
        "storm": lambda: paint_glow(rng, rgb(255, 245, 140), rgb(110, 90, 255)),
        "arcane": lambda: paint_glow(rng, rgb(255, 150, 255), rgb(110, 40, 200)),
        "wood": lambda: P.paint_wood(rng, rgb(150, 104, 62), rgb(96, 62, 36)),
        "iron": lambda: P.paint_metal(rng, rgb(92, 96, 108)),
        "leather": lambda: P.paint_wrap(rng, rgb(116, 72, 44), rgb(62, 38, 22)),
        "rare_steel": lambda: P.paint_metal(rng, rgb(110, 160, 230), 0.35),
        "epic_metal": lambda: P.paint_metal(rng, rgb(120, 70, 180), 0.35),
        "gold": lambda: P.paint_metal(rng, rgb(235, 182, 70), 0.3),
        "molten": lambda: paint_molten(rng),
    }
    albedo = np.zeros((SIZE, SIZE, 4), np.float32)
    glow = np.zeros((SIZE, SIZE, 4), np.float32)
    albedo[..., 3] = 1.0
    glow[..., 3] = 1.0
    for name, paint in painters.items():
        colour, light = paint()
        u0, v0, _, _ = region(name)
        r0, c0 = int(round(v0 * SIZE)), int(round(u0 * SIZE))
        albedo[r0:r0 + CELL, c0:c0 + CELL, :3] = np.clip(colour, 0, 1)
        glow[r0:r0 + CELL, c0:c0 + CELL, :3] = np.clip(light, 0, 1)
    return albedo, glow


# ----------------------------------------------------------------------------------------------------------------
# Models
# ----------------------------------------------------------------------------------------------------------------

def builder():
    return P.MeshBuilder(region)


def ball(c, r, sides=8):
    """A chunky faceted ball: two cones on a band."""
    c = Vector(c)
    return P.convex(P.cylinder(c - Vector((0, 0, r * 0.45)), c + Vector((0, 0, r * 0.45)), r, r, sides, caps=False) +
                    P.cylinder(c + Vector((0, 0, r * 0.45)), c + Vector((0, 0, r)), r, 0.0, sides, caps=False) +
                    P.cylinder(c - Vector((0, 0, r * 0.45)), c - Vector((0, 0, r)), r, 0.0, sides, caps=False))


def horn(b, region_name, base, direction, length, radius, curl=0.35, segments=4):
    """A curved, tapering horn, tusk or fang from base along direction, bending up."""
    p = Vector(base)
    d = Vector(direction).normalized()
    for i in range(segments):
        t0, t1 = i / segments, (i + 1) / segments
        step = d * (length / segments)
        q = p + step
        b.part(region_name, P.cylinder(p, q, radius * (1 - t0), radius * (1 - t1) + 0.002, 6))
        d = (d + Vector((0, 0, curl))).normalized()
        p = q


def hide(region_name, size=0.5):
    """A stretched hide lying flat: a wavy outline, thin."""
    b = builder()
    outline = []
    for k in range(10):
        a = k * 2 * math.pi / 10
        r = size * (0.42 + 0.08 * math.sin(a * 3) + (0.08 if k % 5 == 2 else 0))
        outline.append((math.cos(a) * r, math.sin(a) * r * 0.8))
    b.part(region_name, P.slab(outline, (0, 0, 0.03), Vector((1, 0, 0)), Vector((0, 1, 0)), lambda i: 0.05))
    # Rolled up on one side, so it reads as a hide and not a rug.
    b.part(region_name, P.cylinder((-size * 0.3, -size * 0.3, 0.07), (-size * 0.3, size * 0.3, 0.07), 0.06, 0.06, 6))
    return b


def build_hide_boar():
    return hide("hide_boar")


def build_hide_rhino():
    return hide("hide_rhino", 0.58)


def build_pelt():
    b = hide("fur", 0.55)
    b.part("fur", P.box((0.24, 0, 0.09), (0.12, 0.14, 0.09)))  # The head end.
    return b


def build_tusk():
    b = builder()
    horn(b, "bone", (0, 0, 0.04), (1, 0, 0.2), 0.42, 0.06, curl=0.45)
    return b


def build_fang():
    b = builder()
    horn(b, "bone", (0, 0, 0.0), (0.2, 0, 1), 0.3, 0.05, curl=0.15, segments=3)
    b.part("leather", P.cylinder((0, 0, -0.01), (0, 0, 0.05), 0.06, 0.06, 6))  # A cord round the root.
    return b


def build_horn():
    b = builder()
    horn(b, "bone", (0, 0, 0.06), (1, 0, 0.6), 0.5, 0.1, curl=0.3, segments=5)
    b.part("storm", P.gem((0.0, 0, 0.06), (0.08, 0.08, 0.05)))  # A storm-blue glint where it broke off.
    return b


def build_shard():
    b = builder()
    b.part("crystal", P.gem((0, 0, 0.22), (0.08, 0.07, 0.22)))
    b.part("crystal", P.gem((0.09, 0.03, 0.12), (0.05, 0.05, 0.13), Vector((0.5, 0.2, 1))))
    b.part("crystal", P.gem((-0.08, -0.03, 0.1), (0.045, 0.045, 0.1), Vector((-0.5, -0.1, 1))))
    return b


def essence(region_name):
    """A glowing orb of an element in a little gold cage."""
    b = builder()
    b.part(region_name, ball((0, 0, 0.17), 0.13))
    for k in range(4):
        a = k * math.pi / 2
        b.part("gold", P.box((math.cos(a) * 0.13, math.sin(a) * 0.13, 0.17), (0.03, 0.03, 0.3)))
    b.part("gold", P.cylinder((0, 0, 0.0), (0, 0, 0.04), 0.12, 0.1, 8))
    b.part("gold", P.cylinder((0, 0, 0.31), (0, 0, 0.35), 0.1, 0.06, 8))
    return b


def build_essence_earth():
    return essence("earth")


def build_essence_frost():
    return essence("frost")


def build_essence_storm():
    return essence("storm")


def build_essence_arcane():
    return essence("arcane")


# ---- Head: origin at the bottom of the head piece's opening ----

def build_cap():
    b = builder()
    b.part("hide_boar", P.cylinder((0, 0, 0), (0, 0, 0.16), 0.17, 0.12, 8))
    b.part("hide_boar", P.cylinder((0, 0, 0.16), (0, 0, 0.22), 0.12, 0.0, 8))
    b.part("leather", P.cylinder((0, 0, 0), (0, 0, 0.04), 0.185, 0.185, 8))  # Band.
    return b


def build_hood():
    b = builder()
    b.part("fur", P.cylinder((0, -0.02, 0), (0, -0.05, 0.24), 0.19, 0.08, 8))
    b.part("fur", P.cylinder((0, -0.05, 0.24), (0, -0.1, 0.32), 0.08, 0.0, 8))
    for sx in (-1, 1):  # Wolf ears.
        b.part("fur", P.cylinder((sx * 0.09, -0.02, 0.2), (sx * 0.12, -0.02, 0.33), 0.05, 0.0, 4))
    horn(b, "bone", (0.0, 0.17, 0.03), (0, 1, -0.3), 0.08, 0.025, curl=-0.2, segments=2)  # A fang at the brow.
    return b


def build_crown():
    b = builder()
    b.part("gold", P.cylinder((0, 0, 0), (0, 0, 0.08), 0.17, 0.17, 10, phase=math.pi / 10))
    for k in range(5):
        a = k * 2 * math.pi / 5 + math.pi / 2
        b.part("crystal", P.gem((math.cos(a) * 0.16, math.sin(a) * 0.16, 0.15), (0.035, 0.035, 0.09)))
    b.part("epic_metal", P.cylinder((0, 0, 0.08), (0, 0, 0.11), 0.18, 0.18, 10))
    return b


# ---- Chest: origin at the bottom middle of the torso ----

def torso(b, region_name, width=0.44, depth=0.26, height=0.5):
    b.part(region_name, P.box((0, 0, height / 2), (width, depth, height)))
    b.part(region_name, P.box((0, 0, height + 0.03), (width * 0.55, depth * 0.8, 0.06)))  # Collar.


def build_jerkin():
    b = builder()
    torso(b, "hide_boar")
    b.part("leather", P.box((0, 0, 0.1), (0.46, 0.28, 0.06)))  # Belt.
    b.part("iron", P.box((0, 0.145, 0.1), (0.07, 0.01, 0.05)))  # Buckle.
    return b


def build_jerkin_tusk():
    b = builder()
    torso(b, "hide_boar")
    b.part("leather", P.box((0, 0, 0.1), (0.46, 0.28, 0.06)))
    for sx in (-1, 1):
        b.part("leather", P.box((sx * 0.25, 0, 0.47), (0.14, 0.3, 0.1)))  # Shoulder pads,
        horn(b, "bone", (sx * 0.27, 0.05, 0.52), (sx * 0.5, 0.3, 0.6), 0.16, 0.035)  # each with a tusk.
    return b


def build_hauberk():
    b = builder()
    torso(b, "epic_metal", 0.46, 0.28, 0.52)
    for z in (0.14, 0.28, 0.42):  # Plates.
        b.part("rare_steel", P.box((0, 0.145, z), (0.4, 0.02, 0.08)))
    b.part("storm", P.gem((0, 0.16, 0.36), (0.06, 0.02, 0.07), Vector((0, 1, 0))))
    for sx in (-1, 1):
        b.part("epic_metal", P.box((sx * 0.27, 0, 0.48), (0.16, 0.32, 0.12)))
        b.part("storm", P.box((sx * 0.27, 0, 0.555), (0.12, 0.26, 0.02)))
    return b


# ---- Legs: origin at the waist ----

def legs(b, region_name, belt="leather", width=0.11):
    b.part(belt, P.box((0, 0, -0.04), (0.38, 0.24, 0.08)))
    for sx in (-1, 1):
        b.part(region_name, P.cylinder((sx * 0.1, 0, -0.06), (sx * 0.1, 0, -0.62), width, width * 0.8, 8))


def build_leggings():
    b = builder()
    legs(b, "hide_boar")
    return b


def build_greaves():
    b = builder()
    legs(b, "hide_rhino", width=0.12)
    for sx in (-1, 1):
        b.part("rare_steel", P.box((sx * 0.1, 0.09, -0.42), (0.14, 0.05, 0.22)))  # Knee guards.
    return b


# ---- Feet: a pair; origin between them at the soles ----

def boots(b, region_name, trim):
    for sx in (-1, 1):
        b.part(region_name, P.box((sx * 0.1, 0.03, 0.05), (0.13, 0.26, 0.1)))
        b.part(region_name, P.box((sx * 0.1, -0.02, 0.18), (0.13, 0.15, 0.18)))
        b.part(trim, P.box((sx * 0.1, -0.02, 0.27), (0.15, 0.17, 0.04)))


def build_boots():
    b = builder()
    boots(b, "leather", "hide_boar")
    return b


def build_boots_wind():
    b = builder()
    boots(b, "rare_steel", "fur")
    for sx in (-1, 1):  # Little wing fins at the heel.
        b.part("frost", P.box((sx * 0.17, -0.08, 0.2), (0.02, 0.1, 0.12)))
    return b


# ---- Worn on the hero (5.5 step E): one leg's thigh guard or one boot, hung on each leg's bone by GearView ----
# Thigh: origin at the hip joint, hanging down (-Z) to the knee. Boot: origin at the middle of the sole.

def thigh(b, region_name, radius=0.105):
    b.part(region_name, P.cylinder((0, 0, 0.02), (0, 0, -0.3), radius, radius * 0.85, 8))


def one_boot(b, region_name, trim):
    # A deep toe box and a tall shaft, so it fits over the hero's own chunky boot.
    b.part(region_name, P.box((0, 0.03, 0.075), (0.14, 0.28, 0.15)))
    b.part(region_name, P.box((0, -0.02, 0.22), (0.14, 0.17, 0.16)))
    b.part(trim, P.box((0, -0.02, 0.3), (0.16, 0.19, 0.04)))


def build_leggings_worn():
    b = builder()
    thigh(b, "hide_boar")
    return b


def build_greaves_worn():
    b = builder()
    thigh(b, "hide_rhino", 0.115)
    b.part("rare_steel", P.box((0, 0.1, -0.29), (0.14, 0.05, 0.12)))  # Knee guard.
    return b


def build_boots_worn():
    b = builder()
    one_boot(b, "leather", "hide_boar")
    return b


def build_boots_wind_worn():
    b = builder()
    one_boot(b, "rare_steel", "fur")
    for sx in (-1, 1):
        b.part("frost", P.box((sx * 0.07, -0.08, 0.2), (0.02, 0.1, 0.12)))
    return b


# ---- Main hand: grip at the origin, along +Z ----

def build_cudgel():
    b = builder()
    P.handle(b, "wood", bottom=-0.15, top=0.3, radius=0.03)
    b.part("wood", P.cylinder((0, 0, 0.25), (0, 0, 0.62), 0.06, 0.09, 8))
    for z in (0.35, 0.55):
        b.part("iron", P.cylinder((0, 0, z - 0.025), (0, 0, z + 0.025), 0.095, 0.095, 8))
    P.wrap(b, -0.1, 0.08, 0.036)
    return b


def build_wand():
    b = builder()
    P.handle(b, "wood", bottom=-0.12, top=0.42, radius=0.022)
    b.part("gold", P.cylinder((0, 0, 0.4), (0, 0, 0.46), 0.03, 0.05, 6))
    b.part("crystal", P.gem((0, 0, 0.56), (0.05, 0.05, 0.12)))
    P.wrap(b, -0.05, 0.1, 0.028)
    return b


def build_blade():
    b = builder()
    b.part("leather", P.cylinder((0, 0, -0.12), (0, 0, 0.1), 0.028, 0.028, 8))
    b.part("frost", P.gem((0, 0, -0.15), (0.04, 0.04, 0.04)))  # Pommel gem.
    b.part("gold", P.box((0, 0, 0.12), (0.22, 0.05, 0.04)))  # Cross-guard.
    outline = [(-0.045, 0.0), (0.045, 0.0), (0.04, 0.6), (0.0, 0.72), (-0.04, 0.6)]
    b.part("frost", P.slab(outline, (0, 0, 0.14), Vector((1, 0, 0)), Vector((0, 0, 1)),
                           lambda i: 0.03 if i in (0, 1) else 0.012), grain=Vector((0, 0, 1)))
    return b


def build_maul():
    b = builder()
    P.handle(b, "epic_metal", bottom=-0.3, top=0.55, radius=0.035)
    P.wrap(b, -0.25, 0.0, 0.042, "leather")
    head_z = 0.68
    b.part("molten", P.box((0, 0, head_z), (0.26, 0.5, 0.28)), grain=Vector((0, 1, 0)))
    for sy in (-1, 1):
        b.part("molten", P.box((0, sy * 0.29, head_z), (0.32, 0.08, 0.34)))
        b.part("gold", P.box((0, sy * 0.19, head_z), (0.28, 0.03, 0.3)))
    b.part("storm", P.gem((0, 0, head_z + 0.22), (0.07, 0.07, 0.1)))
    for sx in (-1, 1):  # Thunder horns curling off the head.
        horn(b, "bone", (sx * 0.12, 0, head_z + 0.08), (sx, 0, 0.3), 0.22, 0.05, curl=0.5)
    return b


# ---- Off hand: grip at the origin, the face toward +Y ----

def build_buckler():
    b = builder()
    b.part("wood", P.cylinder((0, 0, 0), (0, 0.05, 0), 0.24, 0.24, 10, phase=math.pi / 10), grain=Vector((1, 0, 0)))
    b.part("iron", P.cylinder((0, -0.005, 0), (0, 0.06, 0), 0.25, 0.25, 10, caps=False))
    b.part("iron", P.cylinder((0, 0.05, 0), (0, 0.1, 0), 0.08, 0.04, 8))  # Boss.
    return b


def build_shield():
    b = builder()
    outline = [(-0.25, 0.25), (0.25, 0.25), (0.24, -0.05), (0.0, -0.36), (-0.24, -0.05)]
    b.part("rare_steel", P.slab(outline, (0, 0.03, 0), Vector((1, 0, 0)), Vector((0, 0, 1)), lambda i: 0.05))
    b.part("hide_rhino", P.slab([(x * 0.8, y * 0.8) for x, y in outline], (0, 0.06, 0), Vector((1, 0, 0)), Vector((0, 0, 1)),
                                lambda i: 0.02))
    horn(b, "bone", (0, 0.08, 0.02), (0, 1, 0.5), 0.2, 0.05, curl=0.4)
    return b


MODELS = {
    "LootHideBoar": build_hide_boar,
    "LootTusk": build_tusk,
    "LootPelt": build_pelt,
    "LootFang": build_fang,
    "LootShard": build_shard,
    "LootHideRhino": build_hide_rhino,
    "LootHorn": build_horn,
    "LootEssenceEarth": build_essence_earth,
    "LootEssenceFrost": build_essence_frost,
    "LootEssenceStorm": build_essence_storm,
    "LootEssenceArcane": build_essence_arcane,
    "LootCap": build_cap,
    "LootJerkin": build_jerkin,
    "LootLeggings": build_leggings,
    "LootBoots": build_boots,
    "LootCudgel": build_cudgel,
    "LootBuckler": build_buckler,
    "LootHood": build_hood,
    "LootJerkinTusk": build_jerkin_tusk,
    "LootGreaves": build_greaves,
    "LootBootsWind": build_boots_wind,
    "LootWand": build_wand,
    "LootShield": build_shield,
    "LootCrown": build_crown,
    "LootHauberk": build_hauberk,
    "LootBlade": build_blade,
    "LootMaul": build_maul,
    # Worn pieces for the legs and feet (step E), one per leg: LootBuilder finds them by "<model>Worn".
    "LootLeggingsWorn": build_leggings_worn,
    "LootGreavesWorn": build_greaves_worn,
    "LootBootsWorn": build_boots_worn,
    "LootBootsWindWorn": build_boots_wind_worn,
}


# ----------------------------------------------------------------------------------------------------------------
# Blender scene and export
# ----------------------------------------------------------------------------------------------------------------

def preview_material(atlas, glow):
    mat = P.preview_material(atlas, glow)
    mat.name = "Loot_Preview"
    return mat


def build_models(material):
    collection = bpy.data.collections.get("Loot")
    if collection is None:
        collection = bpy.data.collections.new("Loot")
        bpy.context.scene.collection.children.link(collection)
    for i, (name, build) in enumerate(MODELS.items()):
        b = build()
        obj = b.build(name, material, collection)
        obj.location = ((i % 9) * 1.0, (i // 9) * 1.2, 0.0)
        print(f"{name}: {b.triangle_count()} triangles")


def export_models(only=None):
    os.makedirs(MODEL_DIR, exist_ok=True)
    for name in (only or MODELS):
        obj = bpy.data.objects[name]
        saved = obj.location.copy()
        obj.location = (0.0, 0.0, 0.0)
        for o in bpy.context.scene.objects:
            o.select_set(o == obj)
        path = os.path.join(MODEL_DIR, f"{name}.fbx")
        with bpy.context.temp_override(active_object=obj, object=obj, selected_objects=[obj]):
            bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"},
                                     apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                                     bake_space_transform=True, axis_forward="-Z", axis_up="Y",
                                     mesh_smooth_type="OFF", colors_type="LINEAR", prioritize_active_color=True,
                                     use_tspace=False, use_custom_props=False, add_leaf_bones=False,
                                     bake_anim=False, path_mode="STRIP")
        obj.location = saved
        print(f"Exported {path}")


def build_all(export=True, only=None):
    albedo, glow = paint_atlas()
    atlas_image = P.save_image("Loot_Atlas", albedo, ATLAS_PATH)
    glow_image = P.save_image("Loot_Glow", glow, GLOW_PATH)
    build_models(preview_material(atlas_image, glow_image))
    if export:
        export_models(only)


if __name__ == "__main__":
    build_all()
