"""Builds "The Wayfarer", Voxelforge's first hero (Roadmap 2.5).

Run inside Blender (Text Editor, or exec over MCP). It rebuilds the hero from scratch in the current file:
  - one mesh object "Hero", T-pose, facing -Y, feet at z = 0, about 1.8 m tall
  - two material slots: Hero_Body (skin, leather, metal) and Hero_TeamCloth (tunic, hood, cloak), which
    Unity tints per player slot
Each part gets a "part" color while modelling; bake_hero() paints them into one texture.

Blender is Z-up with the hero facing -Y; the FBX exporter turns that into Unity's Y-up, facing +Z.
"""
import math
import os

import bmesh
import bpy
from mathutils import Vector

REPO = os.path.abspath(os.path.join(os.path.dirname(bpy.data.filepath or __file__), "..", ".."))

# Base colors per part, linear RGB. TeamCloth parts are light so the per-player tint colors them.
PARTS = {
    "skin": ((0.80, 0.52, 0.38), "Hero_Body"),
    "hair": ((0.24, 0.12, 0.05), "Hero_Body"),
    "eye": ((0.03, 0.03, 0.04), "Hero_Body"),
    "leather": ((0.33, 0.18, 0.08), "Hero_Body"),
    "leather_dark": ((0.16, 0.09, 0.05), "Hero_Body"),
    "pants": ((0.22, 0.20, 0.17), "Hero_Body"),
    "metal": ((0.55, 0.56, 0.60), "Hero_Body"),
    "gold": ((0.85, 0.58, 0.18), "Hero_Body"),
    "cloth": ((0.90, 0.88, 0.84), "Hero_TeamCloth"),
    "cloth_trim": ((0.62, 0.60, 0.56), "Hero_TeamCloth"),
}

_pieces = []


def _finish(obj, part, smooth=True, subdiv=0):
    """Tags a piece with its part, optionally subdivides it, and queues it for joining."""
    obj["part"] = part
    if subdiv:
        mod = obj.modifiers.new("Subdiv", "SUBSURF")
        mod.levels = subdiv
        mod.render_levels = subdiv
    if smooth:
        for poly in obj.data.polygons:
            poly.use_smooth = True
    _pieces.append(obj)
    return obj


def _object_from_bmesh(name, bm):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def box(part, center, size, bevel=0.0, subdiv=1):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=size, verts=bm.verts)
    bmesh.ops.translate(bm, vec=center, verts=bm.verts)
    obj = _object_from_bmesh(part, bm)
    if bevel:
        mod = obj.modifiers.new("Bevel", "BEVEL")
        mod.width = bevel
        mod.segments = 2
    return _finish(obj, part, subdiv=subdiv)


def ball(part, center, radius, scale=(1, 1, 1), segments=12, rings=8, subdiv=0):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segments, v_segments=rings, radius=radius)
    bmesh.ops.scale(bm, vec=scale, verts=bm.verts)
    bmesh.ops.translate(bm, vec=center, verts=bm.verts)
    return _finish(_object_from_bmesh(part, bm), part, subdiv=subdiv)


def limb(part, start, end, r_start, r_end, sides=10, scale_cross=(1, 1), rings=4):
    """A tapered tube from start to end with a few rings (so it bends when skinned), capped at both ends."""
    start, end = Vector(start), Vector(end)
    axis = end - start
    rotation = Vector((0, 0, 1)).rotation_difference(axis.normalized()).to_matrix()
    bm = bmesh.new()
    loops = []
    for r in range(rings + 1):
        t = r / rings
        radius = r_start + (r_end - r_start) * t
        center = start + axis * t
        loop = []
        for i in range(sides):
            a = 2 * math.pi * i / sides
            local = Vector((math.cos(a) * radius * scale_cross[0], math.sin(a) * radius * scale_cross[1], 0))
            loop.append(bm.verts.new(center + rotation @ local))
        loops.append(loop)
    for r in range(rings):
        for i in range(sides):
            j = (i + 1) % sides
            bm.faces.new((loops[r][i], loops[r][j], loops[r + 1][j], loops[r + 1][i]))
    bm.faces.new(list(reversed(loops[0])))
    bm.faces.new(loops[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _finish(_object_from_bmesh(part, bm), part)


def hood():
    """A hood: a sphere shell around the head with the face cut out."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=0.205)
    bmesh.ops.scale(bm, vec=(1.0, 1.05, 1.12), verts=bm.verts)
    bmesh.ops.translate(bm, vec=(0, 0.025, 1.635), verts=bm.verts)
    face_hole = [f for f in bm.faces
                 if f.calc_center_median().y < -0.02 and 1.47 < f.calc_center_median().z < 1.79]
    neck_hole = [f for f in bm.faces if f.calc_center_median().z < 1.47]
    bmesh.ops.delete(bm, geom=face_hole + neck_hole, context="FACES")
    obj = _object_from_bmesh("Hood", bm)
    mod = obj.modifiers.new("Thickness", "SOLIDIFY")
    mod.thickness = 0.025
    mod.offset = 1
    # A little point at the back, like a traveller's hood.
    return _finish(obj, "cloth", subdiv=1)


def cloak():
    """A short cape hanging from the shoulders down the back."""
    bm = bmesh.new()
    rows, cols = 6, 6
    top_z, bottom_z = 1.40, 0.62
    verts = []
    for r in range(rows + 1):
        t = r / rows
        z = top_z + (bottom_z - top_z) * t
        half_width = 0.22 + 0.12 * t
        row = []
        for c in range(cols + 1):
            s = c / cols * 2 - 1
            x = s * half_width
            y = 0.16 + 0.03 * t + 0.05 * (1 - s * s) * (0.4 + t)
            row.append(bm.verts.new((x, y, z)))
        verts.append(row)
    for r in range(rows):
        for c in range(cols):
            bm.faces.new((verts[r][c], verts[r][c + 1], verts[r + 1][c + 1], verts[r + 1][c]))
    obj = _object_from_bmesh("Cloak", bm)
    mod = obj.modifiers.new("Thickness", "SOLIDIFY")
    mod.thickness = 0.02
    return _finish(obj, "cloth")


def build_hero():
    """Deletes any old hero and builds a new one. Returns the joined object."""
    _pieces.clear()
    for obj in list(bpy.data.objects):
        if obj.get("part") is not None or obj.name.startswith("Hero"):
            bpy.data.objects.remove(obj, do_unlink=True)
    for mesh in list(bpy.data.meshes):
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)

    # --- Legs and boots ---
    for side in (-1, 1):
        x = 0.11 * side
        limb("pants", (x, 0.0, 0.20), (x, 0.0, 0.86), 0.088, 0.105)
        box("leather_dark", (x, -0.045, 0.11), (0.20, 0.34, 0.22), bevel=0.04)          # boot
        box("leather", (x, 0.0, 0.27), (0.22, 0.22, 0.10), bevel=0.025)                  # boot cuff

    # --- Body ---
    limb("cloth", (0, 0, 0.90), (0, 0, 1.44), 0.20, 0.25, sides=14, scale_cross=(1, 0.62))  # tunic chest
    limb("cloth", (0, 0, 0.98), (0, 0, 0.56), 0.20, 0.27, sides=12, scale_cross=(1, 0.75))  # tunic skirt
    limb("cloth_trim", (0, 0, 0.60), (0, 0, 0.54), 0.268, 0.272, sides=12, scale_cross=(1, 0.75))  # hem
    limb("leather", (0, 0, 0.94), (0, 0, 0.86), 0.215, 0.215, sides=12, scale_cross=(1, 0.72))  # belt
    box("gold", (0, -0.165, 0.90), (0.09, 0.03, 0.07), bevel=0.01, subdiv=0)                # buckle
    box("leather", (0.14, -0.15, 0.82), (0.10, 0.06, 0.12), bevel=0.02)                     # belt pouch
    limb("leather", (-0.21, -0.16, 1.38), (0.19, -0.14, 0.92), 0.028, 0.028, sides=6)     # chest strap
    cloak()

    # --- Head ---
    limb("skin", (0, 0, 1.38), (0, 0, 1.52), 0.065, 0.06)                                   # neck
    ball("skin", (0, -0.01, 1.63), 0.155, scale=(1.0, 0.98, 1.08), subdiv=1)               # head
    ball("skin", (0, -0.165, 1.61), 0.035, scale=(1, 1, 1.15), segments=8, rings=6)          # nose
    for side in (-1, 1):
        ball("eye", (0.058 * side, -0.142, 1.655), 0.022, scale=(1, 0.6, 1.2), segments=8, rings=6)
        box("hair", (0.06 * side, -0.148, 1.70), (0.08, 0.03, 0.025), bevel=0.008, subdiv=0)  # brow
    ball("hair", (0, -0.075, 1.53), 0.13, scale=(1.0, 0.75, 0.75), subdiv=1)                # beard
    hood()

    # --- Arms (T-pose along X) ---
    for side in (-1, 1):
        sx = side
        limb("cloth", (0.18 * sx, 0, 1.36), (0.50 * sx, 0, 1.36), 0.10, 0.08)                # sleeve
        limb("skin", (0.50 * sx, 0, 1.36), (0.58 * sx, 0, 1.36), 0.072, 0.07)                # forearm
        limb("leather", (0.56 * sx, 0, 1.36), (0.73 * sx, 0, 1.36), 0.082, 0.092)            # bracer
        box("leather_dark", (0.825 * sx, -0.005, 1.355), (0.17, 0.14, 0.10), bevel=0.035)    # hand
        box("leather_dark", (0.77 * sx, -0.09, 1.36), (0.07, 0.08, 0.065), bevel=0.022)      # thumb
        ball("metal", (0.25 * sx, 0, 1.45), 0.15, scale=(1.05, 1.0, 0.72), subdiv=1)          # pauldron
        ball("gold", (0.25 * sx, -0.125, 1.47), 0.028, segments=8, rings=6)                   # rivet

    hero = _join_pieces()
    _assign_material_slots(hero)
    return hero


def _join_pieces():
    """Applies each piece's modifiers and merges them into one mesh. Each face remembers its part's color
    (corner color "Part") and part (face attribute "PartIndex", an index into PARTS)."""
    names = list(PARTS)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    bm = bmesh.new()
    color_layer = bm.loops.layers.float_color.new("Part")
    part_layer = bm.faces.layers.int.new("PartIndex")
    for obj in _pieces:
        part = obj["part"]
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        before = set(bm.faces)
        bm.from_mesh(mesh)
        evaluated.to_mesh_clear()
        color = (*PARTS[part][0], 1.0)
        for face in bm.faces:
            if face in before:
                continue
            face[part_layer] = names.index(part)
            face.smooth = True
            for loop in face.loops:
                loop[color_layer] = color
    for obj in _pieces:
        mesh = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.meshes.remove(mesh)
    return _object_from_bmesh("Hero", bm)


def _assign_material_slots(hero):
    mesh = hero.data
    mesh.materials.clear()
    for name in ("Hero_Body", "Hero_TeamCloth"):
        mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        mat.use_nodes = True
        bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        attr = next((n for n in mat.node_tree.nodes if n.type == "ATTRIBUTE"), None) or mat.node_tree.nodes.new("ShaderNodeAttribute")
        attr.attribute_name = "Part"
        mat.node_tree.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
        bsdf.inputs["Roughness"].default_value = 0.8
        mesh.materials.append(mat)
    # Material indices are set here, not while joining: Blender clamps them to the slots a mesh has.
    names = list(PARTS)
    parts = mesh.attributes["PartIndex"].data
    for poly, part in zip(mesh.polygons, parts):
        poly.material_index = 1 if PARTS[names[part.value]][1] == "Hero_TeamCloth" else 0


ALBEDO_PATH = os.path.join(REPO, "Assets", "_Voxelforge", "Art", "Models", "Hero", "Textures", "Hero_Albedo.png")
TPOSE_FBX_PATH = os.path.join(REPO, "Tools", "Blender", "Export", "Hero_TPose.fbx")


def bake_hero(size=1024):
    """Paints the hero's colors plus hand-painted-style shading into one texture (needs UVs, Cycles).

    Shading is baked into the colors like WoW's painted textures: dark in crevices (ambient occlusion),
    lighter toward the top, a little brush-stroke noise, and bright worn edges. Both material slots use the
    same texture; Unity multiplies the TeamCloth slot by the player's color.
    """
    hero = bpy.data.objects["Hero"]
    image = bpy.data.images.get("Hero_Albedo") or bpy.data.images.new("Hero_Albedo", size, size)
    image.scale(size, size)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 64
    scene.cycles.bake_type = "EMIT"
    scene.render.bake.margin = 8

    for mat in hero.data.materials:
        _build_bake_nodes(mat, image)
    with bpy.context.temp_override(active_object=hero, object=hero, selected_objects=[hero],
                                   selected_editable_objects=[hero]):
        bpy.ops.object.bake(type="EMIT", margin=8, use_clear=True)

    os.makedirs(os.path.dirname(ALBEDO_PATH), exist_ok=True)
    image.filepath_raw = ALBEDO_PATH
    image.file_format = "PNG"
    image.save()
    for mat in hero.data.materials:
        _build_final_nodes(mat, image)
    print(f"Baked {ALBEDO_PATH}")


def _build_bake_nodes(mat, image):
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    emit = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])

    part = nt.nodes.new("ShaderNodeAttribute")
    part.attribute_name = "Part"
    geometry = nt.nodes.new("ShaderNodeNewGeometry")
    coords = nt.nodes.new("ShaderNodeTexCoord")

    def factor(source, from_min, from_max, to_min, to_max):
        node = nt.nodes.new("ShaderNodeMapRange")
        node.inputs["From Min"].default_value = from_min
        node.inputs["From Max"].default_value = from_max
        node.inputs["To Min"].default_value = to_min
        node.inputs["To Max"].default_value = to_max
        nt.links.new(source, node.inputs["Value"])
        return node.outputs["Result"]

    ao = nt.nodes.new("ShaderNodeAmbientOcclusion")
    ao.samples = 16
    ao.inputs["Distance"].default_value = 0.12
    shade_ao = factor(ao.outputs["AO"], 0.0, 1.0, 0.45, 1.0)

    split = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(geometry.outputs["Position"], split.inputs["Vector"])
    shade_top = factor(split.outputs["Z"], 0.0, 1.9, 0.8, 1.12)

    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 28.0
    noise.inputs["Detail"].default_value = 4.0
    nt.links.new(coords.outputs["Object"], noise.inputs["Vector"])
    shade_noise = factor(noise.outputs["Fac"], 0.3, 0.7, 0.9, 1.08)

    shade_edge = factor(geometry.outputs["Pointiness"], 0.5, 0.58, 1.0, 1.18)

    color = part.outputs["Color"]
    for shade in (shade_ao, shade_top, shade_noise, shade_edge):
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs["Factor"].default_value = 1.0
        nt.links.new(color, mix.inputs[6])
        nt.links.new(shade, mix.inputs[7])
        color = mix.outputs[2]
    nt.links.new(color, emit.inputs["Color"])

    target = nt.nodes.new("ShaderNodeTexImage")
    target.image = image
    nt.nodes.active = target


def _build_final_nodes(mat, image):
    """What Unity and Mixamo see: the baked texture on a plain Principled BSDF."""
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = 0.8
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = image
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])


def export_tpose_fbx():
    """Exports the un-rigged hero for Mixamo's auto-rigger, with the texture embedded so Mixamo shows it."""
    hero = bpy.data.objects["Hero"]
    os.makedirs(os.path.dirname(TPOSE_FBX_PATH), exist_ok=True)
    for obj in bpy.context.scene.objects:
        obj.select_set(obj == hero)
    with bpy.context.temp_override(active_object=hero, object=hero, selected_objects=[hero]):
        bpy.ops.export_scene.fbx(filepath=TPOSE_FBX_PATH, use_selection=True, object_types={"MESH"},
                                 apply_scale_options="FBX_SCALE_UNITS", mesh_smooth_type="FACE",
                                 path_mode="COPY", embed_textures=True)
    print(f"Exported {TPOSE_FBX_PATH}")


PROXY_FBX_PATH = os.path.join(REPO, "Tools", "Blender", "Export", "Hero_RigProxy.fbx")


def build_rig_proxy(voxel_size=0.012, keep_ratio=0.1):
    """Melts the hero into one closed shell for Mixamo's auto-rigger.

    The hero is ~60 overlapping pieces (head inside the hood, belt through the tunic, an open cape), and
    Mixamo fails on that ("Unknown error while generating motion"). It rigs this stand-in instead, and
    apply_mixamo_rig() copies the skeleton and weights back onto the real hero.
    """
    hero = bpy.data.objects["Hero"]
    old = bpy.data.objects.get("Hero_RigProxy")
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    proxy = bpy.data.objects.new("Hero_RigProxy", hero.data.copy())
    bpy.context.scene.collection.objects.link(proxy)
    proxy.data.materials.clear()
    remesh = proxy.modifiers.new("Remesh", "REMESH")
    remesh.mode = "VOXEL"
    remesh.voxel_size = voxel_size
    decimate = proxy.modifiers.new("Decimate", "DECIMATE")
    decimate.ratio = keep_ratio

    depsgraph = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(proxy.evaluated_get(depsgraph))
    old_mesh = proxy.data
    proxy.modifiers.clear()
    proxy.data = mesh
    mesh.name = "Hero_RigProxy"
    bpy.data.meshes.remove(old_mesh)

    # Thin parts (the chest strap) can leave tiny scraps after remeshing; keep only real pieces.
    bm = bmesh.new()
    bm.from_mesh(mesh)
    pieces, seen = [], set()
    for vert in bm.verts:
        if vert in seen:
            continue
        piece, stack = [], [vert]
        while stack:
            v = stack.pop()
            if v not in seen:
                seen.add(v)
                piece.append(v)
                stack.extend(e.other_vert(v) for e in v.link_edges)
        pieces.append(piece)
    scraps = [v for piece in pieces if len(piece) < 50 for v in piece]
    bmesh.ops.delete(bm, geom=scraps, context="VERTS")
    islands = len(pieces) - sum(1 for piece in pieces if len(piece) < 50)
    bm.to_mesh(mesh)
    tris = sum(len(f.verts) - 2 for f in bm.faces)
    non_manifold = sum(1 for e in bm.edges if not e.is_manifold)
    bm.free()
    print(f"Rig proxy: {tris} tris, {islands} pieces, {non_manifold} non-manifold edges")
    return proxy


def export_rig_proxy_fbx():
    proxy = bpy.data.objects["Hero_RigProxy"]
    os.makedirs(os.path.dirname(PROXY_FBX_PATH), exist_ok=True)
    for obj in bpy.context.scene.objects:
        obj.select_set(obj == proxy)
    with bpy.context.temp_override(active_object=proxy, object=proxy, selected_objects=[proxy]):
        bpy.ops.export_scene.fbx(filepath=PROXY_FBX_PATH, use_selection=True, object_types={"MESH"},
                                 apply_scale_options="FBX_SCALE_UNITS", mesh_smooth_type="FACE")
    print(f"Exported {PROXY_FBX_PATH}")


RIGGED_PROXY_PATH = os.path.join(REPO, "Tools", "Blender", "Export", "Hero_RigProxy_Rigged.fbx")
HERO_FBX_PATH = os.path.join(REPO, "Assets", "_Voxelforge", "Art", "Models", "Hero", "Hero.fbx")
MAX_INFLUENCES = 4


def apply_mixamo_rig():
    """Puts the skeleton Mixamo made for the stand-in onto the real hero, and exports it for Unity.

    Each hero vertex takes the bone weights of the nearest point on the rigged stand-in's surface
    (blended across that triangle's corners), keeps its 4 strongest bones, and is normalized.
    """
    from mathutils.bvhtree import BVHTree
    from mathutils.interpolate import poly_3d_calc

    before = set(bpy.data.objects)
    window = bpy.context.window_manager.windows[0]
    area = next(a for a in window.screen.areas if a.type == "VIEW_3D")
    with bpy.context.temp_override(window=window, area=area):
        bpy.ops.import_scene.fbx(filepath=RIGGED_PROXY_PATH)
    imported = [o for o in bpy.data.objects if o not in before]
    armature = next(o for o in imported if o.type == "ARMATURE")
    rigged = next(o for o in imported if o.type == "MESH")
    armature.name = armature.data.name = "HeroRig"

    # Weights per stand-in vertex, by bone name.
    group_names = {g.index: g.name for g in rigged.vertex_groups}
    source_weights = [{group_names[g.group]: g.weight for g in v.groups if g.weight > 0} for v in rigged.data.vertices]
    source_world = [rigged.matrix_world @ v.co for v in rigged.data.vertices]
    polygons = [list(p.vertices) for p in rigged.data.polygons]
    tree = BVHTree.FromPolygons(source_world, polygons)

    hero = bpy.data.objects["Hero"]
    hero.hide_set(False)
    hero.vertex_groups.clear()
    groups = {name: hero.vertex_groups.new(name=name) for name in group_names.values()}
    for vertex in hero.data.vertices:
        point, _, face, _ = tree.find_nearest(hero.matrix_world @ vertex.co)
        corners = polygons[face]
        blend = poly_3d_calc([source_world[i] for i in corners], point)
        weights = {}
        for corner, amount in zip(corners, blend):
            for bone, weight in source_weights[corner].items():
                weights[bone] = weights.get(bone, 0.0) + weight * amount
        strongest = sorted(weights.items(), key=lambda kv: kv[1], reverse=True)[:MAX_INFLUENCES]
        total = sum(w for _, w in strongest) or 1.0
        for bone, weight in strongest:
            groups[bone].add([vertex.index], weight / total, "REPLACE")

    world = hero.matrix_world.copy()
    hero.parent = armature
    hero.matrix_world = world
    hero.modifiers.clear()
    hero.modifiers.new("Armature", "ARMATURE").object = armature

    # The stand-ins have done their job.
    for name in ("Hero_RigProxy",):
        if name in bpy.data.objects:
            bpy.data.objects.remove(bpy.data.objects[name], do_unlink=True)
    rigged_mesh = rigged.data
    bpy.data.objects.remove(rigged, do_unlink=True)
    bpy.data.meshes.remove(rigged_mesh)
    print(f"Rigged the hero: {len(hero.vertex_groups)} bones weighted")
    return armature


def export_hero_fbx():
    """Exports the rigged hero (armature + mesh, no animation) into Unity."""
    armature = bpy.data.objects["HeroRig"]
    hero = bpy.data.objects["Hero"]
    for obj in bpy.context.scene.objects:
        obj.select_set(obj in (armature, hero))
    os.makedirs(os.path.dirname(HERO_FBX_PATH), exist_ok=True)
    with bpy.context.temp_override(active_object=armature, object=armature, selected_objects=[armature, hero]):
        bpy.ops.export_scene.fbx(filepath=HERO_FBX_PATH, use_selection=True, object_types={"ARMATURE", "MESH"},
                                 apply_scale_options="FBX_SCALE_UNITS", mesh_smooth_type="FACE",
                                 add_leaf_bones=False, bake_anim=False, path_mode="AUTO")
    print(f"Exported {HERO_FBX_PATH}")
