"""Builds Voxelforge's foliage: grass, flowers, bushes and trees (Roadmap 2.6 part B).

Run inside Blender (Text Editor, or exec over MCP). build_all() rebuilds everything in the current file:
  - paints one shared 2048 x 2048 texture atlas into Art/Textures/Foliage/Foliage_Atlas.png
  - builds every plant plus a cheaper "<Name>_LOD1" for far away, laid out in a row to look at
  - exports one FBX per plant into Art/Models/Foliage/

Mesh contract (read by Assets/_Voxelforge/Shaders/Foliage.shader, drawn without GameObjects):
  - origin at the bottom centre, Z up here; the export bakes Unity's Y up into the vertices
  - 1 unit = 1 block; trees are modelled for a 5-block trunk and Unity scales them to the trunk that grew
  - UV0 = the plant's area of the atlas (albedo + cut-out alpha)
  - vertex colour RGB = painted shading multiplier, A = sway (0 where rooted, 1 at the tips)
  - canopies carry custom normals pointing out of the whole crown, so they light like one soft ball

Everything is our own, drawn by code (no photos), so it can be rebuilt and tweaked.
"""
import math
import os
import random

import bmesh
import bpy
import numpy as np
from mathutils import Euler, Matrix, Vector, noise

REPO = os.path.abspath(os.path.join(os.path.dirname(bpy.data.filepath or __file__), "..", ".."))
ART = os.path.join(REPO, "Assets", "_Voxelforge", "Art")
ATLAS_PATH = os.path.join(ART, "Textures", "Foliage", "Foliage_Atlas.png")
MODEL_DIR = os.path.join(ART, "Models", "Foliage")

ATLAS_SIZE = 2048

# Atlas areas as (u0, v0, u1, v1), v up. Cards have alpha; the "mass" and bark areas are opaque.
REGIONS = {
    "grass_a": (0.0, 0.75, 0.5, 1.0),
    "grass_b": (0.5, 0.75, 0.75, 1.0),
    "twigs": (0.75, 0.75, 1.0, 1.0),
    "flower_yellow": (0.0, 0.5, 0.25, 0.75),
    "flower_blue": (0.25, 0.5, 0.5, 0.75),
    "flower_red": (0.5, 0.5, 0.75, 0.75),
    "bush_card": (0.75, 0.5, 1.0, 0.75),
    "leaf_card": (0.0, 0.25, 0.25, 0.5),
    "pine_card": (0.25, 0.25, 0.5, 0.5),
    "dry_card": (0.5, 0.25, 0.75, 0.5),
    "bark_pale": (0.75, 0.25, 1.0, 0.5),
    "leaf_mass": (0.0, 0.0, 0.25, 0.25),
    "pine_mass": (0.25, 0.0, 0.5, 0.25),
    "dry_mass": (0.5, 0.0, 0.75, 0.25),
    "bark": (0.75, 0.0, 1.0, 0.25),
}


def rgb(r, g, b):
    return np.array([r, g, b], np.float32) / 255.0


# ----------------------------------------------------------------------------------------------------------------
# Painting
# ----------------------------------------------------------------------------------------------------------------

class Layer:
    """One atlas area. x runs right and y runs up from the bottom-left corner, in pixels."""

    def __init__(self, view):
        self.px = view
        self.h, self.w = view.shape[:2]

    def _grid(self, x0, y0, x1, y1):
        x0, x1 = max(int(math.floor(x0)), 0), min(int(math.ceil(x1)), self.w)
        y0, y1 = max(int(math.floor(y0)), 0), min(int(math.ceil(y1)), self.h)
        if x0 >= x1 or y0 >= y1:
            return None
        xs = np.arange(x0, x1, dtype=np.float32) + 0.5
        ys = (np.arange(y0, y1, dtype=np.float32) + 0.5)[::-1]  # Rows run top-down.
        x, y = np.meshgrid(xs, ys)
        return x, y, slice(self.h - y1, self.h - y0), slice(x0, x1)

    def _blend(self, rows, cols, colour, alpha):
        dst = self.px[rows, cols]
        a = alpha[..., None]
        dst[..., :3] = dst[..., :3] * (1.0 - a) + np.clip(colour, 0.0, 1.0) * a
        dst[..., 3:] = dst[..., 3:] + a * (1.0 - dst[..., 3:])

    def stroke(self, points, widths, base, tip, rib=0.2, side=0.25):
        """A tapered brush stroke along points (blade, leaf, petal, twig). Lit from one side, darker down the middle."""
        p = np.asarray(points, np.float32)
        w = np.maximum(np.asarray(widths, np.float32), 0.5)
        pad = float(w.max()) + 2.0
        grid = self._grid(p[:, 0].min() - pad, p[:, 1].min() - pad, p[:, 0].max() + pad, p[:, 1].max() + pad)
        if grid is None:
            return
        x, y, rows, cols = grid
        # Distance to every segment of the path, with the width blended along each segment.
        a, seg = p[:-1], p[1:] - p[:-1]
        seg_len2 = np.maximum((seg * seg).sum(axis=1), 1e-6)
        rx = x[..., None] - a[:, 0]
        ry = y[..., None] - a[:, 1]
        along = np.clip((rx * seg[:, 0] + ry * seg[:, 1]) / seg_len2, 0.0, 1.0)
        dx = rx - along * seg[:, 0]
        dy = ry - along * seg[:, 1]
        width = w[:-1] + along * (w[1:] - w[:-1])
        field = np.sqrt(dx * dx + dy * dy) - width
        nearest = np.argmin(field, axis=-1)
        take = lambda arr: np.take_along_axis(arr, nearest[..., None], -1)[..., 0]
        cover = np.clip(0.5 - take(field), 0.0, 1.0)
        if not cover.any():
            return
        tangent = seg / np.sqrt(seg_len2)[:, None]
        across = (tangent[nearest, 0] * take(dy) - tangent[nearest, 1] * take(dx)) / (take(width) + 1e-3)
        across = np.clip(across, -1.0, 1.0)
        t = ((nearest + take(along)) / len(seg))[..., None]
        colour = np.asarray(base) + (np.asarray(tip) - np.asarray(base)) * t
        light = 1.0 + side * across - rib * np.exp(-(across / 0.15) ** 2)
        self._blend(rows, cols, colour * light[..., None], cover)

    def dot(self, cx, cy, radius, colour, highlight=0.3):
        grid = self._grid(cx - radius - 2, cy - radius - 2, cx + radius + 2, cy + radius + 2)
        if grid is None:
            return
        x, y, rows, cols = grid
        d = np.sqrt((x - cx) ** 2 + (y - cy) ** 2)
        cover = np.clip(radius + 0.5 - d, 0.0, 1.0)
        light = 1.0 + highlight * ((cx - x) * 0.4 + (y - cy) * 0.8) / radius
        self._blend(rows, cols, np.asarray(colour) * light[..., None], cover)

    def fill(self, colour, mottle):
        self.px[..., :3] = np.asarray(colour) * mottle[..., None]
        self.px[..., 3] = 1.0


def bezier(p0, p1, p2, n):
    t = np.linspace(0.0, 1.0, n, dtype=np.float32)[:, None]
    p0, p1, p2 = (np.asarray(v, np.float32) for v in (p0, p1, p2))
    return (1 - t) ** 2 * p0 + 2 * (1 - t) * t * p1 + t ** 2 * p2


def blade_widths(width, n):
    t = np.linspace(0.0, 1.0, n)
    return width * (1.0 - t) ** 0.85 + 0.4


def leaf_widths(width, n):
    t = np.linspace(0.0, 1.0, n)
    return width * np.sin(np.pi * t) ** 0.7 + 0.5


def value_noise(h, w, cell, rng, octaves=3):
    """Soft blotchy noise in 0..1 that tiles left to right."""
    out = np.zeros((h, w), np.float32)
    total, amp = 0.0, 1.0
    for _ in range(octaves):
        gw = max(w // cell, 1)
        grid = rng.random((h // cell + 2, gw)).astype(np.float32)
        ys, xs = np.arange(h) / cell, np.arange(w) / cell
        y0, x0 = ys.astype(int), xs.astype(int)
        fy, fx = ys - y0, xs - x0
        fy, fx = fy * fy * (3 - 2 * fy), fx * fx * (3 - 2 * fx)
        a = grid[y0][:, x0 % gw]
        b = grid[y0][:, (x0 + 1) % gw]
        c = grid[y0 + 1][:, x0 % gw]
        d = grid[y0 + 1][:, (x0 + 1) % gw]
        top = a + (b - a) * fx
        bottom = c + (d - c) * fx
        out += amp * (top + (bottom - top) * fy[:, None])
        total += amp
        amp *= 0.5
        cell = max(cell // 2, 1)
    return out / total


def paint_grass(layer, rng, count, width, length, base, tip, lean_spread):
    blades = []
    for _ in range(count):
        bx = layer.w / 2 + rng.normal(0, layer.w * 0.12)
        lean = (bx - layer.w / 2) / (layer.w / 2) * lean_spread + rng.normal(0, 0.22)
        size = layer.h * rng.uniform(*length) * (1.0 - 0.3 * min(abs(lean), 1.0))
        top = (bx + lean * size * 0.7, size * (1.0 - 0.2 * min(abs(lean), 1.0)))
        mid = (bx + lean * size * 0.12, size * 0.55)
        blades.append((rng.uniform(0.6, 1.08), (bx, -3.0), mid, top, rng.uniform(*width)))
    for shade, p0, p1, p2, w in sorted(blades, key=lambda b: b[0]):  # Darker blades sit behind.
        layer.stroke(bezier(p0, p1, p2, 22), blade_widths(w, 22), base * shade, tip * shade, rib=0.18, side=0.3)


def paint_seed_heads(layer, rng, count):
    for _ in range(count):
        bx = layer.w / 2 + rng.normal(0, layer.w * 0.1)
        top = (bx + rng.normal(0, 40), layer.h * rng.uniform(0.82, 0.97))
        stem = bezier((bx, -3), (bx, top[1] * 0.5), top, 20)
        layer.stroke(stem, np.full(20, 2.2), rgb(80, 110, 45), rgb(170, 160, 90), rib=0.0)
        for k in range(9):  # Little oat-like seeds hanging off the top of the stalk.
            t = 0.78 + k * 0.025
            sx, sy = stem[min(int(t * 19), 19)]
            side = 1 if k % 2 else -1
            layer.stroke(bezier((sx, sy), (sx + side * 6, sy - 4), (sx + side * 9, sy - 14), 8),
                         leaf_widths(4.5, 8), rgb(200, 180, 110), rgb(240, 225, 160), rib=0.1)


def paint_twigs(layer, rng):
    dark, light = rgb(95, 75, 58), rgb(160, 132, 100)
    leaf_dark, leaf_light = rgb(110, 120, 70), rgb(175, 180, 105)

    def branch(x, y, angle, length, width, depth):
        end = np.array([x + math.sin(angle) * length, y + math.cos(angle) * length])
        bend = np.array([math.cos(angle), -math.sin(angle)]) * rng.normal(0, length * 0.12)
        points = bezier((x, y), (np.array([x, y]) + end) / 2 + bend, end, 12)
        layer.stroke(points, np.linspace(width, width * 0.6, 12), dark, light, rib=0.0, side=0.35)
        if depth == 0:
            for _ in range(3):
                a = angle + rng.uniform(-1.2, 1.2)
                ln = rng.uniform(12, 22)
                start = points[rng.integers(6, 12)]
                tip = start + np.array([math.sin(a), math.cos(a)]) * ln
                layer.stroke(bezier(start, (start + tip) / 2, tip, 8), leaf_widths(4.0, 8), leaf_dark, leaf_light)
            return
        for _ in range(rng.integers(2, 4)):
            sx, sy = points[rng.integers(6, 12)]
            branch(sx, sy, angle + rng.uniform(-0.75, 0.75), length * rng.uniform(0.55, 0.75), width * 0.65, depth - 1)

    for k in range(6):
        branch(layer.w / 2 + rng.normal(0, 14), -2, (k - 2.5) * 0.24 + rng.normal(0, 0.08),
               layer.h * rng.uniform(0.3, 0.42), rng.uniform(6, 8), 3)


def paint_flowers(layer, rng, bloom, count, scale):
    """Leaves at the base, stems, then a bloom on each stem. bloom(layer, x, y, size, rng) draws one flower head."""
    green_dark, green_light = rgb(45, 95, 32), rgb(115, 170, 62)
    for _ in range(8):
        bx = layer.w / 2 + rng.normal(0, 30)
        a = rng.uniform(-1.1, 1.1)
        ln = rng.uniform(80, 150)
        tip = (bx + math.sin(a) * ln, math.cos(a) * ln * 0.8)
        mid = (bx + math.sin(a) * ln * 0.4, math.cos(a) * ln * 0.55)
        layer.stroke(bezier((bx, -3), mid, tip, 14), leaf_widths(rng.uniform(12, 18), 14), green_dark, green_light,
                     rib=0.3)
    heads = []
    for _ in range(count):
        x = layer.w / 2 + rng.uniform(-0.3, 0.3) * layer.w
        y = layer.h * rng.uniform(0.5, 0.86)
        heads.append((y, x))
    for y, x in sorted(heads):  # Lower heads first, so higher ones overlap them.
        bx = layer.w / 2 + rng.normal(0, 18)
        stem = bezier((bx, -3), ((bx + x) / 2 + rng.normal(0, 15), y * 0.5), (x, y), 16)
        layer.stroke(stem, np.linspace(4.5, 3.0, 16), green_dark, green_light, rib=0.0)
        bloom(layer, x, y, scale * rng.uniform(0.85, 1.15), rng)


def petals(layer, x, y, count, length, width, base, tip, rng, centre, centre_radius):
    start = rng.uniform(0, math.tau)
    for k in range(count):
        a = start + k * math.tau / count + rng.normal(0, 0.08)
        end = (x + math.cos(a) * length, y + math.sin(a) * length * 0.85)
        shade = 1.0 + 0.15 * math.sin(a)  # Upper petals catch the light.
        layer.stroke(bezier((x, y), ((x + end[0]) / 2, (y + end[1]) / 2), end, 12), leaf_widths(width, 12),
                     base * shade, tip * shade, rib=0.12, side=0.15)
    layer.dot(x, y, centre_radius, centre)


def buttercup(layer, x, y, size, rng):
    petals(layer, x, y, 5, 26 * size, 14 * size, rgb(230, 165, 25), rgb(255, 228, 90), rng, rgb(225, 120, 25), 8 * size)


def cornflower(layer, x, y, size, rng):
    petals(layer, x, y, 9, 28 * size, 7 * size, rgb(65, 80, 200), rgb(140, 170, 255), rng, rgb(55, 35, 110), 7 * size)


def poppy(layer, x, y, size, rng):
    petals(layer, x, y, 4, 34 * size, 24 * size, rgb(185, 28, 24), rgb(245, 85, 55), rng, rgb(35, 22, 22), 9 * size)


def paint_clump(layer, rng, count, length, width, dark, light, radius=0.42, flat=1.0):
    """A ball of leaves seen from outside: inner leaves are darker and drawn first, outer ones on top."""
    cx, cy = layer.w / 2, layer.h / 2
    leaves = []
    for _ in range(count):
        k = math.sqrt(rng.random())
        a = rng.uniform(0, math.tau)
        px, py = cx + k * radius * layer.w * math.cos(a), cy + k * radius * layer.h * math.sin(a) * flat
        shade = 0.6 + 0.3 * k + 0.2 * (py - cy) / (layer.h / 2) + rng.normal(0, 0.05)
        leaves.append((k + rng.uniform(-0.2, 0.2), px, py, a + rng.normal(0, 0.6), length * rng.uniform(0.75, 1.2), shade))
    for _, px, py, a, ln, shade in sorted(leaves):
        d = np.array([math.cos(a), math.sin(a)])
        start = np.array([px, py]) - d * ln * 0.3
        end = start + d * ln
        mid = (start + end) / 2 + np.array([-d[1], d[0]]) * rng.normal(0, ln * 0.08)
        layer.stroke(bezier(start, mid, end, 12), leaf_widths(width, 12), dark * shade, light * shade, rib=0.3)


def paint_pine_spray(layer, rng):
    """A drooping pine branch hung from the top of the card."""
    dark, light = rgb(24, 58, 44), rgb(72, 122, 82)
    stems = [bezier((layer.w / 2, layer.h - 4), (layer.w / 2 + rng.normal(0, 30), layer.h * 0.5),
                    (layer.w / 2 + rng.normal(0, 40), layer.h * 0.08), 30)]
    for k in range(8):
        sx, sy = stems[0][4 + k * 3 + rng.integers(0, 2)]
        side = 1 if k % 2 else -1
        reach = rng.uniform(90, 140) * (1.0 - k * 0.05)
        stems.append(bezier((sx, sy), (sx + side * reach * 0.6, sy - 25), (sx + side * reach, sy - reach * 0.9), 16))
    needles = []
    for stem in stems:
        for i in range(1, len(stem)):
            sx, sy = stem[i]
            t = np.subtract(stem[i], stem[i - 1])
            base_angle = math.atan2(t[1], t[0])
            for side in (-1, 1):
                a = base_angle + side * rng.uniform(0.6, 1.0)
                ln = rng.uniform(38, 58)
                needles.append((rng.uniform(0.6, 1.1), sx, sy, a, ln))
    for shade, sx, sy, a, ln in sorted(needles):
        end = (sx + math.cos(a) * ln, sy + math.sin(a) * ln)
        layer.stroke(bezier((sx, sy), ((sx + end[0]) / 2, (sy + end[1]) / 2 - 3), end, 8), blade_widths(4.5, 8),
                     dark * shade, light * shade, rib=0.0, side=0.2)
    for stem in stems:
        layer.stroke(stem, np.linspace(5, 2, len(stem)), rgb(70, 48, 32), rgb(105, 75, 50), rib=0.0)


def paint_leaf_mass(layer, rng, base, count, length, width, dark, light):
    layer.fill(base, 0.75 + 0.45 * value_noise(layer.h, layer.w, 64, rng))
    for _ in range(count):
        px, py = rng.uniform(0, layer.w), rng.uniform(0, layer.h)
        a = rng.uniform(0, math.tau)
        ln = length * rng.uniform(0.7, 1.2)
        d = np.array([math.cos(a), math.sin(a)])
        start = np.array([px, py])
        shade = rng.uniform(0.65, 1.1)
        layer.stroke(bezier(start, start + d * ln * 0.5 + np.array([-d[1], d[0]]) * ln * 0.08, start + d * ln, 10),
                     leaf_widths(width, 10), dark * shade, light * shade, rib=0.3)


def paint_pine_mass(layer, rng):
    """Needles pointing away from the middle: pine tiers are mapped from above, so they point down the branches."""
    layer.fill(rgb(28, 62, 46), 0.75 + 0.45 * value_noise(layer.h, layer.w, 64, rng))
    cx, cy = layer.w / 2, layer.h / 2
    for _ in range(1800):
        px, py = rng.uniform(0, layer.w), rng.uniform(0, layer.h)
        a = math.atan2(py - cy, px - cx) + rng.normal(0, 0.45)
        ln = rng.uniform(22, 36)
        shade = rng.uniform(0.6, 1.15)
        end = (px + math.cos(a) * ln, py + math.sin(a) * ln)
        layer.stroke(bezier((px, py), ((px + end[0]) / 2, (py + end[1]) / 2), end, 6), blade_widths(3.0, 6),
                     rgb(26, 60, 44) * shade, rgb(80, 132, 88) * shade, rib=0.0)


def paint_bark(layer, rng, base, groove, ridge, wavy, knots):
    layer.fill(base, 0.8 + 0.35 * value_noise(layer.h, layer.w, 32, rng))

    def wrapped(points, widths, a, b, rib):
        for shift in (-layer.w, 0, layer.w):  # Draw across the seam so the bark wraps round the trunk.
            layer.stroke(points + np.array([shift, 0.0]), widths, a, b, rib=rib, side=0.3)

    ys = np.linspace(-10, layer.h + 10, 40)
    for _ in range(30):
        x = rng.uniform(0, layer.w)
        phase = rng.uniform(0, math.tau)
        points = np.stack([x + wavy * np.sin(ys / 45 + phase) + rng.normal(0, 1.5, len(ys)), ys], axis=1)
        if rng.random() < 0.55:
            wrapped(points, np.full(len(ys), rng.uniform(2.5, 5.5)), groove, groove * 1.2, 0.0)
        else:
            wrapped(points, np.full(len(ys), rng.uniform(4, 8)), ridge, ridge * 1.1, 0.2)
    for _ in range(knots):
        x, y = rng.uniform(40, layer.w - 40), rng.uniform(60, layer.h - 60)
        layer.dot(x, y, rng.uniform(9, 14), groove * 0.8, highlight=-0.3)
        layer.dot(x, y, rng.uniform(4, 6), ridge, highlight=0.2)


def paint_pale_bark(layer, rng):
    paint_bark(layer, rng, rgb(160, 140, 112), rgb(95, 80, 66), rgb(190, 172, 145), wavy=10, knots=5)
    for _ in range(90):  # Short dark dashes across the trunk.
        x, y = rng.uniform(0, layer.w), rng.uniform(0, layer.h)
        ln = rng.uniform(10, 26)
        layer.stroke(np.array([[x, y], [x + ln / 2, y + 1], [x + ln, y]]), np.array([1.5, 2.5, 1.5]),
                     rgb(90, 78, 66), rgb(110, 95, 80), rib=0.0)


def paint_atlas(seed=2026):
    pixels = np.zeros((ATLAS_SIZE, ATLAS_SIZE, 4), np.float32)

    def layer(name):
        u0, v0, u1, v1 = REGIONS[name]
        s = ATLAS_SIZE
        return Layer(pixels[round((1 - v1) * s):round((1 - v0) * s), round(u0 * s):round(u1 * s)])

    rng = np.random.default_rng(seed)
    paint_grass(layer("grass_a"), rng, 90, (8, 14), (0.45, 0.95), rgb(45, 90, 26), rgb(165, 212, 86), 0.9)
    tall = layer("grass_b")
    paint_grass(tall, rng, 40, (4.5, 7.5), (0.6, 1.0), rgb(55, 95, 35), rgb(185, 205, 105), 0.7)
    paint_seed_heads(tall, rng, 5)
    paint_twigs(layer("twigs"), rng)
    paint_flowers(layer("flower_yellow"), rng, buttercup, 6, 1.0)
    paint_flowers(layer("flower_blue"), rng, cornflower, 5, 1.0)
    paint_flowers(layer("flower_red"), rng, poppy, 3, 1.0)
    paint_clump(layer("bush_card"), rng, 170, 40, 13, rgb(30, 78, 34), rgb(100, 165, 70))
    paint_clump(layer("leaf_card"), rng, 150, 48, 15, rgb(38, 84, 28), rgb(125, 180, 60))
    paint_pine_spray(layer("pine_card"), rng)
    paint_clump(layer("dry_card"), rng, 220, 24, 8, rgb(80, 92, 38), rgb(178, 182, 92), flat=0.65)
    paint_pale_bark(layer("bark_pale"), rng)
    paint_leaf_mass(layer("leaf_mass"), rng, rgb(42, 88, 32), 650, 44, 14, rgb(38, 84, 28), rgb(120, 175, 60))
    paint_pine_mass(layer("pine_mass"), rng)
    paint_leaf_mass(layer("dry_mass"), rng, rgb(92, 100, 45), 900, 24, 8, rgb(80, 92, 38), rgb(170, 175, 88))
    paint_bark(layer("bark"), rng, rgb(92, 62, 42), rgb(48, 30, 20), rgb(130, 95, 66), wavy=6, knots=4)
    return pixels


def save_atlas(pixels):
    image = bpy.data.images.get("Foliage_Atlas")
    if image is None or tuple(image.size) != (ATLAS_SIZE, ATLAS_SIZE):
        if image is not None:
            bpy.data.images.remove(image)
        image = bpy.data.images.new("Foliage_Atlas", ATLAS_SIZE, ATLAS_SIZE, alpha=True)
    image.pixels.foreach_set(np.flipud(pixels).ravel())  # Blender stores rows bottom-up.
    os.makedirs(os.path.dirname(ATLAS_PATH), exist_ok=True)
    image.filepath_raw = ATLAS_PATH
    image.file_format = "PNG"
    image.save()
    print(f"Saved {ATLAS_PATH}")
    return image


# ----------------------------------------------------------------------------------------------------------------
# Meshes
# ----------------------------------------------------------------------------------------------------------------

def region_uv(name, s, t):
    """(s, t) in 0..1 inside an atlas area, kept 2 texels away from its edge."""
    u0, v0, u1, v1 = REGIONS[name]
    pad = 2.0 / ATLAS_SIZE
    s, t = min(max(s, 0.0), 1.0), min(max(t, 0.0), 1.0)
    return u0 + pad + s * (u1 - u0 - 2 * pad), v0 + pad + t * (v1 - v0 - 2 * pad)


def clamp01(x):
    return min(max(x, 0.0), 1.0)


class MeshBuilder:
    """Faces with their own corners: position, normal, uv and colour (shade, shade, shade, sway)."""

    def __init__(self):
        self.positions, self.normals, self.uvs, self.colours, self.faces = [], [], [], [], []

    def face(self, corners):
        start = len(self.positions)
        for position, normal, uv, colour in corners:
            self.positions.append(tuple(position))
            self.normals.append(tuple(Vector(normal).normalized()))
            self.uvs.append(uv)
            self.colours.append(colour)
        self.faces.append(tuple(range(start, start + len(corners))))

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
        mesh.polygons.foreach_set("use_smooth", [True] * len(mesh.polygons))
        mesh.update()
        mesh.normals_split_custom_set(self.normals)
        mesh.materials.append(material)
        obj = bpy.data.objects.new(name, mesh)
        collection.objects.link(obj)
        return obj


def card(b, region, base, right, up, width, height, rows, colour, normal=None):
    """An upright textured card from base (bottom centre) along right and up. colour(p, t) gives (r, g, b, sway)."""
    face_normal = right.cross(up).normalized()
    for r in range(rows):
        t0, t1 = r / rows, (r + 1) / rows
        corners = []
        for s, t in ((0, t0), (1, t0), (1, t1), (0, t1)):
            p = base + right * (s - 0.5) * width + up * height * t
            corners.append((p, normal(p) if normal else face_normal, region_uv(region, s, t), colour(p, t)))
        b.face(corners)


def crossed_cards(b, region, width, height, count, rows, colour, rng, sink=0.03):
    """A tuft: cards crossing at the centre, each turned a little at random."""
    start = rng.uniform(0, math.pi)
    for k in range(count):
        a = start + k * math.pi / count + rng.uniform(-0.12, 0.12)
        card(b, region, Vector((0, 0, -sink)), Vector((math.cos(a), math.sin(a), 0)), Vector((0, 0, 1)),
             width, height, rows, colour)


def icosphere(subdivisions):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdivisions, radius=1.0)
    directions = {v: v.co.normalized() for v in bm.verts}
    faces = [[directions[v] for v in f.verts] for f in bm.faces]
    bm.free()
    return faces


def blob(b, region, centre, radii, subdivisions, rng, normal, colour, bumps=0.12):
    """A lumpy ellipsoid of leaves. Its UVs project its directions onto a disc, so there is no seam anywhere."""
    seed = Vector((rng.uniform(-50, 50), rng.uniform(-50, 50), rng.uniform(-50, 50)))
    turn = Euler((rng.uniform(0, math.tau), rng.uniform(0, math.tau), rng.uniform(0, math.tau))).to_matrix()
    for directions in icosphere(subdivisions):
        corners = []
        for d in directions:
            k = 1.0 + bumps * noise.noise(d * 2.2 + seed)
            p = centre + Vector((d.x * radii[0], d.y * radii[1], d.z * radii[2])) * k
            projected = turn @ d
            corners.append((p, normal(p), region_uv(region, 0.5 + 0.48 * projected.x, 0.5 + 0.48 * projected.y),
                            colour(p, 0.0)))
        b.face(corners)


def fringe(b, region, centre, radii, count, size, rng, normal, colour, min_z=-0.35):
    """Leaf cards over a blob, tilted every which way, so its outline is leafy from any side."""
    for _ in range(count):
        while True:
            d = Vector((rng.gauss(0, 1), rng.gauss(0, 1), rng.gauss(0, 1))).normalized()
            if d.z >= min_z:
                break
        p = centre + Vector((d.x * radii[0], d.y * radii[1], d.z * radii[2])) * 1.02
        right = Vector((0, 0, 1)).cross(d)
        right = right.normalized() if right.length > 0.1 else Vector((1, 0, 0))
        up = d.cross(right).normalized()
        turn = Matrix.Rotation(rng.uniform(-math.pi, math.pi), 3, d) @ Matrix.Rotation(rng.uniform(-0.8, 0.8), 3, right)
        right, up = turn @ right, turn @ up
        s = size * rng.uniform(0.8, 1.2)
        card(b, region, p - up * s * 0.5, right, up, s, s, 1, colour, normal)


def tube(b, region, path, radii, sides, colour, v_length, roots=0, rng=None):
    """A trunk or branch along path. Bark wraps once round (u) and runs v_length metres up the bark area (v)."""
    path = [Vector(p) for p in path]
    tangent = (path[1] - path[0]).normalized()
    side = Vector((1, 0, 0)) if abs(tangent.x) < 0.9 else Vector((0, 1, 0))
    rings, travelled = [], 0.0
    phase = rng.uniform(0, math.tau) if rng else 0.0
    for i, p in enumerate(path):
        if i > 0:
            travelled += (p - path[i - 1]).length
        tangent = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
        side = (side - tangent * side.dot(tangent)).normalized()  # Carry the frame along without twisting.
        other = tangent.cross(side)
        ring = []
        for j in range(sides + 1):
            a = math.tau * j / sides
            radial = side * math.cos(a) + other * math.sin(a)
            r = radii[i]
            if i == 0 and roots:
                r *= 1.0 + 0.3 * max(0.0, math.cos(roots * a + phase))
            ring.append((p + radial * r, radial, j / sides))
        rings.append((ring, travelled / v_length))
    for (lower, v0), (upper, v1) in zip(rings, rings[1:]):
        for j in range(sides):
            corners = []
            for (p, n, u), v in ((lower[j], v0), (lower[j + 1], v0), (upper[j + 1], v1), (upper[j], v1)):
                corners.append((p, n, region_uv(region, u, v), colour(p, v)))
            b.face(corners)


def crown(centre, radius, z_low, z_high):
    """Normal and colour functions for a tree crown: lit like one ball, darker inside and underneath."""
    def normal(p):
        d = p - centre
        return (d.normalized() if d.length > 1e-4 else Vector((0, 0, 1))) + Vector((0, 0, 0.25))

    def colour(p, t):
        d = p - centre
        k = min(d.length / radius, 1.2)
        up = d.normalized().z if d.length > 1e-4 else 0.0
        shade = 0.5 + 0.5 * clamp01(0.45 + 0.35 * up + 0.4 * (k - 0.6))
        sway = clamp01(0.8 * clamp01((p.z - z_low) / (z_high - z_low)) + 0.25 * k)
        return shade, shade, shade, sway

    return normal, colour


def bark_colour(top, sway_scale=0.35):
    def colour(p, t):
        shade = 0.7 + 0.3 * clamp01(p.z / 2.0) - 0.15 * clamp01((p.z - top * 0.6) / (top * 0.4))
        sway = sway_scale * clamp01(p.z / top) ** 2
        return shade, shade, shade, sway
    return colour


# ----------------------------------------------------------------------------------------------------------------
# Plants
# ----------------------------------------------------------------------------------------------------------------

def tuft_colour(bottom_shade):
    def colour(p, t):
        shade = bottom_shade + (1.0 - bottom_shade) * t
        return shade, shade, shade, t ** 1.4
    return colour


def build_grass_short(lod, rng):
    b = MeshBuilder()
    crossed_cards(b, "grass_a", 0.95, 0.48, 3 if lod == 0 else 2, 2 if lod == 0 else 1, tuft_colour(0.55), rng)
    return b


def build_grass_tall(lod, rng):
    b = MeshBuilder()
    crossed_cards(b, "grass_b", 0.75, 0.8, 3 if lod == 0 else 2, 2 if lod == 0 else 1, tuft_colour(0.55), rng)
    return b


def build_prairie_grass(lod, rng):
    """Roadmap 4.7: the Thunderhoof Plains' tall, wind-bent seed grass. The tall grass's seed-head cards, taller, with
    more rows so the whole tuft can bend over one way like grass leaning out of a steady wind. Its gold comes from the
    Plains' grass tint, so it needs no atlas space of its own."""
    b = MeshBuilder()
    crossed_cards(b, "grass_b", 0.85, 1.3, 4 if lod == 0 else 2, 4 if lod == 0 else 2, tuft_colour(0.6), rng)
    lean = rng.uniform(0.32, 0.42)
    heading = rng.uniform(0, 2 * math.pi)
    push = Vector((math.cos(heading), math.sin(heading), 0))
    for i, p in enumerate(b.positions):
        t = max(p[2], 0.0) / 1.3
        bend = push * (lean * t * t)
        # Lean over and sink a little, so the blades keep their length as they bow.
        b.positions[i] = (p[0] + bend.x, p[1] + bend.y, p[2] - 0.12 * t * t)
    return b


def flower_builder(region):
    def build(lod, rng):
        b = MeshBuilder()
        crossed_cards(b, region, 0.5, 0.5, 2, 2 if lod == 0 else 1, tuft_colour(0.65), rng)
        return b
    return build


def build_dry_shrub(lod, rng):
    b = MeshBuilder()
    crossed_cards(b, "twigs", 0.95, 0.95, 3 if lod == 0 else 2, 2 if lod == 0 else 1, tuft_colour(0.7), rng, sink=0.06)
    return b


def build_bush(lod, rng):
    b = MeshBuilder()
    centre, radii = Vector((0, 0, 0.36)), (0.52, 0.5, 0.42)
    normal, colour = crown(Vector((0, 0, 0.2)), 0.6, 0.0, 0.9)
    blob(b, "leaf_mass", centre, radii, 2 if lod == 0 else 1, rng, normal, colour, bumps=0.15)
    fringe(b, "bush_card", centre, radii, 20 if lod == 0 else 6, 0.55 if lod == 0 else 0.7, rng, normal, colour,
           min_z=-0.1)
    return b


def build_oak(lod, rng):
    b = MeshBuilder()
    trunk = [(0, 0, -0.25), (0, 0, 0.45), (0.05, 0.03, 1.8), (0.12, 0.0, 3.2), (0.08, -0.05, 4.4), (0.0, 0.0, 5.7)]
    radii = [0.46, 0.36, 0.31, 0.28, 0.24, 0.16]
    if lod:
        trunk, radii = [trunk[0], trunk[1], trunk[3], trunk[5]], [radii[0], radii[1], radii[3], radii[5]]
    tube(b, "bark", trunk, radii, 10 if lod == 0 else 6, bark_colour(6.0), 7.0, roots=4 if lod == 0 else 0, rng=rng)

    blobs = [((0.0, 0.0, 6.1), (1.9, 1.9, 1.45)),
             ((1.3, 0.7, 5.35), (1.3, 1.25, 1.0)),
             ((-1.2, 0.8, 5.25), (1.2, 1.2, 1.0)),
             ((-0.3, -1.4, 5.4), (1.25, 1.2, 1.0)),
             ((0.9, -0.7, 6.6), (1.1, 1.1, 0.9)),
             ((-0.4, 0.3, 7.3), (1.2, 1.15, 0.85))]
    if lod == 0:
        for (x, y, z), _ in blobs[1:4]:  # Branches reaching into the lower clumps.
            start = Vector((0.08, -0.05, 4.0))
            end = Vector((x, y, z - 0.2))
            middle = start.lerp(end, 0.5) + Vector((0, 0, 0.25))
            tube(b, "bark", [start, middle, end], [0.17, 0.12, 0.07], 6, bark_colour(6.0), 7.0)

    normal, colour = crown(Vector((0.0, 0.0, 5.9)), 2.6, 4.2, 8.2)
    for centre, size in blobs:
        blob(b, "leaf_mass", Vector(centre), size, 2 if lod == 0 else 1, rng, normal, colour)
        count = round(9 * size[0]) if lod == 0 else 3
        fringe(b, "leaf_card", Vector(centre), size, count, 1.2 if lod == 0 else 1.5, rng, normal, colour)
    return b


def pine_tier(b, z0, radius, height, sides, cards, rng, sway):
    """One skirt of branches: a cone with a ragged, drooping rim, its underside, and needle cards hanging off it."""
    apex = Vector((0, 0, z0 + height))
    rim, middle, inner = [], [], []
    start = rng.uniform(0, math.tau)
    for j in range(sides):
        a = start + math.tau * j / sides + rng.uniform(-0.08, 0.08)
        long = j % 2 == 0
        r = radius * (1.0 if long else 0.78) * rng.uniform(0.93, 1.05)
        direction = Vector((math.cos(a), math.sin(a), 0))
        rim.append(direction * r + Vector((0, 0, z0 - (0.24 * radius if long else 0.0))))
        middle.append(direction * r * 0.6 + Vector((0, 0, z0 + height * 0.42)))
        inner.append(direction * 0.22 + Vector((0, 0, z0 + height * 0.3)))

    def uv(p):
        return region_uv("pine_mass", 0.5 + 0.47 * p.x / radius, 0.5 + 0.47 * p.y / radius)

    def top_normal(p):
        return Vector((p.x / radius, p.y / radius, 0.0)) + Vector((0, 0, 0.75))

    def top_colour(p):
        k = clamp01(math.hypot(p.x, p.y) / radius)
        shade = 0.55 + 0.45 * clamp01((p.z - z0 + 0.2 * radius) / (height + 0.2 * radius)) * 0.7 + 0.15 * k
        return shade, shade, shade, sway(p, k)

    def under_colour(p):
        return 0.42, 0.42, 0.42, sway(p, clamp01(math.hypot(p.x, p.y) / radius))

    for j in range(sides):
        k = (j + 1) % sides
        quad = (rim[j], rim[k], middle[k], middle[j])
        b.face([(p, top_normal(p), uv(p), top_colour(p)) for p in quad])
        tri = (middle[j], middle[k], apex)
        b.face([(p, top_normal(p), uv(p), top_colour(p)) for p in tri])
        under = (rim[k], rim[j], inner[j], inner[k])
        b.face([(p, Vector((p.x, p.y, -0.4 * radius)), uv(p), under_colour(p)) for p in under])

    if not cards:
        return
    for j in range(0, sides, 2):
        p = rim[j]
        out = Vector((p.x, p.y, 0)).normalized()
        right = Vector((0, 0, 1)).cross(out).normalized()
        up = (Vector((0, 0, 1)) - out * 0.3).normalized()
        size = 0.85 * rng.uniform(0.85, 1.15)
        base = p * 0.97 - up * size * 0.62
        card(b, "pine_card", base, right, up, size, size, 1,
             lambda q, t: (0.6 + 0.4 * t, 0.6 + 0.4 * t, 0.6 + 0.4 * t, sway(q, 1.0)),
             lambda q: top_normal(q))


def build_pine(lod, rng):
    b = MeshBuilder()
    top = 8.1
    tube(b, "bark", [(0, 0, -0.25), (0, 0, 1.6), (0, 0, 4.5), (0, 0, top)], [0.36, 0.3, 0.2, 0.06],
         8 if lod == 0 else 5, bark_colour(top, 0.3), 9.0, roots=3 if lod == 0 else 0, rng=rng)

    def sway(p, k):
        return clamp01(0.7 * clamp01((p.z - 1.0) / 7.5) + 0.3 * k)

    tiers = [(1.45, 2.0, 2.0), (2.75, 1.7, 1.85), (3.95, 1.4, 1.7), (5.05, 1.05, 1.6), (6.05, 0.72, 2.3)]
    for z0, radius, height in tiers:
        pine_tier(b, z0, radius, height, 14 if lod == 0 else 8, lod == 0, rng, sway)
    return b


def build_scrub_tree(lod, rng):
    """A wind-bent, flat-topped tree for the desert and the crags."""
    b = MeshBuilder()
    trunk = [(0, 0, -0.25), (0.05, 0.0, 1.0), (0.18, 0.08, 2.2), (0.12, 0.15, 3.4), (-0.05, 0.1, 4.3)]
    radii = [0.38, 0.3, 0.27, 0.24, 0.2]
    tube(b, "bark_pale", trunk, radii, 9 if lod == 0 else 6, bark_colour(6.0), 7.0, roots=3 if lod == 0 else 0, rng=rng)

    pads = [((1.7, 0.6, 6.0), (1.5, 1.3, 0.45)),
            ((-1.5, 1.0, 6.3), (1.3, 1.2, 0.42)),
            ((0.2, -1.5, 5.7), (1.3, 1.2, 0.4)),
            ((0.0, 0.2, 6.9), (1.2, 1.1, 0.4))]
    fork = Vector((-0.05, 0.1, 4.1))
    for (x, y, z), _ in pads:
        end = Vector((x * 0.85, y * 0.85, z - 0.25))
        bend = fork.lerp(end, 0.45) + Vector((0, 0, 0.5))
        tube(b, "bark_pale", [fork, bend, end], [0.17, 0.12, 0.07], 6 if lod == 0 else 4, bark_colour(6.0), 7.0)

    normal, colour = crown(Vector((0.0, 0.0, 5.6)), 2.5, 5.0, 7.4)
    for centre, size in pads:
        blob(b, "dry_mass", Vector(centre), size, 2 if lod == 0 else 1, rng, normal, colour, bumps=0.1)
        fringe(b, "dry_card", Vector(centre), size, 13 if lod == 0 else 4, 0.85 if lod == 0 else 1.2, rng, normal,
               colour, min_z=-0.15)
    return b


# FBX file name -> (builder, seed). Each file holds "<Name>_LOD0" and "<Name>_LOD1".
MODELS = {
    "GrassShort": (build_grass_short, 1),
    "GrassTall": (build_grass_tall, 2),
    "FlowerYellow": (flower_builder("flower_yellow"), 3),
    "FlowerBlue": (flower_builder("flower_blue"), 4),
    "FlowerRed": (flower_builder("flower_red"), 5),
    "Bush": (build_bush, 6),
    "DryShrub": (build_dry_shrub, 7),
    "OakTree": (build_oak, 8),
    "PineTree": (build_pine, 9),
    "ScrubTree": (build_scrub_tree, 10),
    "PrairieGrass": (build_prairie_grass, 11),
}


# ----------------------------------------------------------------------------------------------------------------
# Blender scene, preview material and export
# ----------------------------------------------------------------------------------------------------------------

def preview_material(image):
    """Shows the atlas times the painted shading in Blender. Unity uses its own Voxelforge/Foliage material."""
    mat = bpy.data.materials.get("Foliage_Preview") or bpy.data.materials.new("Foliage_Preview")
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    nodes.clear()
    out = nodes.new("ShaderNodeOutputMaterial")
    bsdf = nodes.new("ShaderNodeBsdfPrincipled")
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = image
    shade = nodes.new("ShaderNodeVertexColor")
    shade.layer_name = "Col"
    mix = nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.blend_type = "MULTIPLY"
    mix.inputs["Factor"].default_value = 1.0
    links.new(tex.outputs["Color"], mix.inputs[6])
    links.new(shade.outputs["Color"], mix.inputs[7])
    links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
    bsdf.inputs["Roughness"].default_value = 0.9
    links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    try:
        mat.surface_render_method = "DITHERED"
    except (AttributeError, TypeError):
        pass
    return mat


def build_models(material):
    collection = bpy.data.collections.get("Foliage")
    if collection is None:
        collection = bpy.data.collections.new("Foliage")
        bpy.context.scene.collection.children.link(collection)
    x = 0.0
    for name, (builder, seed) in MODELS.items():
        for lod in (0, 1):
            b = builder(lod, random.Random(seed * 10 + lod))
            obj = b.build(f"{name}_LOD{lod}", material, collection)
            obj.location = (x, 0.0 if lod == 0 else 9.0, 0.0)
            print(f"{obj.name}: {b.triangle_count()} triangles")
        x += 6.0 if "Tree" in name else 2.0


def export_models():
    os.makedirs(MODEL_DIR, exist_ok=True)
    for name in MODELS:
        objects = [bpy.data.objects[f"{name}_LOD0"], bpy.data.objects[f"{name}_LOD1"]]
        saved = [o.location.copy() for o in objects]
        for o in objects:
            o.location = (0.0, 0.0, 0.0)  # Unity draws the raw mesh, so the origin must be the plant's root.
        for o in bpy.context.scene.objects:
            o.select_set(o in objects)
        path = os.path.join(MODEL_DIR, f"{name}.fbx")
        with bpy.context.temp_override(active_object=objects[0], object=objects[0], selected_objects=objects):
            bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"},
                                     apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                                     bake_space_transform=True, axis_forward="-Z", axis_up="Y",
                                     mesh_smooth_type="OFF", colors_type="LINEAR", prioritize_active_color=True,
                                     use_tspace=False, use_custom_props=False, add_leaf_bones=False,
                                     bake_anim=False, path_mode="STRIP")
        for o, location in zip(objects, saved):
            o.location = location
        print(f"Exported {path}")


def build_all(export=True):
    image = save_atlas(paint_atlas())
    build_models(preview_material(image))
    if export:
        export_models()


if __name__ == "__main__":
    build_all()
