"""Builds the Thunderhoof, Voxelforge's big, gentle bull-folk race (Roadmap 4.7).

Run inside Blender (Text Editor, or exec over MCP) with build_hero.py next to it. build_all() rebuilds everything
in the current file and exports it for Unity:
  - "ThunderhoofRig": the Wayfarer's Mixamo skeleton (same bone names, from Hero.blend) with its joints moved to
    the Thunderhoof's proportions, so Unity's Humanoid retargeting plays the 2.5 Mixamo clips on it
  - "Thunderhoof": one mesh, T-pose, facing -Y, feet at z = 0, about 2.0 m to the top of the head (horns above),
    modelled around that skeleton's joints, with the Hero's two material slots (body, team cloth)
  - one baked 1024 albedo with the same painted shading as the Wayfarer (build_hero.bake nodes)
No Mixamo upload: the body is weighted by melting it into one closed shell (as build_hero's rig proxy does),
letting Blender's automatic weights skin that, and copying the weights back onto the real pieces. Horns, ears and
head props follow the head rigidly; the tail follows the hips.

Blender is Z-up with the hero facing -Y; the FBX exporter turns that into Unity's Y-up, facing +Z.
"""
import importlib.util
import math
import os

import bmesh
import bpy
from mathutils import Vector

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

HERO_BLEND = os.path.join(HERE, "Hero.blend")
MODEL_DIR = os.path.join(REPO, "Assets", "_Voxelforge", "Art", "Models", "Thunderhoof")
FBX_PATH = os.path.join(MODEL_DIR, "Thunderhoof.fbx")
ALBEDO_PATH = os.path.join(MODEL_DIR, "Textures", "Thunderhoof_Albedo.png")
MATERIALS = ("Thunderhoof_Body", "Thunderhoof_TeamCloth")
MAX_INFLUENCES = 4

# Base colors per part, linear RGB. TeamCloth parts are light so the per-player tint colors them.
PARTS = {
    "fur": ((0.30, 0.16, 0.07), MATERIALS[0]),
    "fur_light": ((0.62, 0.45, 0.29), MATERIALS[0]),
    "mane": ((0.10, 0.055, 0.03), MATERIALS[0]),
    "nose": ((0.20, 0.12, 0.11), MATERIALS[0]),
    "eye": ((0.02, 0.02, 0.02), MATERIALS[0]),
    "horn": ((0.82, 0.76, 0.60), MATERIALS[0]),
    "hoof": ((0.10, 0.085, 0.075), MATERIALS[0]),
    "leather": ((0.30, 0.17, 0.08), MATERIALS[0]),
    "gold": ((0.85, 0.58, 0.18), MATERIALS[0]),
    "bone": ((0.88, 0.84, 0.74), MATERIALS[0]),
    "feather": ((0.62, 0.14, 0.06), MATERIALS[0]),
    "bead": ((0.08, 0.50, 0.50), MATERIALS[0]),
    "tail": ((0.30, 0.16, 0.07), MATERIALS[0]),
    "tail_tuft": ((0.10, 0.055, 0.03), MATERIALS[0]),
    "cloth": ((0.90, 0.88, 0.84), MATERIALS[1]),
    "cloth_trim": ((0.62, 0.60, 0.56), MATERIALS[1]),
}

# Parts that don't bend: every vertex of them follows one bone.
RIGID = {"horn": "mixamorig:Head", "eye": "mixamorig:Head", "nose": "mixamorig:Head",
         "tail": "mixamorig:Hips", "tail_tuft": "mixamorig:Hips"}

# Where each joint goes (world, Z up, facing -Y), left side only; right is mirrored. head -> tail per bone.
# Wayfarer for scale: hips 0.845, shoulders +-0.38 at 1.41, head top 1.89.
SPINE = {
    "Hips": ((0, 0, 0.90), (0, 0, 1.02)),
    "Spine": ((0, 0, 1.02), (0, 0, 1.17)),
    "Spine1": ((0, 0, 1.17), (0, 0, 1.34)),
    "Spine2": ((0, 0, 1.34), (0, 0, 1.56)),
    "Neck": ((0, -0.01, 1.54), (0, -0.03, 1.65)),
    "Head": ((0, -0.03, 1.65), (0, -0.03, 2.05)),
    "HeadTop_End": ((0, -0.03, 2.05), (0, -0.03, 2.45)),
}
LEFT = {
    "Shoulder": ((0.15, 0, 1.52), (0.46, 0, 1.53)),
    "Arm": ((0.46, 0, 1.53), (0.76, 0, 1.49)),
    "ForeArm": ((0.76, 0, 1.49), (1.02, -0.02, 1.49)),
    "Hand": ((1.02, -0.02, 1.49), (1.24, -0.04, 1.49)),
    "UpLeg": ((0.15, 0, 0.84), (0.15, 0, 0.50)),
    "Leg": ((0.15, 0, 0.50), (0.155, 0.005, 0.20)),
    "Foot": ((0.155, 0.005, 0.20), (0.15, -0.13, 0.03)),
    "ToeBase": ((0.15, -0.13, 0.03), (0.155, -0.22, 0.03)),
    "Toe_End": ((0.155, -0.22, 0.03), (0.16, -0.30, 0.03)),
}


def joints():
    """Every bone's (head, tail), by full Mixamo name."""
    result = {f"mixamorig:{name}": (Vector(h), Vector(t)) for name, (h, t) in SPINE.items()}
    for name, (h, t) in LEFT.items():
        result[f"mixamorig:Left{name}"] = (Vector(h), Vector(t))
        mirror = lambda v: Vector((-v[0], v[1], v[2]))
        result[f"mixamorig:Right{name}"] = (mirror(h), mirror(t))
    return result


def _clear_old():
    for obj in list(bpy.data.objects):
        if obj.name.startswith("Thunderhoof") or obj.get("part") is not None:
            bpy.data.objects.remove(obj, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.armatures):
        for data in list(coll):
            if data.users == 0:
                coll.remove(data)


def build_rig():
    """Appends the Wayfarer's skeleton from Hero.blend as ThunderhoofRig and moves its joints. Bone names and the
    hierarchy stay the same, which is all Humanoid retargeting needs."""
    before = set(bpy.data.objects)
    with bpy.data.libraries.load(HERO_BLEND, link=False) as (src, dst):
        dst.objects = ["HeroRig"]
    rig = next(o for o in bpy.data.objects if o not in before)
    rig.name = "ThunderhoofRig"
    rig.data = rig.data.copy()
    rig.data.name = "ThunderhoofRig"
    bpy.context.scene.collection.objects.link(rig)
    bpy.context.view_layer.update()

    to_local = rig.matrix_world.inverted()
    targets = joints()
    bpy.context.view_layer.objects.active = rig
    for obj in bpy.context.view_layer.objects:
        obj.select_set(obj == rig)
    bpy.ops.object.mode_set(mode="EDIT")
    for bone in rig.data.edit_bones:
        if bone.name not in targets:
            continue
        head, tail = targets[bone.name]
        roll = bone.roll
        bone.use_connect = False
        bone.head = to_local @ head
        bone.tail = to_local @ tail
        bone.roll = roll
    bpy.ops.object.mode_set(mode="OBJECT")
    missing = [b.name for b in rig.data.bones if b.name not in targets]
    print(f"ThunderhoofRig: {len(rig.data.bones)} bones, unmoved: {missing}")
    return rig


def ring(part, center, radius, tube, axis=(0, 1, 0), sides=12, tube_sides=6):
    """A torus (nose ring, bracelets) around axis."""
    bm = bmesh.new()
    rotation = Vector((0, 0, 1)).rotation_difference(Vector(axis).normalized()).to_matrix()
    loops = []
    for i in range(sides):
        a = 2 * math.pi * i / sides
        ring_center = Vector((math.cos(a) * radius, math.sin(a) * radius, 0))
        outward = ring_center.normalized()
        loop = []
        for j in range(tube_sides):
            b = 2 * math.pi * j / tube_sides
            local = ring_center + outward * math.cos(b) * tube + Vector((0, 0, math.sin(b) * tube))
            loop.append(bm.verts.new(Vector(center) + rotation @ local))
        loops.append(loop)
    for i in range(sides):
        for j in range(tube_sides):
            a, b = loops[i], loops[(i + 1) % sides]
            bm.faces.new((a[j], a[(j + 1) % tube_sides], b[(j + 1) % tube_sides], b[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return hb._finish(hb._object_from_bmesh(part, bm), part)


def chain(part, points, radii, sides=8):
    """A bent tube through points (horns, the tail), one tapered limb per segment, overlapping at the joints."""
    for i in range(len(points) - 1):
        hb.limb(part, points[i], points[i + 1], radii[i], radii[i + 1], sides=sides, rings=2)
        if 0 < i:
            hb.ball(part, points[i], radii[i] * 1.02, segments=sides, rings=6)


def build_body():
    """Models the Thunderhoof around its skeleton's joints. Returns the joined mesh."""
    hb._pieces.clear()
    for side in (-1, 1):
        x = 0.15 * side
        # --- Legs: thick furry thighs, a shaggy fetlock tuft, and cloven hoof-boots ---
        hb.limb("fur", (x, 0.0, 0.88), (x, 0.0, 0.50), 0.13, 0.115, sides=10)                  # thigh
        hb.ball("fur", (x, 0.0, 0.50), 0.115, segments=10, rings=6)                            # knee
        hb.limb("fur", (x, 0.0, 0.50), (x, 0.0, 0.22), 0.11, 0.09, sides=10)                   # shin
        hb.limb("mane", (x, 0.0, 0.30), (x, 0.0, 0.17), 0.12, 0.135, sides=12, rings=2)        # fetlock tuft
        hb.box("hoof", (x, -0.07, 0.085), (0.22, 0.34, 0.17), bevel=0.05)                      # hoof
        hb.box("mane", (x, -0.235, 0.085), (0.025, 0.04, 0.15), bevel=0.008, subdiv=0)          # cleft

    # --- Kilt and belt (team cloth). Squashed tubes are built bottom-up: hb.limb's turn for a tube pointing straight
    # down is an arbitrary half-turn, which can put the squash on the wrong axis. ---
    hb.limb("cloth", (0, 0, 0.56), (0, 0, 1.04), 0.40, 0.31, sides=16, scale_cross=(1, 0.74))  # kilt
    hb.limb("cloth_trim", (0, 0, 0.53), (0, 0, 0.60), 0.405, 0.398, sides=16, scale_cross=(1, 0.74))
    hb.limb("leather", (0, 0, 0.97), (0, 0, 1.06), 0.315, 0.315, sides=16, scale_cross=(1, 0.76))  # belt
    hb.box("gold", (0, -0.245, 1.015), (0.12, 0.04, 0.09), bevel=0.012, subdiv=0)               # buckle
    for i, x in enumerate((-0.18, -0.06, 0.06, 0.18)):                                          # belt beads
        hb.ball("bead", (x, -0.23 + abs(x) * 0.25, 0.94), 0.025, segments=8, rings=6)
        hb.limb("leather", (x, -0.23 + abs(x) * 0.25, 0.97), (x, -0.23 + abs(x) * 0.25, 0.92), 0.008, 0.008, sides=5, rings=1)

    # --- Torso: a barrel chest under a big shoulder hump and mane ---
    hb.limb("fur", (0, 0, 0.98), (0, 0, 1.58), 0.30, 0.38, sides=16, scale_cross=(1, 0.72))     # chest
    hb.ball("fur_light", (0, -0.17, 1.30), 0.25, scale=(1.05, 0.55, 1.1), segments=14, rings=10)  # chest fur
    hb.ball("fur", (0, 0.07, 1.56), 0.33, scale=(1.25, 0.85, 0.62), subdiv=1)                   # shoulder hump
    hb.ball("mane", (0, 0.06, 1.66), 0.24, scale=(1.15, 1.0, 0.85), subdiv=1)                   # mane
    hb.ball("mane", (0, 0.13, 1.50), 0.2, scale=(1.0, 0.8, 1.0), segments=12, rings=8)          # mane down the back

    # --- Shoulder mantle (team cloth) and a bone-and-feather necklace ---
    hb.ball("cloth", (0, 0.02, 1.57), 0.40, scale=(1.2, 0.8, 0.24), segments=16, rings=8, subdiv=1)  # mantle
    hb.limb("cloth_trim", (0, 0.02, 1.535), (0, 0.02, 1.555), 0.478, 0.475, sides=16, scale_cross=(1, 0.66))
    for i in range(7):
        a = math.radians(-60 + i * 20)
        center = (math.sin(a) * 0.22, -0.33 + (1 - math.cos(a)) * 0.1, 1.45 - (1 - math.cos(a)) * 0.15)
        if i == 3:
            hb.box("bone", center, (0.05, 0.03, 0.12), bevel=0.012, subdiv=0)                   # fang
        else:
            hb.ball("bead" if i % 2 else "bone", center, 0.028, segments=8, rings=6)
    hb.box("feather", (0.07, -0.33, 1.33), (0.035, 0.012, 0.14), bevel=0.01, subdiv=0)
    hb.box("feather", (-0.07, -0.33, 1.33), (0.035, 0.012, 0.14), bevel=0.01, subdiv=0)

    # --- Head: a broad bull head with a pale muzzle, nose ring, ears and curved horns ---
    hb.limb("fur", (0, -0.02, 1.52), (0, -0.04, 1.72), 0.15, 0.14, sides=12)                    # thick neck
    hb.ball("fur", (0, -0.06, 1.84), 0.195, scale=(1.0, 1.05, 1.0), subdiv=1)                   # skull
    hb.ball("fur_light", (0, -0.245, 1.745), 0.14, scale=(1.05, 1.0, 0.78), subdiv=1)           # muzzle
    hb.ball("nose", (0, -0.36, 1.755), 0.07, scale=(1.4, 0.5, 0.75), segments=10, rings=6)      # nose pad
    for side in (-1, 1):
        hb.ball("eye", (0.045 * side, -0.385, 1.765), 0.018, scale=(1, 0.6, 1.2), segments=6, rings=4)  # nostril
        hb.ball("eye", (0.10 * side, -0.205, 1.885), 0.024, scale=(1, 0.6, 1.0), segments=8, rings=6)  # eye
        hb.box("mane", (0.095 * side, -0.21, 1.925), (0.09, 0.03, 0.025), bevel=0.008, subdiv=0)  # brow
        hb.ball("fur", (0.23 * side, -0.03, 1.89), 0.065, scale=(1.7, 0.55, 0.75), segments=10, rings=6)  # ear
        chain("horn", [(0.12 * side, -0.04, 1.95), (0.27 * side, -0.02, 1.99), (0.37 * side, -0.05, 2.08),
                       (0.37 * side, -0.10, 2.17)], [0.06, 0.048, 0.032, 0.008])
    ring("gold", (0, -0.385, 1.705), 0.04, 0.009, axis=(1, 0, 0))                                # nose ring
    hb.ball("mane", (0, -0.09, 2.0), 0.10, scale=(1.2, 1.0, 0.7), segments=10, rings=6)         # forelock
    hb.ball("mane", (0, -0.15, 1.62), 0.09, scale=(1.2, 0.8, 1.0), segments=10, rings=6)        # chin beard

    # --- Arms (T-pose along X): big furry arms, leather bracers with beads, broad hands ---
    for side in (-1, 1):
        sx = side
        hb.ball("fur", (0.44 * sx, 0, 1.53), 0.16, segments=12, rings=8)                         # shoulder
        hb.limb("fur", (0.44 * sx, 0, 1.53), (0.76 * sx, 0, 1.49), 0.14, 0.115, sides=10)       # upper arm
        hb.ball("fur", (0.76 * sx, 0, 1.49), 0.11, segments=10, rings=6)                         # elbow
        hb.limb("fur", (0.76 * sx, 0, 1.49), (1.00 * sx, -0.02, 1.49), 0.11, 0.09, sides=10)    # forearm
        hb.limb("leather", (0.88 * sx, -0.01, 1.49), (1.01 * sx, -0.02, 1.49), 0.105, 0.098, sides=12, rings=2)  # bracer
        ring("bead", (0.62 * sx, 0, 1.51), 0.128, 0.018, axis=(1, 0, 0))                       # arm band
        hb.box("fur_light", (1.11 * sx, -0.025, 1.485), (0.20, 0.17, 0.13), bevel=0.045)         # hand
        hb.box("fur_light", (1.05 * sx, -0.12, 1.49), (0.08, 0.09, 0.075), bevel=0.025)          # thumb
        hb.box("hoof", (1.20 * sx, -0.025, 1.485), (0.04, 0.16, 0.11), bevel=0.015, subdiv=0)    # knuckle nails

    # --- Tail: from the small of the back, hanging down past the kilt, with a dark tuft ---
    chain("tail", [(0, 0.24, 0.98), (0, 0.33, 0.86), (0, 0.37, 0.66), (0, 0.36, 0.48)], [0.04, 0.035, 0.03, 0.028])
    hb.ball("tail_tuft", (0, 0.36, 0.42), 0.07, scale=(0.9, 0.9, 1.4), subdiv=1)

    body = hb._join_pieces("Thunderhoof", PARTS)
    hb._assign_material_slots(body, PARTS, MATERIALS)
    tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
    print(f"Thunderhoof: {tris} tris")
    return body


def build_weights(body, rig, voxel_size=0.02, keep_ratio=0.15):
    """Skins body to rig without Mixamo (Roadmap 4.7).

    The body is dozens of overlapping pieces, which Blender's automatic (heat) weights can't solve. So, like
    build_hero's rig proxy, it's melted into one closed shell first; automatic weights skin that, and each body
    vertex takes the weights of the nearest point on the shell (blended across that triangle), keeps its
    MAX_INFLUENCES strongest bones and is normalized. Parts in RIGID follow one bone outright.
    """
    from mathutils.bvhtree import BVHTree
    from mathutils.interpolate import poly_3d_calc

    proxy = bpy.data.objects.new("Thunderhoof_RigProxy", body.data.copy())
    bpy.context.scene.collection.objects.link(proxy)
    proxy.data.materials.clear()
    remesh = proxy.modifiers.new("Remesh", "REMESH")
    remesh.mode = "VOXEL"
    remesh.voxel_size = voxel_size
    decimate = proxy.modifiers.new("Decimate", "DECIMATE")
    decimate.ratio = keep_ratio
    depsgraph = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(proxy.evaluated_get(depsgraph))
    old = proxy.data
    proxy.modifiers.clear()
    proxy.data = mesh
    bpy.data.meshes.remove(old)

    for obj in bpy.context.view_layer.objects:
        obj.select_set(obj in (proxy, rig))
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    unweighted = sum(1 for v in proxy.data.vertices if not v.groups)
    print(f"Rig proxy: {len(proxy.data.polygons)} faces, {unweighted} vertices automatic weights missed")

    group_names = {g.index: g.name for g in proxy.vertex_groups}
    source_weights = [{group_names[g.group]: g.weight for g in v.groups if g.weight > 0} for v in proxy.data.vertices]
    source_world = [proxy.matrix_world @ v.co for v in proxy.data.vertices]
    polygons = [list(p.vertices) for p in proxy.data.polygons]
    tree = BVHTree.FromPolygons(source_world, polygons)

    # Each vertex's part, from the faces around it.
    names = list(PARTS)
    vertex_part = {}
    indices = body.data.attributes["PartIndex"].data
    for poly in body.data.polygons:
        for v in poly.vertices:
            vertex_part[v] = names[indices[poly.index].value]

    body.vertex_groups.clear()
    bones = [b.name for b in rig.data.bones]
    groups = {name: body.vertex_groups.new(name=name) for name in bones}
    for vertex in body.data.vertices:
        rigid = RIGID.get(vertex_part.get(vertex.index))
        if rigid:
            groups[rigid].add([vertex.index], 1.0, "REPLACE")
            continue
        point, _, face, _ = tree.find_nearest(body.matrix_world @ vertex.co)
        corners = polygons[face]
        blend = poly_3d_calc([source_world[i] for i in corners], point)
        weights = {}
        for corner, amount in zip(corners, blend):
            for bone, weight in source_weights[corner].items():
                weights[bone] = weights.get(bone, 0.0) + weight * amount
        strongest = sorted(weights.items(), key=lambda kv: kv[1], reverse=True)[:MAX_INFLUENCES]
        total = sum(w for _, w in strongest)
        if total <= 0:
            groups["mixamorig:Spine1"].add([vertex.index], 1.0, "REPLACE")
            continue
        for bone, weight in strongest:
            groups[bone].add([vertex.index], weight / total, "REPLACE")

    world = body.matrix_world.copy()
    body.parent = rig
    body.matrix_world = world
    body.modifiers.clear()
    body.modifiers.new("Armature", "ARMATURE").object = rig

    proxy_mesh = proxy.data
    bpy.data.objects.remove(proxy, do_unlink=True)
    bpy.data.meshes.remove(proxy_mesh)
    print(f"Weighted the Thunderhoof: {sum(1 for g in body.vertex_groups)} bones")


def unwrap(body, margin=0.004):
    """Smart-projects UVs onto the body for the bake."""
    for obj in bpy.context.view_layer.objects:
        obj.select_set(obj == body)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=margin)
    bpy.ops.object.mode_set(mode="OBJECT")


def bake(body, size=1024):
    """Paints the part colors plus the Wayfarer's hand-painted shading (AO, light from above, brush noise, worn
    edges; build_hero._build_bake_nodes) into one texture shared by both material slots."""
    image = bpy.data.images.get("Thunderhoof_Albedo") or bpy.data.images.new("Thunderhoof_Albedo", size, size)
    image.scale(size, size)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 64
    scene.render.bake.margin = 8
    for mat in body.data.materials:
        hb._build_bake_nodes(mat, image)
    for obj in bpy.context.view_layer.objects:
        obj.select_set(obj == body)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.bake(type="EMIT", margin=8, use_clear=True)
    os.makedirs(os.path.dirname(ALBEDO_PATH), exist_ok=True)
    image.filepath_raw = ALBEDO_PATH
    image.file_format = "PNG"
    image.save()
    for mat in body.data.materials:
        hb._build_final_nodes(mat, image)
    print(f"Baked {ALBEDO_PATH}")


def export_fbx(rig, body):
    """Exports the rigged Thunderhoof (armature + mesh, no animation) into Unity, like build_hero.export_hero_fbx."""
    for obj in bpy.context.view_layer.objects:
        obj.select_set(obj in (rig, body))
    bpy.context.view_layer.objects.active = rig
    os.makedirs(MODEL_DIR, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=FBX_PATH, use_selection=True, object_types={"ARMATURE", "MESH"},
                             apply_scale_options="FBX_SCALE_UNITS", mesh_smooth_type="FACE",
                             add_leaf_bones=False, bake_anim=False, path_mode="AUTO")
    print(f"Exported {FBX_PATH}")


def build_all():
    """Rebuilds the Thunderhoof from scratch and exports it. Then run Voxelforge > Rebuild Hero Animator in Unity."""
    _clear_old()
    rig = build_rig()
    body = build_body()
    build_weights(body, rig)
    unwrap(body)
    bake(body)
    export_fbx(rig, body)
    return rig, body
