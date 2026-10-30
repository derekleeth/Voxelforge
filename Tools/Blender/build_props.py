"""Builds Voxelforge's props and tools (Roadmap 3.5): pickaxes, an axe, The Pick of Destiny, torches, the workbench,
Kian's M.G.R.S. (Magic Gun Rifle Staff, used in 3.6), the Home Totem (3.8), the three wearable bags, the
drawstring loot bag and the Storage Chest (4.5), the Training Dummy, the Rune Hammer and the Stone Spear (4.6).

Run inside Blender (Text Editor, or exec over MCP). build_all() rebuilds everything in the current file:
  - paints one 1024 x 1024 atlas into Art/Textures/Props/Props_Atlas.png, plus a glow map (Props_Glow.png) that is
    black except for flames, mana crystals and runes
  - builds every model, laid out in a row to look at
  - exports one FBX per model into Art/Models/Props/

Mesh contract:
  - 1 unit = 1 block (1 m); Z up here, the export bakes Unity's Y up into the vertices
  - with the import settings in FoliageImportSettings.cs, Blender +X is Unity +X and Blender +Y is Unity +Z
  - tools: origin where the hand grips, handle along +Z, head at the top, a blade (if any) on the +X side
  - world props: origin at the bottom centre of their block; the wall torch has its wall on the +Y side
    (Unity +Z, see PropAttach.Wall)
  - the chest lid (4.5): origin on the hinge (the back top edge of the body, Unity (0, 0.5, 0.31) from the block's
    bottom centre), the lid reaching forward along -Y (Unity -Z); the game turns it about X to open it
  - worn bags (4.5): origin at the middle of the side that rests on the hero's back, the bag hanging out along -Y
    (Unity -Z, behind a hero facing +Z), +Z up. BackpackView puts that point on the hero's back
  - UV0 = the part's area of the atlas; vertex colour RGB = 1 (shading comes from the texture), A = sway
    (only flame tips sway, through Voxelforge/Foliage's wind)

Everything is our own, drawn by code (no photos), so it can be rebuilt and tweaked.
"""
import math
import os
import random

import bpy
import numpy as np
from mathutils import Matrix, Vector

REPO = os.path.abspath(os.path.join(os.path.dirname(bpy.data.filepath or __file__), "..", ".."))
ART = os.path.join(REPO, "Assets", "_Voxelforge", "Art")
ATLAS_PATH = os.path.join(ART, "Textures", "Props", "Props_Atlas.png")
GLOW_PATH = os.path.join(ART, "Textures", "Props", "Props_Glow.png")
MODEL_DIR = os.path.join(ART, "Models", "Props")

SIZE = 1024
CELL = SIZE // 4

# Atlas areas on a 4 x 4 grid: name -> (column, row), row 0 at the top.
GRID = {
    "wood": (0, 0), "wood_dark": (1, 0), "planks": (2, 0), "stone": (3, 0),
    "iron": (0, 1), "steel": (1, 1), "gold": (2, 1), "leather": (3, 1),
    "cloth": (0, 2), "flame": (1, 2), "mana": (2, 2), "rune": (3, 2),
    "totem": (0, 3), "hearth": (1, 3), "canvas": (2, 3), "rope": (3, 3),
}


def region(name):
    col, row = GRID[name]
    return col * 0.25, 1.0 - (row + 1) * 0.25, (col + 1) * 0.25, 1.0 - row * 0.25


def rgb(r, g, b):
    return np.array([r, g, b], np.float32) / 255.0


# ----------------------------------------------------------------------------------------------------------------
# Painting. Arrays are (rows, columns) with row 0 at the BOTTOM, like Blender's pixels; x and y run 0..1.
# ----------------------------------------------------------------------------------------------------------------

def noise(n, cells, rng, octaves=3):
    """Smooth value noise, 0..1."""
    out = np.zeros((n, n), np.float32)
    amp, total = 1.0, 0.0
    for o in range(octaves):
        c = cells * 2 ** o
        g = rng.random((c + 1, c + 1)).astype(np.float32)
        t = np.linspace(0.0, c, n, endpoint=False)
        i = t.astype(int)
        f = t - i
        f = f * f * (3.0 - 2.0 * f)
        fx, fy = f[None, :], f[:, None]
        a, b = g[i][:, i], g[i][:, i + 1]
        c0, d = g[i + 1][:, i], g[i + 1][:, i + 1]
        out += amp * ((a * (1 - fx) + b * fx) * (1 - fy) + (c0 * (1 - fx) + d * fx) * fy)
        total += amp
        amp *= 0.5
    return out / total


def grid(n):
    t = (np.arange(n, dtype=np.float32) + 0.5) / n
    return np.meshgrid(t, t)  # x across, y up


def tint(base, amount):
    return base[None, None, :] * amount[..., None]


def paint_wood(rng, base, dark, rings=12.0):
    x, y = grid(CELL)
    n = noise(CELL, 4, rng)
    warp = noise(CELL, 2, rng)
    grain = np.sin((x * rings + warp * 2.5 + n * 0.6) * math.pi * 2.0) * 0.5 + 0.5
    lines = np.clip((grain - 0.78) * 5.0, 0.0, 1.0)
    colour = tint(base, 0.85 + 0.3 * n)
    colour = colour * (1 - lines[..., None]) + dark[None, None, :] * lines[..., None]
    return colour, np.zeros_like(colour)


def paint_planks(rng):
    colour, glow = paint_wood(rng, rgb(170, 118, 70), rgb(105, 66, 36), rings=20.0)
    x, y = grid(CELL)
    board = np.floor(x * 4.0)
    shade = 0.88 + 0.12 * np.sin(board * 2.7)
    gap = (np.abs(x * 4.0 - np.round(x * 4.0)) < 0.05).astype(np.float32)
    nails = np.zeros_like(x)
    for bx in range(4):
        for ny in (0.1, 0.9):
            nails += np.exp(-(((x - (bx + 0.5) / 4) * CELL) ** 2 + ((y - ny) * CELL) ** 2) / 8.0)
    colour = colour * shade[..., None] * (1 - 0.75 * gap[..., None])
    colour = colour * (1 - nails[..., None]) + rgb(70, 70, 75)[None, None, :] * nails[..., None]
    return colour, glow


def paint_stone(rng):
    n = noise(CELL, 5, rng, 4)
    spots = noise(CELL, 16, rng, 2)
    colour = tint(rgb(128, 126, 122), 0.7 + 0.5 * n)
    colour *= (1 - 0.35 * np.clip((spots - 0.62) * 6, 0, 1))[..., None]
    return colour, np.zeros_like(colour)


def paint_metal(rng, base, streak=0.25):
    x, y = grid(CELL)
    n = noise(CELL, 3, rng)
    brushed = noise(CELL, 64, rng, 1)[:, :1].repeat(CELL, axis=1).T  # Streaks along the grain (y).
    shine = 0.85 + 0.25 * np.sin(x * math.pi)
    colour = tint(base, (0.8 + 0.3 * n + streak * (brushed - 0.5)) * shine)
    return colour, np.zeros_like(colour)


def paint_wrap(rng, base, dark, soot=0.0):
    x, y = grid(CELL)
    n = noise(CELL, 6, rng)
    wrap = (np.mod(y * 7.0 + x * 1.2, 1.0) < 0.12).astype(np.float32)
    colour = tint(base, 0.8 + 0.35 * n)
    colour = colour * (1 - 0.6 * wrap[..., None]) + dark[None, None, :] * 0.6 * wrap[..., None]
    if soot:
        colour *= (1 - soot * np.clip(y - 0.4, 0, 1) / 0.6)[..., None]
    return colour, np.zeros_like(colour)


def paint_flame(rng):
    x, y = grid(CELL)
    n = noise(CELL, 5, rng)
    stops = [(0.0, rgb(255, 250, 200)), (0.35, rgb(255, 205, 70)), (0.7, rgb(255, 120, 25)), (1.0, rgb(220, 50, 15))]
    t = np.clip(y + (n - 0.5) * 0.25, 0, 1)
    colour = np.zeros((CELL, CELL, 3), np.float32)
    for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
        k = np.clip((t - t0) / (t1 - t0), 0, 1)[..., None]
        inside = ((t >= t0) & (t <= t1))[..., None]
        colour = np.where(inside, c0 * (1 - k) + c1 * k, colour)
    return colour, colour.copy()


def paint_mana(rng):
    x, y = grid(CELL)
    n = noise(CELL, 3, rng, 2)
    facets = noise(CELL, 8, rng, 1)
    k = np.clip(n * 1.4 - 0.2, 0, 1)[..., None]
    colour = rgb(150, 70, 245) * (1 - k) + rgb(90, 220, 255) * k
    colour = colour * (0.75 + 0.5 * facets[..., None])
    sparkle = np.clip((noise(CELL, 32, rng, 1) - 0.8) * 8, 0, 1)[..., None]
    colour = colour * (1 - sparkle) + sparkle
    return colour, colour * 0.9


def paint_rune(rng):
    colour, _ = paint_wood(rng, rgb(70, 46, 34), rgb(40, 25, 18), rings=9.0)
    x, y = grid(CELL)
    glyph = np.zeros((CELL, CELL), np.float32)
    rows, cols = 6, 2
    for gy in range(rows):
        for gx in range(cols):
            cx, cy = (gx + 0.5) / cols, (gy + 0.5) / rows
            for _ in range(3):  # Each rune is a few straight strokes.
                a = Vector((cx + rng.uniform(-0.18, 0.18), cy + rng.uniform(-0.05, 0.05)))
                b = Vector((cx + rng.uniform(-0.18, 0.18), cy + rng.uniform(-0.05, 0.05)))
                d = b - a
                length2 = max(d.length_squared, 1e-6)
                s = np.clip(((x - a.x) * d.x + (y - a.y) * d.y) / length2, 0, 1)
                dist = np.hypot(x - (a.x + s * d.x), (y - (a.y + s * d.y)))
                glyph = np.maximum(glyph, np.clip(1.0 - dist * CELL / 2.2, 0, 1))
    rune = rgb(110, 235, 255)
    colour = colour * (1 - glyph[..., None]) + rune * glyph[..., None]
    return colour, rune * glyph[..., None]


def paint_totem(rng):
    """Weathered, carved wood for the Home Totem: tall grain with a few chiselled bands across it."""
    colour, glow = paint_wood(rng, rgb(150, 100, 60), rgb(88, 56, 32), rings=7.0)
    x, y = grid(CELL)
    bands = np.clip(1.0 - np.abs(np.mod(y * 5.0, 1.0) - 0.5) * 22.0, 0, 1)
    colour = colour * (1 - 0.45 * bands[..., None])
    return colour, glow


def paint_hearth(rng):
    """The Home Totem's green glow: bright in the middle of each carved line, deeper green at the edges."""
    x, y = grid(CELL)
    n = noise(CELL, 4, rng, 2)
    k = np.clip(n * 1.3 - 0.15, 0, 1)[..., None]
    colour = rgb(40, 200, 80) * (1 - k) + rgb(170, 255, 170) * k
    return colour, colour * 0.95


def paint_canvas(rng):
    """Woven travelling canvas for bags: a fine over-under weave, a little sun-bleached and grubby at the bottom."""
    x, y = grid(CELL)
    n = noise(CELL, 5, rng)
    weave = 0.5 + 0.5 * np.sin(x * math.pi * 64) * np.sin(y * math.pi * 64)
    colour = tint(rgb(196, 172, 124), 0.82 + 0.22 * n + 0.1 * weave)
    colour *= (1 - 0.3 * np.clip(0.35 - y, 0, 1) / 0.35)[..., None]
    return colour, np.zeros_like(colour)


def paint_rope(rng):
    """Twisted hemp rope: diagonal strands with dark grooves between them, running along v."""
    x, y = grid(CELL)
    n = noise(CELL, 6, rng)
    twist = np.mod(y * 10.0 + x * 2.5, 1.0)
    groove = np.clip(1.0 - np.abs(twist - 0.5) * 8.0, 0, 1)
    colour = tint(rgb(176, 142, 92), 0.85 + 0.25 * n)
    colour = colour * (1 - 0.55 * groove[..., None])
    return colour, np.zeros_like(colour)


def paint_atlas(seed=35):
    rng = np.random.default_rng(seed)
    painters = {
        "wood": lambda: paint_wood(rng, rgb(178, 128, 78), rgb(118, 78, 44)),
        "wood_dark": lambda: paint_wood(rng, rgb(98, 62, 40), rgb(58, 35, 22)),
        "planks": lambda: paint_planks(rng),
        "stone": lambda: paint_stone(rng),
        "iron": lambda: paint_metal(rng, rgb(86, 90, 100)),
        "steel": lambda: paint_metal(rng, rgb(196, 206, 220), 0.35),
        "gold": lambda: paint_metal(rng, rgb(235, 182, 70), 0.3),
        "leather": lambda: paint_wrap(rng, rgb(112, 70, 42), rgb(60, 36, 22)),
        "cloth": lambda: paint_wrap(rng, rgb(150, 122, 86), rgb(80, 62, 42), soot=0.55),
        "flame": lambda: paint_flame(rng),
        "mana": lambda: paint_mana(rng),
        "rune": lambda: paint_rune(rng),
        # Added after the rest so their random numbers don't change the older areas.
        "totem": lambda: paint_totem(rng),
        "hearth": lambda: paint_hearth(rng),
        "canvas": lambda: paint_canvas(rng),
        "rope": lambda: paint_rope(rng),
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


def save_image(name, pixels, path):
    image = bpy.data.images.get(name)
    if image is None or tuple(image.size) != (SIZE, SIZE):
        if image is not None:
            bpy.data.images.remove(image)
        image = bpy.data.images.new(name, SIZE, SIZE, alpha=True)
    image.pixels.foreach_set(pixels.ravel())  # Already bottom-up.
    os.makedirs(os.path.dirname(path), exist_ok=True)
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    print(f"Saved {path}")
    return image


# ----------------------------------------------------------------------------------------------------------------
# Shapes. Every shape is a list of faces, each a list of corner positions, facing outward.
# ----------------------------------------------------------------------------------------------------------------

def face_normal(face):
    n = Vector((0.0, 0.0, 0.0))
    for i, a in enumerate(face):  # Newell's method: works for any flat polygon.
        b = face[(i + 1) % len(face)]
        n.x += (a.y - b.y) * (a.z + b.z)
        n.y += (a.z - b.z) * (a.x + b.x)
        n.z += (a.x - b.x) * (a.y + b.y)
    return n.normalized() if n.length > 1e-9 else Vector((0.0, 0.0, 1.0))


def centre(points):
    return sum(points, Vector()) / len(points)


def facing(face, outward):
    """face, reversed if its normal points against outward."""
    return face if face_normal(face).dot(outward) >= 0 else list(reversed(face))


def frame(axis):
    """Two unit vectors at right angles to axis and each other."""
    axis = axis.normalized()
    helper = Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((1, 0, 0))
    u = axis.cross(helper).normalized()
    return u, axis.cross(u).normalized()


def convex(points_faces):
    """Orients each face of a convex shape away from its middle."""
    middle = centre([p for f in points_faces for p in f])
    return [facing(f, centre(f) - middle) for f in points_faces]


def cylinder(p0, p1, r0, r1, sides=8, caps=True, phase=0.0):
    p0, p1 = Vector(p0), Vector(p1)
    u, w = frame(p1 - p0)
    def ring(p, r):
        return [p + (u * math.cos(phase + k * 2 * math.pi / sides) + w * math.sin(phase + k * 2 * math.pi / sides)) * r
                for k in range(sides)]
    a, b = ring(p0, r0), ring(p1, r1)
    faces = [[a[k], a[(k + 1) % sides], b[(k + 1) % sides], b[k]] for k in range(sides)]
    if caps:
        if r0 > 1e-5:
            faces.append(a)
        if r1 > 1e-5:
            faces.append(b)
    return convex([f for f in faces if len({tuple(round(c, 6) for c in p) for p in f}) >= 3])


def box(c, size, rotation=Matrix.Identity(3)):
    c, hx, hy, hz = Vector(c), size[0] / 2, size[1] / 2, size[2] / 2
    corners = [c + rotation @ Vector((sx * hx, sy * hy, sz * hz)) for sz in (-1, 1) for sy in (-1, 1) for sx in (-1, 1)]
    idx = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    return convex([[corners[i] for i in f] for f in idx])


def sweep(points, sizes, up_hint=Vector((0, 0, 1))):
    """A rectangular tube along points; sizes[i] = (height across up_hint, thickness). Capped at both ends."""
    points = [Vector(p) for p in points]
    rings = []
    for i, p in enumerate(points):
        tangent = (points[min(i + 1, len(points) - 1)] - points[max(i - 1, 0)]).normalized()
        side = tangent.cross(up_hint).normalized()
        up = side.cross(tangent).normalized()
        h, t = sizes[i]
        rings.append([p + up * h / 2 + side * t / 2, p + up * h / 2 - side * t / 2,
                      p - up * h / 2 - side * t / 2, p - up * h / 2 + side * t / 2])
    faces = []
    for i in range(len(rings) - 1):
        a, b = rings[i], rings[i + 1]
        mid = (points[i] + points[i + 1]) / 2
        for k in range(4):
            f = [a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k]]
            faces.append(facing(f, centre(f) - mid))
    faces.append(facing(list(rings[0]), points[0] - points[1]))
    faces.append(facing(list(rings[-1]), points[-1] - points[-2]))
    return faces


def slab(outline, origin, a, b, thickness):
    """outline (2D, in the plane through origin along a and b) extruded both ways along a x b.
    thickness(i) gives the full thickness at corner i, so blades can be sharp at the edge."""
    origin, a, b = Vector(origin), Vector(a), Vector(b)
    n = a.cross(b).normalized()
    area = sum(outline[i][0] * outline[(i + 1) % len(outline)][1] - outline[(i + 1) % len(outline)][0] * outline[i][1]
               for i in range(len(outline)))
    pts = outline if area > 0 else list(reversed(outline))
    th = [thickness(outline.index(p)) for p in pts]
    base = [origin + a * x + b * y for x, y in pts]
    front = [p + n * t / 2 for p, t in zip(base, th)]
    back = [p - n * t / 2 for p, t in zip(base, th)]
    faces = [facing(front, n), facing(list(back), -n)]
    for i in range(len(pts)):
        j = (i + 1) % len(pts)
        dx, dy = pts[j][0] - pts[i][0], pts[j][1] - pts[i][1]
        out = a * dy - b * dx  # Outward for a counter-clockwise outline.
        faces.append(facing([back[i], back[j], front[j], front[i]], out))
    return faces


def gem(c, radii, axis=Vector((0, 0, 1))):
    c = Vector(c)
    u, w = frame(axis)
    ax = Vector(axis).normalized()
    tips = [c + ax * radii[2], c - ax * radii[2]]
    belt = [c + u * radii[0], c + w * radii[1], c - u * radii[0], c - w * radii[1]]
    faces = [[t, belt[k], belt[(k + 1) % 4]] for t in tips for k in range(4)]
    return convex(faces)


# ----------------------------------------------------------------------------------------------------------------
# Meshes
# ----------------------------------------------------------------------------------------------------------------

class MeshBuilder:
    """Flat-shaded faces with their own corners: position, normal, uv and colour (1, 1, 1, sway)."""

    def __init__(self):
        self.positions, self.normals, self.uvs, self.colours, self.faces = [], [], [], [], []

    def part(self, name, faces, grain=Vector((0, 0, 1)), sway=None):
        """Adds faces textured with atlas area name. The texture runs along grain (v) and around it (u), stretched
        over the whole part; faces looking along grain use the other two axes. sway(p) gives the wind sway."""
        g = Vector(grain).normalized()
        u, w = frame(g)
        points = [p for f in faces for p in f]
        def span(axis):
            values = [p.dot(axis) for p in points]
            return min(values), max(max(values) - min(values), 1e-6)
        spans = {"g": span(g), "u": span(u), "w": span(w)}
        u0, v0, u1, v1 = region(name)
        pad = 2.0 / SIZE
        def to_uv(s, t):
            return u0 + pad + s * (u1 - u0 - 2 * pad), v0 + pad + t * (v1 - v0 - 2 * pad)
        def coord(p, key, axis):
            lo, size = spans[key]
            return (p.dot(axis) - lo) / size
        for f in faces:
            n = face_normal(f)
            if abs(n.dot(g)) > 0.75:
                axes = (("u", u), ("w", w))
            else:
                across = ("u", u) if abs(n.dot(u)) < abs(n.dot(w)) else ("w", w)
                axes = (across, ("g", g))
            start = len(self.positions)
            for p in f:
                self.positions.append(tuple(p))
                self.normals.append(tuple(n))
                self.uvs.append(to_uv(coord(p, *axes[0]), coord(p, *axes[1])))
                self.colours.append((1.0, 1.0, 1.0, sway(p) if sway else 0.0))
            self.faces.append(tuple(range(start, start + len(f))))

    def triangle_count(self):
        return sum(len(f) - 2 for f in self.faces)

    def build(self, name, material, collection):
        old = bpy.data.objects.get(name)
        if old is not None:
            bpy.data.objects.remove(old)
        if name in bpy.data.meshes:
            bpy.data.meshes.remove(bpy.data.meshes[name])
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(self.positions, [], self.faces)  # Every corner is its own vertex, so loop i = vertex i.
        mesh.uv_layers.new(name="UVMap").data.foreach_set("uv", [c for uv in self.uvs for c in uv])
        colours = mesh.color_attributes.new("Col", "FLOAT_COLOR", "CORNER")
        colours.data.foreach_set("color", [c for colour in self.colours for c in colour])
        mesh.color_attributes.active_color = colours
        mesh.update()
        mesh.normals_split_custom_set(self.normals)
        mesh.materials.append(material)
        obj = bpy.data.objects.new(name, mesh)
        collection.objects.link(obj)
        return obj


# ----------------------------------------------------------------------------------------------------------------
# Models
# ----------------------------------------------------------------------------------------------------------------

def handle(b, region_name, bottom=-0.12, top=0.62, radius=0.028):
    b.part(region_name, cylinder((0, 0, bottom), (0, 0, top), radius * 1.1, radius, 8, phase=math.pi / 8))
    b.part(region_name, cylinder((0, 0, bottom - 0.025), (0, 0, bottom), radius * 0.8, radius * 1.25, 8))  # Knob.


def pick_head(b, region_name, z, half_length, height, thickness, droop):
    """A curved pick: thickest at the handle, drooping to a point at both ends."""
    xs = [half_length * (i / 6.0) for i in range(-6, 7)]
    points = [(x, 0.0, z - droop * (x / half_length) ** 2) for x in xs]
    sizes = [(height * (1 - 0.82 * abs(x) / half_length), thickness * (1 - 0.6 * abs(x) / half_length)) for x in xs]
    b.part(region_name, sweep(points, sizes, Vector((0, 0, 1))), grain=Vector((1, 0, 0)))


def wrap(b, z0, z1, radius, region_name="leather"):
    b.part(region_name, cylinder((0, 0, z0), (0, 0, z1), radius, radius, 8, phase=math.pi / 8))


def build_wooden_pickaxe():
    b = MeshBuilder()
    handle(b, "wood")
    pick_head(b, "wood_dark", 0.6, 0.28, 0.08, 0.06, 0.08)
    wrap(b, 0.52, 0.66, 0.036)
    return b


def build_stone_pickaxe():
    b = MeshBuilder()
    handle(b, "wood")
    pick_head(b, "stone", 0.6, 0.31, 0.1, 0.075, 0.09)
    wrap(b, 0.5, 0.67, 0.038)
    return b


def axe_blade(b, region_name, z, reach, half_height, spine, edge, flare=0.0):
    """An axe blade in the XZ plane on the +X side, thick at the handle and sharp at the edge."""
    # Neck from the handle, flaring into a curved (crescent) cutting edge.
    rim = [(reach * (1.0 + 0.12 * (1.0 - t * t)), z + (half_height + flare) * t) for t in np.linspace(-1.0, 1.0, 7)]
    outline = [(0.02, z - half_height * 0.4), (reach * 0.45, z - half_height * 0.35), (reach * 0.8, z - half_height * 0.7 - flare)]
    outline += rim
    outline += [(reach * 0.8, z + half_height * 0.7 + flare), (reach * 0.45, z + half_height * 0.35), (0.02, z + half_height * 0.4)]
    def thickness(i):
        x = outline[i][0]
        return spine + (edge - spine) * min(max((x - 0.02) / (reach - 0.02), 0.0), 1.0)
    b.part(region_name, slab(outline, (0, 0, 0), (1, 0, 0), (0, 0, 1), thickness), grain=Vector((1, 0, 0)))


def build_stone_axe():
    b = MeshBuilder()
    handle(b, "wood")
    axe_blade(b, "stone", 0.56, 0.22, 0.1, 0.075, 0.016)
    b.part("stone", box((-0.04, 0, 0.56), (0.07, 0.07, 0.11)))  # The butt behind the handle.
    wrap(b, 0.47, 0.64, 0.037)
    return b


def build_pick_of_destiny():
    """Kian's axe-pick: a steel axe blade on one side, a long pick on the other, gold trim and a mana crystal heart."""
    b = MeshBuilder()
    handle(b, "wood_dark", bottom=-0.16, top=0.74, radius=0.031)
    for z in (-0.12, 0.08, 0.36):
        b.part("gold", cylinder((0, 0, z - 0.018), (0, 0, z + 0.018), 0.037, 0.037, 8, phase=math.pi / 8))
    z = 0.7
    b.part("steel", box((0, 0, z), (0.12, 0.1, 0.16)))
    b.part("gold", box((0, 0, z + 0.085), (0.13, 0.11, 0.018)))
    b.part("gold", box((0, 0, z - 0.085), (0.13, 0.11, 0.018)))
    axe_blade(b, "steel", z, 0.34, 0.15, 0.08, 0.014, flare=0.04)
    # The pick: long, curving down to a point on the -X side.
    xs = [-0.05 - 0.36 * (i / 8.0) for i in range(9)]
    points = [(x, 0.0, z - 0.11 * ((x + 0.05) / 0.36) ** 2) for x in xs]
    sizes = [(0.085 * (1 - 0.9 * i / 8.0), 0.07 * (1 - 0.7 * i / 8.0)) for i in range(9)]
    b.part("steel", sweep(points, sizes), grain=Vector((1, 0, 0)))
    # A glowing mana crystal set into both faces of the head, and one more crowning it.
    b.part("mana", gem((0, 0.055, z), (0.035, 0.035, 0.05), axis=Vector((0, 0, 1))))
    b.part("mana", gem((0, -0.055, z), (0.035, 0.035, 0.05), axis=Vector((0, 0, 1))))
    b.part("mana", gem((0, 0, z + 0.14), (0.03, 0.03, 0.06)))
    return b


def flame(b, base, height, radius):
    """Two nested low-poly flames. Tips sway in the wind."""
    base = Vector(base)
    sway = lambda p: min(max((p.z - base.z) / height, 0.0), 1.0) * 0.5
    outer = [base + Vector((0, 0, h * height)) for h in (0.0, 0.3, 0.65, 1.0)]
    radii = [radius * 0.55, radius, radius * 0.6, 0.0]
    for i in range(3):
        b.part("flame", cylinder(outer[i], outer[i + 1], radii[i], radii[i + 1], 6, caps=i == 0, phase=0.3), sway=sway)
    inner = base + Vector((0, 0, height * 0.12))
    b.part("flame", cylinder(inner, inner + Vector((0, 0, height * 0.55)), radius * 0.7, 0.0, 5, phase=1.0), sway=sway)


def torch(b, base, direction):
    """A torch from base along direction: stick, oily cloth and flame (the flame always points up)."""
    base, d = Vector(base), Vector(direction).normalized()
    top = base + d * 0.5
    b.part("wood", cylinder(base, top, 0.03, 0.038, 6))
    cloth0 = base + d * 0.4
    b.part("cloth", cylinder(cloth0, base + d * 0.56, 0.05, 0.058, 6, phase=0.4))
    flame(b, base + d * 0.53, 0.3, 0.07)


def build_torch_floor():
    b = MeshBuilder()
    torch(b, (0, 0, 0), (0, 0, 1))
    return b


def build_torch_wall():
    """Hung on a wall on the +Y side (Unity +Z), leaning out of it."""
    b = MeshBuilder()
    lean = math.radians(25)
    base = Vector((0, 0.43, 0.22))
    direction = Vector((0, -math.sin(lean), math.cos(lean)))
    torch(b, base, direction)
    b.part("iron", box((0, 0.485, 0.36), (0.12, 0.03, 0.2)))  # The plate on the wall.
    ring = base + direction * 0.2
    b.part("iron", box((0, (ring.y + 0.485) / 2, ring.z), (0.035, 0.485 - ring.y, 0.03)))  # Arm out to the stick.
    b.part("iron", cylinder(ring - direction * 0.02, ring + direction * 0.02, 0.048, 0.048, 6))
    return b


def build_workbench():
    b = MeshBuilder()
    top_z, top_t, half = 0.88, 0.08, 0.46
    b.part("planks", box((0, 0, top_z + top_t / 2), (half * 2, half * 2, top_t)), grain=Vector((0, 1, 0)))
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.part("wood_dark", box((sx * (half - 0.06), sy * (half - 0.06), top_z / 2), (0.09, 0.09, top_z)))
    b.part("planks", box((0, 0, 0.22), (half * 2 - 0.1, half * 2 - 0.1, 0.04)), grain=Vector((1, 0, 0)))  # Shelf.
    for sx in (-1, 1):  # Rails under the top.
        b.part("wood_dark", box((sx * (half - 0.06), 0, top_z - 0.05), (0.06, half * 2 - 0.12, 0.08)), grain=Vector((0, 1, 0)))
    # A hammer lying on top, and a vice on one corner.
    z = top_z + top_t
    hammer = Matrix.Rotation(math.radians(30), 3, "Z")
    b.part("wood", box(Vector((0.05, -0.12, z + 0.018)), (0.36, 0.035, 0.035), hammer), grain=hammer @ Vector((1, 0, 0)))
    b.part("iron", box(Vector((0.05, -0.12, z)) + hammer @ Vector((0.18, 0, 0.035)), (0.06, 0.13, 0.06), hammer))
    b.part("iron", box((-0.3, 0.3, z + 0.06), (0.16, 0.12, 0.12)))
    b.part("iron", box((-0.3, 0.3, z + 0.13), (0.04, 0.2, 0.02)))
    # A couple of logs on the shelf.
    for i, x in enumerate((-0.18, 0.02)):
        b.part("wood", cylinder((x, -0.32, 0.29), (x, 0.32, 0.29), 0.07, 0.07, 7, phase=i), grain=Vector((0, 1, 0)))
    return b


def build_mgrs():
    """Kian's M.G.R.S. (Magic Gun Rifle Staff): a wizard's staff with a rifle stock and a glowing barrel crown,
    plus a mana crystal scope. It shoots out of the top end (3.6)."""
    b = MeshBuilder()
    # Shaft, with a glowing rune band.
    b.part("wood_dark", cylinder((0, 0, -0.5), (0, 0, 0.78), 0.032, 0.03, 8, phase=math.pi / 8))
    b.part("rune", cylinder((0, 0, 0.16), (0, 0, 0.5), 0.034, 0.034, 8, phase=math.pi / 8))
    for z in (0.14, 0.52, 0.76):
        b.part("gold", cylinder((0, 0, z - 0.02), (0, 0, z + 0.02), 0.042, 0.042, 8, phase=math.pi / 8))
    # Rifle stock at the bottom end, and a trigger in front of the hand.
    stock = [(-0.035, -0.46), (0.03, -0.46), (0.07, -0.62), (0.07, -0.8), (-0.06, -0.8), (-0.05, -0.62)]
    b.part("wood", slab(stock, (0, 0, 0), (1, 0, 0), (0, 0, 1), lambda i: 0.06))
    b.part("gold", box((0.005, 0, -0.81), (0.14, 0.065, 0.025)))
    b.part("iron", box((0.045, 0, 0.06), (0.04, 0.05, 0.1)))
    b.part("iron", box((0.075, 0, 0.03), (0.02, 0.012, 0.05)))  # Trigger.
    # The barrel crown: an iron barrel, a gold muzzle, and two curling prongs like a wizard's staff.
    b.part("iron", cylinder((0, 0, 0.76), (0, 0, 1.12), 0.042, 0.038, 6))
    b.part("gold", cylinder((0, 0, 1.1), (0, 0, 1.15), 0.052, 0.05, 6))
    b.part("mana", cylinder((0, 0, 1.15), (0, 0, 1.152), 0.036, 0.036, 6))  # The glow inside the muzzle.
    for side in (-1, 1):
        prong = [(0.0, 0.0, 0.8), (0.07 * side, 0.0, 0.88), (0.11 * side, 0.0, 1.0), (0.1 * side, 0.0, 1.14),
                 (0.06 * side, 0.0, 1.22), (0.02 * side, 0.0, 1.24)]
        sizes = [(0.035, 0.03), (0.032, 0.028), (0.03, 0.026), (0.026, 0.022), (0.02, 0.018), (0.008, 0.008)]
        b.part("gold", sweep(prong, sizes, Vector((0, 1, 0))), grain=Vector((0, 0, 1)))
    b.part("mana", gem((0, 0, 1.3), (0.035, 0.035, 0.07)))
    # The mana crystal scope on a gold mount, on one side of the barrel.
    b.part("gold", box((0, -0.05, 0.86), (0.025, 0.04, 0.025)))
    b.part("gold", box((0, -0.05, 0.98), (0.025, 0.04, 0.025)))
    b.part("mana", gem((0, -0.085, 0.92), (0.03, 0.03, 0.1)))
    return b


def house_sign(b, face_centre, across, outward):
    """A little house carved into a totem face: a glowing house shape set into the wood, with a dark door and window."""
    c, a, n = Vector(face_centre), Vector(across).normalized(), Vector(outward).normalized()
    up = Vector((0, 0, 1))
    house = [(-0.085, -0.08), (0.085, -0.08), (0.085, 0.04), (0.115, 0.04), (0.0, 0.14), (-0.115, 0.04), (-0.085, 0.04)]
    b.part("hearth", slab(house, c + n * 0.004, a, up, lambda i: 0.016))
    b.part("wood_dark", box(c + n * 0.012 + up * -0.035, (0.045, 0.012, 0.09), Matrix((a, n, up)).transposed()))  # Door.
    b.part("wood_dark", box(c + n * 0.012 + a * 0.045 + up * 0.0, (0.03, 0.012, 0.03), Matrix((a, n, up)).transposed()))  # Window.


def build_home_totem():
    """The Home Totem (3.8): a carved post on a stone base, a little house on every side glowing green, a band of
    green runes, and a roof cap, all inside one block (origin at the block's bottom centre)."""
    b = MeshBuilder()
    b.part("stone", box((0, 0, 0.05), (0.62, 0.62, 0.1)))
    b.part("stone", box((0, 0, 0.12), (0.5, 0.5, 0.04)))
    half, bottom, top = 0.17, 0.14, 0.8
    b.part("totem", box((0, 0, (bottom + top) / 2), (half * 2, half * 2, top - bottom)))
    # A glowing rune band low on the post, and a carved collar under the cap.
    b.part("hearth", box((0, 0, 0.24), (half * 2 + 0.012, half * 2 + 0.012, 0.035)))
    b.part("wood_dark", box((0, 0, top - 0.02), (half * 2 + 0.04, half * 2 + 0.04, 0.05)))
    for across, outward in (((1, 0, 0), (0, -1, 0)), ((-1, 0, 0), (0, 1, 0)), ((0, 1, 0), (1, 0, 0)), ((0, -1, 0), (-1, 0, 0))):
        o = Vector(outward)
        house_sign(b, Vector((0, 0, 0.5)) + o * half, across, o)
    # The roof cap: a little four-sided roof, like the house on the sides, with a glowing gem on its peak.
    eave, peak = 0.25, 0.94
    corners = [Vector((sx * eave, sy * eave, top + 0.005)) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    tip = Vector((0, 0, peak))
    b.part("wood_dark", convex([[corners[k], corners[(k + 1) % 4], tip] for k in range(4)] + [list(corners)]))
    b.part("hearth", gem((0, 0, 0.965), (0.035, 0.035, 0.035)))
    return b


def bag_body(b, width, depth, height, region_name="canvas"):
    """A soft bag body: a box with its back face on y = 0, bulging out along -Y, a little narrower at the top."""
    hw, d, hh = width / 2, depth, height / 2
    top, bottom = 0.88, 1.0
    corners = [Vector((sx * hw * (top if sz > 0 else bottom), sy, sz * hh)) for sz in (-1, 1) for sy in (0.0, -d) for sx in (-1, 1)]
    # A rounded front: the middle of the front face pushes out a little further.
    idx = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    b.part(region_name, convex([[corners[i] for i in f] for f in idx]))
    b.part(region_name, box((0, -d - 0.012, -hh * 0.1), (width * 0.8, 0.03, height * 0.7)))


def bag_flap(b, width, depth, height, length, region_name="leather"):
    """A leather flap over the top, folding down the front."""
    hw, d, hh = width / 2 * 0.9, depth, height / 2
    b.part(region_name, box((0, -d / 2, hh + 0.012), (hw * 2 + 0.02, d + 0.02, 0.024)), grain=Vector((0, 1, 0)))
    b.part(region_name, box((0, -d - 0.03, hh - length / 2), (hw * 1.6, 0.022, length)))


def buckle(b, c, size=0.045):
    c = Vector(c)
    b.part("gold", box(c, (size, 0.02, size * 0.8)))
    b.part("iron", box(c + Vector((0, -0.012, 0)), (size * 0.35, 0.01, size * 0.35)))


def shoulder_straps(b, height, spread=0.1):
    """The tops of the shoulder straps, curving forward over the shoulders from the top of the back."""
    for sx in (-1, 1):
        pts = [(sx * spread, -0.01, height / 2 - 0.02), (sx * spread, 0.05, height / 2 + 0.05),
               (sx * spread, 0.13, height / 2 + 0.06)]
        b.part("leather", sweep(pts, [(0.012, 0.05)] * 3, Vector((0, 0, 1))), grain=Vector((0, 1, 0)))


def bedroll(b, z, depth, length, radius):
    b.part("cloth", cylinder((-length / 2, -depth / 2, z), (length / 2, -depth / 2, z), radius, radius, 8,
                             phase=math.pi / 8), grain=Vector((1, 0, 0)))
    for x in (-length * 0.3, length * 0.3):  # Tied with rope.
        b.part("rope", cylinder((x - 0.012, -depth / 2, z), (x + 0.012, -depth / 2, z), radius * 1.08, radius * 1.08, 8,
                                phase=math.pi / 8), grain=Vector((1, 0, 0)))


def build_bag_satchel():
    """The Woven Satchel (+8 slots): a small canvas satchel with a leather flap and a brass buckle."""
    b = MeshBuilder()
    w, d, h = 0.28, 0.11, 0.24
    bag_body(b, w, d, h)
    bag_flap(b, w, d, h, 0.13)
    buckle(b, (0, -d - 0.045, h / 2 - 0.12))
    shoulder_straps(b, h, 0.08)
    return b


def build_bag_pack():
    """The Traveler's Pack (+12 slots): a canvas backpack with a bedroll on top and a pouch on each side."""
    b = MeshBuilder()
    w, d, h = 0.36, 0.17, 0.42
    bag_body(b, w, d, h)
    bag_flap(b, w, d, h, 0.18)
    buckle(b, (0, -d - 0.045, h / 2 - 0.17))
    for sx in (-1, 1):
        b.part("leather", box((sx * (w / 2 + 0.03), -d * 0.55, -h * 0.18), (0.06, d * 0.7, h * 0.4)))
        buckle(b, (sx * (w / 2 + 0.03), -d * 0.95, -h * 0.08), 0.03)
    bedroll(b, h / 2 + 0.07, d, w * 0.95, 0.065)
    shoulder_straps(b, h)
    return b


def build_bag_rucksack():
    """The Delver's Rucksack (+16 slots): a big framed rucksack with a bedroll, a coil of rope and a cooking pot."""
    b = MeshBuilder()
    w, d, h = 0.42, 0.22, 0.54
    bag_body(b, w, d, h)
    bag_flap(b, w, d, h, 0.2)
    buckle(b, (0, -d - 0.045, h / 2 - 0.19))
    for sx in (-1, 1):  # A wooden frame up the sides, standing proud of the top.
        b.part("wood_dark", box((sx * (w / 2 + 0.02), -0.03, 0.06), (0.035, 0.035, h + 0.2)))
        b.part("rope", box((sx * (w / 2 + 0.02), -0.03, h / 2 - 0.05), (0.045, 0.045, 0.03)))
    b.part("wood_dark", box((0, -0.03, h / 2 + 0.15), (w + 0.075, 0.035, 0.035)), grain=Vector((1, 0, 0)))
    bedroll(b, h / 2 + 0.07, d, w * 0.92, 0.07)
    # A coil of rope on the front, and a little iron pot hanging off one side.
    b.part("rope", cylinder((0, -d - 0.035, -h * 0.22), (0, -d - 0.085, -h * 0.22), 0.085, 0.085, 10), grain=Vector((0, 1, 0)))
    b.part("canvas", cylinder((0, -d - 0.084, -h * 0.22), (0, -d - 0.086, -h * 0.22), 0.045, 0.045, 10))
    b.part("iron", cylinder((w / 2 + 0.07, -d * 0.5, -h * 0.28), (w / 2 + 0.07, -d * 0.5, -h * 0.13), 0.07, 0.08, 8))
    b.part("iron", box((w / 2 + 0.07, -d * 0.5, -h * 0.07), (0.015, 0.015, 0.1)))
    shoulder_straps(b, h, 0.12)
    return b


def build_loot_bag():
    """The drawstring loot bag (Roadmap 4.5): a plump canvas sack gathered at the neck with a rope tie and a frilled top.
    Origin at its bottom centre, like a world prop, about 0.4 m tall."""
    b = MeshBuilder()
    # (height, radius) up the sack: a rounded bottom, a full belly, then pinched in at the neck.
    profile = [(0.0, 0.07), (0.035, 0.15), (0.1, 0.19), (0.18, 0.2), (0.25, 0.17), (0.3, 0.1), (0.33, 0.05)]
    sides = 10
    for (z0, r0), (z1, r1) in zip(profile, profile[1:]):
        b.part("canvas", cylinder((0, 0, z0), (0, 0, z1), r0, r1, sides, caps=False, phase=0.3), grain=Vector((0, 0, 1)))
    b.part("canvas", cylinder((0, 0, -0.001), (0, 0, 0.0), 0.07, 0.07, sides, phase=0.3))  # The bottom.
    # The gathered frill above the tie: flaring out to a ruffled rim, dipping into the opening in the middle.
    n = 12
    neck = [Vector((math.cos(k * 2 * math.pi / n) * 0.05, math.sin(k * 2 * math.pi / n) * 0.05, 0.33)) for k in range(n)]
    rim = [Vector((math.cos(k * 2 * math.pi / n) * r, math.sin(k * 2 * math.pi / n) * r, 0.42 - (0.015 if k % 2 else 0.0)))
           for k, r in ((k, 0.115 if k % 2 == 0 else 0.08) for k in range(n))]
    hollow = Vector((0, 0, 0.385))
    frill = []
    for k in range(n):
        j = (k + 1) % n
        side = [neck[k], neck[j], rim[j], rim[k]]
        frill.append(facing(side, centre(side) - Vector((0, 0, centre(side).z))))
        frill.append(facing([rim[k], rim[j], hollow], Vector((0, 0, 1))))
    b.part("canvas", frill, grain=Vector((0, 0, 1)))
    # Rope wound round the neck, with a loose end hanging down.
    b.part("rope", cylinder((0, 0, 0.315), (0, 0, 0.345), 0.068, 0.062, sides, phase=0.3), grain=Vector((0, 0, 1)))
    b.part("rope", sweep([(0.06, -0.03, 0.33), (0.125, -0.075, 0.28), (0.185, -0.115, 0.205)], [(0.026, 0.026)] * 3,
                         Vector((0, 1, 0))), grain=Vector((0, 0, 1)))
    # A patch sewn on the belly.
    b.part("leather", box((0, -0.19, 0.15), (0.11, 0.03, 0.09)))
    return b


# The Storage Chest (4.5): the body sits in its block like any prop; the lid is its own model so the game can swing it
# open. Front faces -Y (Unity -Z), the hinge runs along the back top edge.
CHEST_W, CHEST_D, CHEST_H = 0.84, 0.62, 0.5
CHEST_HINGE = Vector((0.0, CHEST_D / 2, CHEST_H))
CHEST_LID_H = 0.22


def chest_body(b):
    w, d, h = CHEST_W, CHEST_D, CHEST_H
    b.part("planks", box((0, 0, h / 2), (w - 0.04, d - 0.04, h - 0.02)), grain=Vector((1, 0, 0)))
    for sx in (-1, 1):  # Dark corner posts.
        for sy in (-1, 1):
            b.part("wood_dark", box((sx * (w / 2 - 0.035), sy * (d / 2 - 0.035), h / 2), (0.075, 0.075, h)))
    b.part("wood_dark", box((0, 0, 0.035), (w, d, 0.07)), grain=Vector((1, 0, 0)))  # Plinth.
    b.part("wood_dark", box((0, 0, h - 0.025), (w, d, 0.05)), grain=Vector((1, 0, 0)))  # Rim under the lid.
    for x in (-0.22, 0.22):  # Iron bands round the front, bottom and back.
        b.part("iron", box((x, 0, h / 2), (0.07, d + 0.025, h + 0.01)))
    b.part("gold", box((0, -d / 2 - 0.01, h - 0.1), (0.14, 0.03, 0.15)))  # Lock plate.
    b.part("iron", box((0, -d / 2 - 0.025, h - 0.12), (0.035, 0.01, 0.06)))  # Keyhole.


def chest_lid(b, at=Vector((0, 0, 0))):
    """The lid with its origin on the hinge line (at), reaching forward along -Y from it."""
    w, d, t = CHEST_W, CHEST_D, CHEST_LID_H
    def p(x, y, z):
        return Vector((x, y, z)) + at
    b.part("planks", box(p(0, -d / 2, 0.07), (w, d, 0.14)), grain=Vector((1, 0, 0)))
    b.part("planks", box(p(0, -d / 2, 0.17), (w - 0.08, d - 0.08, 0.06)), grain=Vector((1, 0, 0)))  # Raised top.
    b.part("wood_dark", box(p(0, -d / 2, 0.02), (w + 0.01, d + 0.01, 0.04)), grain=Vector((1, 0, 0)))  # Lid rim.
    for x in (-0.22, 0.22):
        b.part("iron", box(p(x, -d / 2, 0.1), (0.075, d + 0.02, 0.15)))
        b.part("iron", box(p(x, -d / 2, t - 0.01), (0.075, d - 0.06, 0.03)))
    b.part("gold", box(p(0, -d - 0.02, -0.02), (0.1, 0.03, 0.13)))  # Hasp down over the lock.
    for x in (-0.3, 0.3):  # Hinge knuckles at the back.
        b.part("iron", cylinder(p(x - 0.06, 0.012, 0.0), p(x + 0.06, 0.012, 0.0), 0.025, 0.025, 6), grain=Vector((1, 0, 0)))


def build_chest():
    b = MeshBuilder()
    chest_body(b)
    chest_lid(b, CHEST_HINGE)
    return b


def build_chest_body():
    b = MeshBuilder()
    chest_body(b)
    return b


def build_chest_lid():
    b = MeshBuilder()
    chest_lid(b)
    return b


# The Training Dummy (4.6): a burlap sparring dummy on a post, standing in its block like any prop. Front faces -Y
# (Unity -Z). It's taller than its block on purpose (a hero's height); only the bottom block is solid.
def build_training_dummy():
    """Chunky on purpose (ROADMAP style guide): a fat stuffed body, a big sack head and stubby arms, so it reads from
    far away against the 1 m grid."""
    b = MeshBuilder()
    # Crossed feet and a thick post.
    for angle in (0.0, 90.0):
        turn = Matrix.Rotation(math.radians(angle), 3, "Z")
        b.part("wood_dark", box(Vector((0, 0, 0.06)), (0.9, 0.16, 0.12), turn), grain=turn @ Vector((1, 0, 0)))
    b.part("wood", cylinder((0, 0, 0.0), (0, 0, 1.2), 0.1, 0.085, 8, phase=math.pi / 8))
    # A stuffed burlap body, barrel-round and widest at the chest.
    b.part("canvas", cylinder((0, 0, 0.42), (0, 0, 0.55), 0.2, 0.3, 12, phase=math.pi / 12))
    b.part("canvas", cylinder((0, 0, 0.55), (0, 0, 0.95), 0.3, 0.36, 12, phase=math.pi / 12))
    b.part("canvas", cylinder((0, 0, 0.95), (0, 0, 1.15), 0.36, 0.2, 12, phase=math.pi / 12))
    # Stubby stuffed arms on a cross-bar, tied off with rope at the cuffs.
    b.part("wood", cylinder((-0.58, 0, 1.0), (0.58, 0, 1.0), 0.05, 0.05, 6), grain=Vector((1, 0, 0)))
    for sx in (-1, 1):
        b.part("canvas", cylinder((sx * 0.3, 0, 1.0), (sx * 0.52, 0, 0.98), 0.13, 0.11, 8), grain=Vector((1, 0, 0)))
        b.part("rope", cylinder((sx * 0.5, 0, 0.98), (sx * 0.56, 0, 0.98), 0.12, 0.12, 8), grain=Vector((1, 0, 0)))
    # Thick rope round the waist and the neck.
    b.part("rope", cylinder((0, 0, 0.58), (0, 0, 0.67), 0.315, 0.32, 12, phase=math.pi / 12))
    b.part("rope", cylinder((0, 0, 1.13), (0, 0, 1.2), 0.15, 0.15, 8))
    # A big, round stuffed sack head, gathered at the neck and tied off in a tuft on top.
    b.part("canvas", cylinder((0, 0, 1.18), (0, 0, 1.3), 0.15, 0.25, 12, phase=math.pi / 12))
    b.part("canvas", cylinder((0, 0, 1.3), (0, 0, 1.5), 0.25, 0.26, 12, phase=math.pi / 12))
    b.part("canvas", cylinder((0, 0, 1.5), (0, 0, 1.62), 0.26, 0.09, 12, phase=math.pi / 12))
    b.part("rope", cylinder((0, 0, 1.6), (0, 0, 1.66), 0.07, 0.07, 8))
    b.part("canvas", cylinder((0, 0, 1.66), (0, 0, 1.76), 0.06, 0.11, 8))
    # A target painted on the chest: a leather ring round a gold bullseye.
    b.part("leather", cylinder((0, -0.3, 0.8), (0, -0.345, 0.8), 0.16, 0.16, 12), grain=Vector((0, -1, 0)))
    b.part("gold", cylinder((0, -0.34, 0.8), (0, -0.36, 0.8), 0.065, 0.065, 10), grain=Vector((0, -1, 0)))
    return b


# The Rune Hammer (4.6): the Runeforged Paladin's thrown hammer, flown by the game as glowing light (an AbilityLook
# core mesh), tumbling end over end. Its origin is its middle so it tumbles about its centre; the handle runs along +Z
# and the head's long side along Y (Unity Z, the way it flies). Chunky on purpose: only the silhouette shows.
def build_rune_hammer():
    b = MeshBuilder()
    b.part("wood_dark", cylinder((0, 0, -0.5), (0, 0, 0.3), 0.05, 0.045, 8, phase=math.pi / 8))
    b.part("gold", cylinder((0, 0, -0.56), (0, 0, -0.48), 0.07, 0.07, 8))  # Pommel.
    b.part("leather", cylinder((0, 0, -0.42), (0, 0, -0.18), 0.058, 0.058, 8))  # Grip wrap.
    # A big blocky head with flared faces and gold bands, a rune panel on each side.
    head_z, head_h = 0.42, 0.3
    b.part("iron", box((0, 0, head_z), (0.3, 0.46, head_h)), grain=Vector((0, 1, 0)))
    for sy in (-1, 1):
        b.part("steel", box((0, sy * 0.27, head_z), (0.36, 0.08, head_h + 0.06)))  # Striking faces.
        b.part("gold", box((0, sy * 0.17, head_z), (0.32, 0.04, head_h + 0.02)))  # Bands.
    for sx in (-1, 1):
        b.part("rune", box((sx * 0.155, 0, head_z), (0.01, 0.26, 0.18)))
    b.part("gold", gem((0, 0, head_z + head_h / 2 + 0.06), (0.07, 0.07, 0.09)))  # A gem on top.
    return b


# The Stone Spear (4.6): the Geomancer's lance of rock, flown as glowing light like the Rune Hammer but point first,
# with no spin. Its origin is its middle; it points along +Y (Unity +Z, the way it flies).
def build_stone_spear():
    b = MeshBuilder()
    along = Vector((0, 1, 0))
    b.part("stone", cylinder((0, -0.6, 0), (0, 0.15, 0), 0.09, 0.16, 6, phase=0.3), grain=along)  # Shaft, thick at the head.
    b.part("stone", cylinder((0, 0.15, 0), (0, 0.8, 0), 0.2, 0.0, 6, phase=0.3), grain=along)  # A big, rough point.
    # Jagged shards jutting back from the head, like a rock that split off a cliff.
    for i, angle in enumerate((0.0, 2.1, 4.2)):
        out = Vector((math.cos(angle), 0, math.sin(angle)))
        base = Vector((0, 0.3, 0)) + out * 0.12
        tip = Vector((0, -0.1 - 0.05 * i, 0)) + out * 0.3
        b.part("stone", cylinder(base, tip, 0.09, 0.0, 4, phase=angle), grain=along)
    return b


MODELS = {
    "WoodenPickaxe": build_wooden_pickaxe,
    "StonePickaxe": build_stone_pickaxe,
    "StoneAxe": build_stone_axe,
    "PickOfDestiny": build_pick_of_destiny,
    "Mgrs": build_mgrs,
    "TorchFloor": build_torch_floor,
    "TorchWall": build_torch_wall,
    "Workbench": build_workbench,
    "HomeTotem": build_home_totem,
    "BagSatchel": build_bag_satchel,
    "BagPack": build_bag_pack,
    "BagRucksack": build_bag_rucksack,
    "LootBag": build_loot_bag,
    "Chest": build_chest,
    "ChestBody": build_chest_body,
    "ChestLid": build_chest_lid,
    "TrainingDummy": build_training_dummy,
    "RuneHammer": build_rune_hammer,
    "StoneSpear": build_stone_spear,
}


# ----------------------------------------------------------------------------------------------------------------
# Blender scene, preview material and export
# ----------------------------------------------------------------------------------------------------------------

def preview_material(atlas, glow):
    """Atlas plus glow in Blender. Unity has its own materials (Voxelforge/Foliage in the world, URP Lit in hands)."""
    mat = bpy.data.materials.get("Props_Preview") or bpy.data.materials.new("Props_Preview")
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    nodes.clear()
    out = nodes.new("ShaderNodeOutputMaterial")
    bsdf = nodes.new("ShaderNodeBsdfPrincipled")
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = atlas
    tex.interpolation = "Closest"
    light = nodes.new("ShaderNodeTexImage")
    light.image = glow
    links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    links.new(light.outputs["Color"], bsdf.inputs["Emission Color"])
    bsdf.inputs["Emission Strength"].default_value = 2.0
    bsdf.inputs["Roughness"].default_value = 0.6
    links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


def build_models(material):
    collection = bpy.data.collections.get("Props")
    if collection is None:
        collection = bpy.data.collections.new("Props")
        bpy.context.scene.collection.children.link(collection)
    x = 0.0
    for name, builder in MODELS.items():
        b = builder()
        obj = b.build(name, material, collection)
        obj.location = (x, 0.0, 0.0)
        print(f"{name}: {b.triangle_count()} triangles")
        x += 1.2


def export_models(only=None):
    """Exports every model, or just the names in only (so rebuilding new models leaves the others' files alone)."""
    os.makedirs(MODEL_DIR, exist_ok=True)
    for name in (only or MODELS):
        obj = bpy.data.objects[name]
        saved = obj.location.copy()
        obj.location = (0.0, 0.0, 0.0)  # The origin is the grip or the block's bottom centre.
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
    atlas_image = save_image("Props_Atlas", albedo, ATLAS_PATH)
    glow_image = save_image("Props_Glow", glow, GLOW_PATH)
    build_models(preview_material(atlas_image, glow_image))
    if export:
        export_models(only)


if __name__ == "__main__":
    build_all()
