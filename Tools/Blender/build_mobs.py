"""Builds Voxelforge's overland mobs (Roadmap 5.2 step E): the Bristleback Boar and the Frostfang Wolf.

Run inside Blender with Mobs.blend open (Text Editor, or exec over MCP) and build_hero.py next to it. build_all()
rebuilds every mob in the current file and exports it for Unity; build_mob("Boar") rebuilds one. For each mob:
  - "<Name>Rig": a small quadruped skeleton made here (not Mixamo): Root, Hips, Chest, Neck, Head, a tail and two
    bones a leg, all named the same on every mob so the clips read alike
  - "<Name>": one mesh, facing -Y, feet at z = 0, modelled from build_hero's pieces (box, ball, limb). Each piece
    follows one bone outright, like a jointed wooden toy, which suits the chunky voxel look and needs no weight
    painting
  - one baked 512 albedo with the Wayfarer's painted shading (build_hero._build_bake_nodes), on one material
  - its clips as actions keyed here: Idle, Walk, Run, Attack, Hit, Death, plus Paw and Charge (Boar) or Howl (Wolf)
They export to Assets/_Voxelforge/Art/Models/Mobs/<Name>/<Name>.fbx as a Generic rig with every action as a take.
Then run Voxelforge > Rebuild Mob Models in Unity.

Blender is Z-up with the mob facing -Y; the FBX exporter turns that into Unity's Y-up, facing +Z.
"""
import importlib.util
import math
import os

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

HERE = os.path.dirname(bpy.data.filepath) if bpy.data.filepath else os.path.join(
    os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals() else "", "")
REPO = os.path.abspath(os.path.join(HERE, "..", "..")) if HERE else ""


def _load_hero_module():
    path = os.path.join(HERE, "build_hero.py")
    spec = importlib.util.spec_from_file_location("build_hero", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


hb = _load_hero_module()

MODEL_ROOT = os.path.join(REPO, "Assets", "_Voxelforge", "Art", "Models", "Mobs")
FPS = 30
LEGS = ("FL", "FR", "BL", "BR")


class Mob:
    """One mob's look: its parts' colours, its skeleton, its pieces and its clips."""

    def __init__(self, name, parts, bones, height):
        self.name = name
        self.parts = parts            # part -> linear RGB
        self.bones = bones            # bone -> (head, tail, parent)
        self.height = height          # top of the body, for the bake's light-from-above

    def model(self):
        raise NotImplementedError

    def clips(self):
        """name -> (frames, loop, pose(t) -> {bone: (pitch, yaw, roll degrees, (x, y, z) offset)})."""
        raise NotImplementedError


# ---------------------------------------------------------------------------------------------- pieces

_bone_of = {}


def on(bone, obj):
    """Fastens a piece (just made by build_hero's box/ball/limb) to bone."""
    _bone_of[obj.name] = bone
    return obj


def _join(name, parts, bones):
    """Merges the pieces into one mesh with a "Part" corner colour, like build_hero._join_pieces, and puts every vertex
    in its piece's bone's vertex group at full weight."""
    names = list(parts)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    bm = bmesh.new()
    color_layer = bm.loops.layers.float_color.new("Part")
    bone_layer = bm.verts.layers.int.new("Bone")
    bone_names = list(bones)
    for obj in hb._pieces:
        part = obj["part"]
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        before_faces = set(bm.faces)
        before_verts = set(bm.verts)
        bm.from_mesh(mesh)
        evaluated.to_mesh_clear()
        color = (*parts[part], 1.0)
        for face in bm.faces:
            if face in before_faces:
                continue
            face.smooth = True
            for loop in face.loops:
                loop[color_layer] = color
        index = bone_names.index(_bone_of[obj.name])
        for vert in bm.verts:
            if vert not in before_verts:
                vert[bone_layer] = index
    for obj in hb._pieces:
        mesh = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.meshes.remove(mesh)
    hb._pieces.clear()
    _bone_of.clear()

    body = hb._object_from_bmesh(name, bm)
    groups = [body.vertex_groups.new(name=b) for b in bone_names]
    layer = body.data.attributes["Bone"].data
    for vert in body.data.vertices:
        groups[layer[vert.index].value].add([vert.index], 1.0, "REPLACE")
    body.data.attributes.remove(body.data.attributes["Bone"])
    return body


# ---------------------------------------------------------------------------------------------- the Boar

class Boar(Mob):
    """The Bristleback Boar: a low, heavy, tusked Meadows boar with a black bristle crest. About 1.0 m tall at the
    crest and 1.7 m snout to tail; its collision box is 0.84 x 1.0 x 0.84."""

    def __init__(self):
        front, back, side = -0.27, 0.27, 0.19
        bones = {
            "Root": ((0, 0, 0), (0, 0, 0.25), None),
            "Hips": ((0, 0.34, 0.60), (0, 0.0, 0.64), "Root"),
            "Chest": ((0, 0.0, 0.64), (0, -0.34, 0.68), "Hips"),
            "Neck": ((0, -0.34, 0.68), (0, -0.48, 0.64), "Chest"),
            "Head": ((0, -0.48, 0.64), (0, -0.88, 0.54), "Neck"),
            "Tail": ((0, 0.42, 0.66), (0, 0.56, 0.52), "Hips"),
        }
        for leg in LEGS:
            x = side * (1 if leg[1] == "L" else -1)  # Left is +X when facing -Y.
            y = front if leg[0] == "F" else back
            parent = "Chest" if leg[0] == "F" else "Hips"
            bones[f"Upper_{leg}"] = ((x, y, 0.56), (x, y, 0.30), parent)
            bones[f"Lower_{leg}"] = ((x, y, 0.30), (x, y, 0.03), f"Upper_{leg}")
        parts = {
            "hide": (0.20, 0.10, 0.05),
            "hide_light": (0.40, 0.24, 0.13),
            "bristle": (0.05, 0.03, 0.02),
            "snout": (0.55, 0.30, 0.25),
            "snout_dark": (0.22, 0.10, 0.08),
            "tusk": (0.86, 0.81, 0.66),
            "hoof": (0.06, 0.05, 0.04),
            "eye": (0.02, 0.02, 0.02),
        }
        super().__init__("Boar", parts, bones, height=1.0)

    def model(self):
        b = self.bones
        # --- Body: a deep chest and shoulder hump in front, narrower hips behind, a pale belly ---
        on("Chest", hb.ball("hide", (0, -0.12, 0.64), 0.34, scale=(1.0, 1.15, 1.0), subdiv=1))
        on("Chest", hb.ball("hide", (0, -0.16, 0.82), 0.24, scale=(1.0, 1.25, 0.7), subdiv=1))      # hump
        on("Chest", hb.ball("hide_light", (0, -0.10, 0.44), 0.22, scale=(1.1, 1.5, 0.55), segments=12, rings=8))
        on("Hips", hb.ball("hide", (0, 0.22, 0.62), 0.30, scale=(0.95, 1.15, 0.95), subdiv=1))
        # The bristle crest down the back, tallest over the shoulders.
        for i, y in enumerate((-0.40, -0.30, -0.20, -0.10, 0.0, 0.10, 0.20, 0.30)):
            tall = 0.26 - abs(y + 0.16) * 0.30
            back = 0.99 - abs(y + 0.16) * 0.22        # Roughly the top of the back here.
            on("Chest" if y < 0.0 else "Hips",
               hb.box("bristle", (0, y, back + tall * 0.2), (0.09, 0.08, tall), bevel=0.025, subdiv=0))
        # --- Neck and head: a wedge of a head, pink snout disc, curled tusks, small eyes and ears ---
        on("Neck", hb.limb("hide", (0, -0.30, 0.68), (0, -0.50, 0.64), 0.27, 0.22, sides=12))
        on("Head", hb.ball("hide", (0, -0.58, 0.62), 0.21, scale=(1.0, 1.25, 0.95), subdiv=1))
        on("Head", hb.limb("snout", (0, -0.70, 0.57), (0, -0.86, 0.53), 0.12, 0.105, sides=12, rings=2))
        on("Head", hb.ball("snout_dark", (0, -0.865, 0.53), 0.085, scale=(1.15, 0.28, 0.85), segments=12, rings=6))
        for s in (-1, 1):
            on("Head", hb.ball("eye", (0.115 * s, -0.66, 0.70), 0.028, segments=8, rings=6))
            on("Head", hb.box("bristle", (0.11 * s, -0.66, 0.735), (0.07, 0.03, 0.02), bevel=0.006, subdiv=0))
            on("Head", hb.ball("hide", (0.15 * s, -0.48, 0.80), 0.07, scale=(0.55, 0.45, 1.3), segments=8, rings=6))
            pts = [(0.075 * s, -0.78, 0.50), (0.11 * s, -0.84, 0.55), (0.13 * s, -0.83, 0.63), (0.12 * s, -0.79, 0.68)]
            for i in range(3):
                on("Head", hb.limb("tusk", pts[i], pts[i + 1], 0.028 - i * 0.008, 0.02 - i * 0.008 + 0.002, sides=6, rings=1))
        # --- Tail: a short curl with a tuft ---
        on("Tail", hb.limb("hide", (0, 0.42, 0.66), (0, 0.54, 0.54), 0.03, 0.022, sides=6, rings=2))
        on("Tail", hb.ball("bristle", (0, 0.555, 0.52), 0.045, scale=(1, 1, 1.4), segments=8, rings=6))
        # --- Legs: stout, with dark split hooves ---
        for leg in LEGS:
            (x, y, top), (_, _, knee), _ = b[f"Upper_{leg}"]
            on(f"Upper_{leg}", hb.limb("hide", (x, y, top + 0.04), (x, y, knee), 0.11, 0.085, sides=10))
            on(f"Upper_{leg}", hb.ball("hide", (x, y, knee), 0.085, segments=10, rings=6))
            on(f"Lower_{leg}", hb.limb("hide", (x, y, knee), (x, y, 0.09), 0.08, 0.07, sides=10))
            on(f"Lower_{leg}", hb.box("hoof", (x, y - 0.02, 0.05), (0.14, 0.17, 0.10), bevel=0.025))
            on(f"Lower_{leg}", hb.box("snout_dark", (x, y - 0.11, 0.05), (0.018, 0.03, 0.09), bevel=0.004, subdiv=0))

    def clips(self):
        return {
            "Idle": (60, True, lambda t: _idle(t, tail=18)),
            "Walk": (30, True, lambda t: _walk(t, swing=24, lift=34, bob=0.025)),
            "Run": (18, True, lambda t: _gallop(t, swing=40, lift=50, bob=0.06, pitch=6)),
            # Windup 0.35 s: the tusks come up and back, then hook down and forward on frame 11.
            "Attack": (22, False, lambda t: _gore(t, impact=11 / 22)),
            "Hit": (10, False, _flinch),
            "Death": (26, False, lambda t: _topple(t, width=0.37)),
            # The charge telegraph: head low, one forefoot raking the ground.
            "Paw": (24, True, _paw),
            "Charge": (14, True, lambda t: _gallop(t, swing=46, lift=55, bob=0.07, pitch=4, head=24)),
        }


# ---------------------------------------------------------------------------------------------- the Wolf

class Wolf(Mob):
    """The Frostfang Wolf: a lean grey-blue wolf with a white ruff and ice-blue eyes. About 1.05 m to the ear tips
    and 1.5 m nose to tail; its collision box is 0.76 x 0.9 x 0.76."""

    def __init__(self):
        front, back, side = -0.22, 0.27, 0.11
        bones = {
            "Root": ((0, 0, 0), (0, 0, 0.25), None),
            "Hips": ((0, 0.32, 0.58), (0, 0.0, 0.60), "Root"),
            "Chest": ((0, 0.0, 0.60), (0, -0.28, 0.64), "Hips"),
            "Neck": ((0, -0.28, 0.66), (0, -0.40, 0.80), "Chest"),
            "Head": ((0, -0.40, 0.80), (0, -0.76, 0.75), "Neck"),
            "Tail": ((0, 0.38, 0.62), (0, 0.56, 0.54), "Hips"),
            "Tail2": ((0, 0.56, 0.54), (0, 0.66, 0.34), "Tail"),
        }
        for leg in LEGS:
            x = side * (1 if leg[1] == "L" else -1)
            y = front if leg[0] == "F" else back
            parent = "Chest" if leg[0] == "F" else "Hips"
            bones[f"Upper_{leg}"] = ((x, y, 0.56), (x, y, 0.30), parent)
            bones[f"Lower_{leg}"] = ((x, y, 0.30), (x, y, 0.02), f"Upper_{leg}")
        parts = {
            "fur": (0.30, 0.34, 0.42),
            "fur_dark": (0.13, 0.15, 0.20),
            "ruff": (0.80, 0.84, 0.90),
            "nose": (0.03, 0.03, 0.035),
            "eye": (0.30, 0.75, 1.0),
            "ear_inner": (0.50, 0.34, 0.38),
            "paw": (0.11, 0.12, 0.15),
        }
        super().__init__("Wolf", parts, bones, height=1.05)

    def model(self):
        b = self.bones
        # --- Body: a deep chest under a big white ruff, a tucked waist, a dark saddle ---
        on("Chest", hb.ball("fur", (0, -0.12, 0.62), 0.20, scale=(0.95, 1.35, 1.05), subdiv=1))
        on("Chest", hb.ball("ruff", (0, -0.24, 0.66), 0.21, scale=(1.15, 0.85, 1.05), subdiv=1))
        on("Hips", hb.ball("fur", (0, 0.20, 0.58), 0.16, scale=(0.95, 1.45, 0.95), subdiv=1))
        on("Hips", hb.ball("fur_dark", (0, 0.10, 0.70), 0.13, scale=(1.0, 2.0, 0.45), segments=12, rings=8))
        # --- Neck and head: a long muzzle, pointed ears, glowing eyes ---
        on("Neck", hb.limb("fur", (0, -0.28, 0.66), (0, -0.42, 0.80), 0.14, 0.11, sides=10))
        on("Head", hb.ball("fur", (0, -0.47, 0.82), 0.13, scale=(1.0, 1.15, 0.95), subdiv=1))
        on("Head", hb.limb("ruff", (0, -0.55, 0.78), (0, -0.73, 0.745), 0.075, 0.048, sides=10, rings=2))
        on("Head", hb.ball("nose", (0, -0.735, 0.755), 0.032, segments=8, rings=6))
        on("Head", hb.ball("ruff", (0, -0.50, 0.73), 0.07, scale=(1.3, 1.0, 0.7), segments=10, rings=6))  # cheeks
        for s in (-1, 1):
            on("Head", hb.ball("eye", (0.062 * s, -0.565, 0.855), 0.024, scale=(1, 0.7, 0.8), segments=8, rings=6))
            on("Head", hb.box("fur_dark", (0.06 * s, -0.565, 0.885), (0.06, 0.03, 0.018), bevel=0.005, subdiv=0))
            on("Head", hb.limb("fur", (0.07 * s, -0.43, 0.90), (0.085 * s, -0.42, 1.05), 0.05, 0.006, sides=4, rings=1))
            on("Head", hb.limb("ear_inner", (0.07 * s, -0.455, 0.91), (0.083 * s, -0.445, 1.01), 0.028, 0.004, sides=4, rings=1))
        # --- Tail: bushy, dark-tipped ---
        on("Tail", hb.limb("fur", (0, 0.36, 0.62), (0, 0.57, 0.53), 0.05, 0.08, sides=8))
        on("Tail2", hb.limb("fur", (0, 0.56, 0.54), (0, 0.65, 0.36), 0.08, 0.05, sides=8))
        on("Tail2", hb.ball("fur_dark", (0, 0.66, 0.33), 0.055, scale=(1, 1, 1.3), segments=8, rings=6))
        # --- Legs: long and thin, with dark paws ---
        for leg in LEGS:
            (x, y, top), (_, _, knee), _ = b[f"Upper_{leg}"]
            thick = 0.075 if leg[0] == "B" else 0.065
            on(f"Upper_{leg}", hb.limb("fur", (x, y, top + 0.04), (x, y, knee), thick, 0.05, sides=8))
            on(f"Upper_{leg}", hb.ball("fur", (x, y, knee), 0.05, segments=8, rings=6))
            on(f"Lower_{leg}", hb.limb("fur", (x, y, knee), (x, y, 0.06), 0.045, 0.04, sides=8))
            on(f"Lower_{leg}", hb.box("paw", (x, y - 0.03, 0.03), (0.09, 0.13, 0.06), bevel=0.02))

    def clips(self):
        return {
            "Idle": (60, True, lambda t: _idle(t, tail=25)),
            "Walk": (28, True, lambda t: _walk(t, swing=26, lift=40, bob=0.02)),
            "Run": (16, True, lambda t: _gallop(t, swing=44, lift=60, bob=0.05, pitch=8)),
            # Windup 0.3 s: a crouch back on frame 0-9, then a lunging bite.
            "Attack": (20, False, lambda t: _bite(t, impact=9 / 20)),
            "Hit": (10, False, _flinch),
            "Death": (26, False, lambda t: _topple(t, width=0.27)),
            # The pack call: sits back, throws its head up and holds the note.
            "Howl": (27, False, _howl),
        }


MOBS = {"Boar": Boar, "Wolf": Wolf}


# ---------------------------------------------------------------------------------------------- clip shapes
# Each returns {bone: (pitch, yaw, roll, offset)} at t in [0, 1). Angles are degrees about the armature's axes
# (pitch +X: a leg swings back, a head dips its nose); offsets are metres in the armature's space.

def _p(pitch=0.0, yaw=0.0, roll=0.0, offset=(0.0, 0.0, 0.0)):
    return (pitch, yaw, roll, offset)


def _ease(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3 - 2 * x)


def _legs(pose, phase_of, swing, lift):
    """Each leg swings fore and aft on its phase; the lower leg tucks up while the foot comes forward."""
    for leg in LEGS:
        a = 2 * math.pi * phase_of[leg]
        pose[f"Upper_{leg}"] = _p(pitch=swing * math.sin(a))
        tuck = max(0.0, math.sin(a + math.pi / 2))
        # A front knee folds back (+pitch is back), a hock folds forward.
        pose[f"Lower_{leg}"] = _p(pitch=(lift if leg[0] == "F" else -lift) * tuck)


def _idle(t, tail):
    a = 2 * math.pi * t
    return {
        "Root": _p(offset=(0, 0, 0.008 * math.sin(2 * a))),
        "Chest": _p(pitch=1.5 * math.sin(a)),
        "Head": _p(pitch=5 * math.sin(a + 1.0), yaw=6 * math.sin(a * 0.5 + 0.4)),
        "Tail": _p(yaw=tail * math.sin(2 * a)),
    }


def _walk(t, swing, lift, bob):
    a = 2 * math.pi * t
    pose = {
        "Root": _p(offset=(0, 0, bob * abs(math.sin(a)))),
        "Chest": _p(roll=2.5 * math.sin(a)),
        "Head": _p(pitch=4 * math.sin(2 * a)),
        "Tail": _p(yaw=12 * math.sin(a)),
    }
    _legs(pose, {"FL": t, "BR": t, "FR": t + 0.5, "BL": t + 0.5}, swing, lift)
    return pose


def _gallop(t, swing, lift, bob, pitch, head=0.0):
    a = 2 * math.pi * t
    pose = {
        "Root": _p(pitch=pitch * math.sin(a), offset=(0, 0, bob * (0.5 + 0.5 * math.sin(a)))),
        "Neck": _p(pitch=head * 0.4),
        "Head": _p(pitch=head + 6 * math.sin(a + 0.6)),
        "Tail": _p(pitch=-15 - 10 * math.sin(a)),
    }
    _legs(pose, {"FL": t, "FR": t + 0.08, "BL": t + 0.5, "BR": t + 0.58}, swing, lift)
    return pose


def _gore(t, impact):
    # Wind up: head up and back, weight on the hind legs. Strike: hook down and forward. Recover.
    if t < impact:
        k = _ease(t / impact)
        head, lunge = -28 * k, 0.06 * k
    else:
        u = (t - impact) / (1 - impact)
        if u < 0.25:
            k = _ease(u / 0.25)
            head, lunge = -28 + 70 * k, 0.06 - 0.22 * k
        else:
            r = _ease((u - 0.25) / 0.75)
            head, lunge = 42 * (1 - r), -0.16 * (1 - r)
    pose = {
        "Root": _p(pitch=-head * 0.15, offset=(0, lunge, 0)),
        "Neck": _p(pitch=head * 0.4),
        "Head": _p(pitch=head * 0.6),
        "Tail": _p(pitch=-20),
    }
    _legs(pose, {leg: 0.0 for leg in LEGS}, 0, 0)
    return pose


def _bite(t, impact):
    # Crouch back, then spring forward with the jaws.
    if t < impact:
        k = _ease(t / impact)
        crouch, reach, head = 0.07 * k, 0.08 * k, -15 * k
    else:
        u = (t - impact) / (1 - impact)
        k = _ease(u / 0.3) if u < 0.3 else 1 - _ease((u - 0.3) / 0.7)
        crouch, reach, head = 0.07 * (1 - _ease(u / 0.2)), 0.08 - 0.30 * k, -15 + 35 * k
    pose = {
        "Root": _p(offset=(0, reach, -crouch)),
        "Neck": _p(pitch=head * 0.5),
        "Head": _p(pitch=head * 0.5),
        "Tail": _p(pitch=-10, yaw=10 * math.sin(6 * t)),
    }
    bend = crouch / 0.07 * 25
    for leg in LEGS:
        pose[f"Upper_{leg}"] = _p(pitch=-bend if leg[0] == "B" else bend * 0.4)
        pose[f"Lower_{leg}"] = _p(pitch=bend if leg[0] == "B" else -bend * 0.4)
    return pose


def _flinch(t):
    k = math.sin(math.pi * min(1.0, t * 1.3))
    return {
        "Root": _p(pitch=-6 * k, offset=(0, 0.06 * k, 0)),
        "Head": _p(pitch=-20 * k, yaw=10 * k),
        "Tail": _p(pitch=15 * k),
    }


def _topple(t, width):
    # Staggers, then rolls onto its side; the legs go limp and the head flops down. Holds the last frame.
    k = _ease(t / 0.75)
    pose = {
        # Rolling about the feet swings the body down beside them; lift it by its half-width so it lies on the ground.
        "Root": _p(roll=88 * k, offset=(0, 0, width * k + 0.04 * math.sin(math.pi * min(1, t / 0.4)))),
        "Neck": _p(pitch=20 * k),
        "Head": _p(pitch=25 * k, roll=-10 * k),
        "Tail": _p(yaw=20 * k),
    }
    for leg in LEGS:
        pose[f"Upper_{leg}"] = _p(pitch=(-25 if leg[0] == "F" else 25) * k, roll=-12 * k)
        pose[f"Lower_{leg}"] = _p(pitch=(20 if leg[0] == "F" else -20) * k)
    return pose


def _paw(t):
    a = 2 * math.pi * t
    pose = {
        "Root": _p(pitch=5, offset=(0, 0.03, -0.02)),
        "Neck": _p(pitch=12),
        "Head": _p(pitch=14 + 4 * math.sin(2 * a), yaw=5 * math.sin(a)),
        "Tail": _p(yaw=30 * math.sin(3 * a)),
        "Upper_FR": _p(pitch=-30 + 50 * (0.5 + 0.5 * math.sin(a))),
        "Lower_FR": _p(pitch=35 * max(0.0, math.sin(a + math.pi / 2))),
    }
    for leg in ("FL", "BL", "BR"):
        pose[f"Upper_{leg}"] = _p(pitch=-6 if leg[0] == "B" else 4)
        pose[f"Lower_{leg}"] = _p(pitch=0)
    return pose


def _howl(t):
    # Sits back (frames 0-8), throws its head up and holds the howl to the end.
    k = _ease(t / 0.3)
    wobble = 2 * math.sin(2 * math.pi * 6 * t) * k
    pose = {
        "Root": _p(pitch=-22 * k, offset=(0, 0.05 * k, -0.12 * k)),
        "Neck": _p(pitch=-30 * k),
        "Head": _p(pitch=-28 * k + wobble),
        "Tail": _p(pitch=25 * k, yaw=8 * math.sin(4 * math.pi * t)),
        "Upper_BL": _p(pitch=-55 * k), "Upper_BR": _p(pitch=-55 * k),
        "Lower_BL": _p(pitch=70 * k), "Lower_BR": _p(pitch=70 * k),
        "Upper_FL": _p(pitch=18 * k), "Upper_FR": _p(pitch=18 * k),
        "Lower_FL": _p(pitch=0), "Lower_FR": _p(pitch=0),
    }
    return pose


# ---------------------------------------------------------------------------------------------- build steps

def _clear_old(name):
    for obj in list(bpy.data.objects):
        if obj.name in (name, f"{name}Rig") or obj.get("part") is not None:
            bpy.data.objects.remove(obj, do_unlink=True)
    for action in list(bpy.data.actions):
        if action.name.startswith(f"{name}|") or action.name.startswith(f"{name}_"):
            bpy.data.actions.remove(action)
    for coll in (bpy.data.meshes, bpy.data.armatures):
        for data in list(coll):
            if data.users == 0:
                coll.remove(data)


def build_rig(mob):
    data = bpy.data.armatures.new(f"{mob.name}Rig")
    rig = bpy.data.objects.new(f"{mob.name}Rig", data)
    bpy.context.scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    for obj in bpy.context.view_layer.objects:
        obj.select_set(obj == rig)
    bpy.ops.object.mode_set(mode="EDIT")
    for name, (head, tail, parent) in mob.bones.items():
        bone = data.edit_bones.new(name)
        bone.head = Vector(head)
        bone.tail = Vector(tail)
        bone.roll = 0.0
        if parent:
            bone.parent = data.edit_bones[parent]
            bone.use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    return rig


def build_body(mob, rig):
    hb._pieces.clear()
    _bone_of.clear()
    mob.model()
    body = _join(mob.name, mob.parts, mob.bones)
    mesh = body.data
    mesh.materials.clear()
    mat = bpy.data.materials.get(f"{mob.name}_Body") or bpy.data.materials.new(f"{mob.name}_Body")
    mat.use_nodes = True
    mesh.materials.append(mat)
    body.parent = rig
    body.modifiers.new("Armature", "ARMATURE").object = rig
    tris = sum(len(p.vertices) - 2 for p in mesh.polygons)
    print(f"{mob.name}: {tris} tris, {len(rig.data.bones)} bones")
    return body


def unwrap(body, margin=0.006):
    for obj in bpy.context.view_layer.objects:
        obj.select_set(obj == body)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=margin)
    bpy.ops.object.mode_set(mode="OBJECT")


def albedo_path(mob):
    return os.path.join(MODEL_ROOT, mob.name, "Textures", f"{mob.name}_Albedo.png")


def bake(mob, body, size=512):
    """The part colours with build_hero's hand-painted shading, into one texture."""
    image_name = f"{mob.name}_Albedo"
    image = bpy.data.images.get(image_name) or bpy.data.images.new(image_name, size, size)
    image.scale(size, size)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 48
    scene.render.bake.margin = 6
    mat = body.data.materials[0]
    hb._build_bake_nodes(mat, image, height=mob.height)
    for obj in bpy.context.view_layer.objects:
        obj.select_set(obj == body)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.bake(type="EMIT", margin=6, use_clear=True)
    path = albedo_path(mob)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    hb._build_final_nodes(mat, image)
    print(f"Baked {path}")


def _rest_frames(rig):
    """Each bone's rest rotation in armature space (3x3), to turn armature-space turns into the bone's own."""
    return {b.name: b.matrix_local.to_3x3() for b in rig.data.bones}


def build_clips(mob, rig):
    """Keys each clip as its own action on the rig. Every bone is keyed on every keyed frame, so no clip leaves a bone
    where the last one put it."""
    rest = _rest_frames(rig)
    rig.animation_data_create()
    for pose_bone in rig.pose.bones:
        pose_bone.rotation_mode = "QUATERNION"
    actions = []
    for clip, (frames, loop, shape) in mob.clips().items():
        action = bpy.data.actions.new(f"{mob.name}|{clip}")
        action.use_fake_user = True
        rig.animation_data.action = action
        last = frames if loop else frames - 1
        step = 1 if frames <= 20 else 2
        keyed = list(range(0, last + 1, step))
        if keyed[-1] != last:
            keyed.append(last)
        for frame in keyed:
            t = frame / frames
            pose = shape(t if loop else t * frames / max(1, frames - 1))
            for pose_bone in rig.pose.bones:
                pitch, yaw, roll, offset = pose.get(pose_bone.name, _p())
                m = rest[pose_bone.name]
                turn = Euler((math.radians(pitch), math.radians(roll), math.radians(yaw)), "XYZ").to_matrix()
                local = (m.inverted() @ turn @ m).to_quaternion()
                pose_bone.rotation_quaternion = local
                pose_bone.location = m.inverted() @ Vector(offset)
                pose_bone.keyframe_insert("rotation_quaternion", frame=frame)
                pose_bone.keyframe_insert("location", frame=frame)
        action.frame_range = (0, last)
        actions.append(action)
    # Each clip goes on its own NLA track, which is what the export reads: exporting "all actions" would also hand
    # this rig every other mob's clips, since their bones share names.
    rig.animation_data.action = None
    for track in list(rig.animation_data.nla_tracks):
        rig.animation_data.nla_tracks.remove(track)
    for action in actions:
        track = rig.animation_data.nla_tracks.new()
        track.name = action.name.split("|")[1]
        strip = track.strips.new(track.name, 0, action)
        strip.name = track.name
    bpy.context.scene.render.fps = FPS
    print(f"{mob.name}: {len(actions)} clips: {', '.join(a.name.split('|')[1] for a in actions)}")
    return actions


def fbx_path(mob):
    return os.path.join(MODEL_ROOT, mob.name, f"{mob.name}.fbx")


def export_fbx(mob, rig, body):
    for obj in bpy.context.view_layer.objects:
        obj.select_set(obj in (rig, body))
    bpy.context.view_layer.objects.active = rig
    path = fbx_path(mob)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"},
                             apply_scale_options="FBX_SCALE_UNITS", mesh_smooth_type="FACE",
                             add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=False,
                             bake_anim_use_nla_strips=True, bake_anim_force_startend_keying=True,
                             bake_anim_simplify_factor=0.0, path_mode="AUTO")
    print(f"Exported {path}")


def build_mob(name, export=True):
    mob = MOBS[name]()
    _clear_old(mob.name)
    rig = build_rig(mob)
    body = build_body(mob, rig)
    build_clips(mob, rig)
    if export:
        unwrap(body)
        bake(mob, body)
        export_fbx(mob, rig, body)
    return rig, body


def build_all(export=True):
    """Rebuilds every mob side by side (the Boar at x = 0, the Wolf at x = 2 in the file; the export is per mob)."""
    results = {}
    for i, name in enumerate(MOBS):
        rig, body = build_mob(name, export)
        rig.location.x = 2.0 * i
        results[name] = (rig, body)
    return results
