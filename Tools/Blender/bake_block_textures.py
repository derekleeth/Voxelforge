"""
Voxelforge block texture baker (Roadmap 1.5).

Builds one node material per block texture layer in a "BlockBake" scene, then bakes each to 512x512 PNGs:
  <Layer>_Albedo.png  painted color (sRGB)
  <Layer>_Height.png  0 = deep crevice, 1 = highest bump (linear)
  <Layer>_Normal.png  tangent-space normal made from the height map (OpenGL style, +Y up)
  <Layer>_Mask.png    R = ambient occlusion, G = smoothness, B = emission (linear)

Every texture tiles seamlessly: noise is sampled on a 4D torus wrapped around the UV square, so the left edge meets
the right edge and the top meets the bottom.

How to use:
  * Open Tools/Blender/BlockMaterials.blend and run this script from its Text Editor (or via the Blender MCP).
  * To tweak a block, open the "BlockBake" scene, select the plane, pick the material (e.g. "VF_Stone") and change
    colors or numbers in the Shader Editor, then run this script. It bakes your edits (REBUILD = False). Set
    REBUILD = True only to reset every material to the recipes in this file.
  * Afterwards, in Unity: Voxelforge > Rebuild Block Textures.
"""

import math
import os

import bpy
import numpy as np

# False: bake the materials as they are in the .blend (keeps your hand tweaks) and only build missing ones from code.
# True: throw away tweaks and rebuild every material from the recipes below.
REBUILD = False

SIZE = 512
SAMPLES = 16
SCENE_NAME = "BlockBake"
# BlockMaterials.blend lives next to this script in Tools/Blender (outside Assets/, so Unity doesn't import it).
OUT_DIR = os.path.join(os.path.dirname(bpy.data.filepath) if bpy.data.filepath else "",
                       "..", "..", "Assets", "_Voxelforge", "Art", "Textures", "Blocks")
# Fallback when run from an unsaved file via MCP.
REPO_OUT_DIR = r"D:/Repos/Voxelforge/Assets/_Voxelforge/Art/Textures/Blocks"


# ---------------------------------------------------------------------------------------------------------------------
# Node graph helpers
# ---------------------------------------------------------------------------------------------------------------------

class Graph:
    """Small helper for building shader node graphs in code, laid out left to right so they're readable in Blender."""

    def __init__(self, material):
        self.tree = material.node_tree
        self.nodes = self.tree.nodes
        self.links = self.tree.links
        self.nodes.clear()
        self._column = 0
        self._row = 0
        self._uv = None

    def node(self, kind, **props):
        n = self.nodes.new(kind)
        for key, value in props.items():
            setattr(n, key, value)
        n.location = (self._column * 220, -self._row * 180)
        self._row += 1
        if self._row > 8:
            self._row = 0
            self._column += 1
        return n

    def link(self, value, socket):
        """Connects a socket, or sets a constant (float or color tuple)."""
        if isinstance(value, bpy.types.NodeSocket):
            self.links.new(value, socket)
        elif isinstance(value, (tuple, list)):
            socket.default_value = (*srgb_to_linear(value), 1.0)
        else:
            socket.default_value = value

    # --- coordinates ---------------------------------------------------------------------------------------------

    def uv(self):
        """(u, v) sockets of the plane's UVs, 0..1."""
        if self._uv is None:
            coords = self.node("ShaderNodeTexCoord")
            sep = self.node("ShaderNodeSeparateXYZ")
            self.links.new(coords.outputs["UV"], sep.inputs[0])
            self._uv = (sep.outputs["X"], sep.outputs["Y"])
        return self._uv

    def torus(self, radius, offset=(0.0, 0.0, 0.0, 0.0), u=None, v=None):
        """
        Wraps the UV square onto a 4D torus: (cos 2pi u, sin 2pi u, cos 2pi v, sin 2pi v) * radius.
        Noise sampled there is seamless on both axes. Bigger radius = more detail per block.
        Pass v=0.0 for a pattern that only varies (and tiles) along u.
        """
        su, sv = self.uv()
        u = su if u is None else u
        v = sv if v is None else v
        angle_u = self.math("MULTIPLY", u, math.tau)
        angle_v = self.math("MULTIPLY", v, math.tau)
        x = self.math("ADD", self.math("MULTIPLY", self.math("COSINE", angle_u), radius), offset[0])
        y = self.math("ADD", self.math("MULTIPLY", self.math("SINE", angle_u), radius), offset[1])
        z = self.math("ADD", self.math("MULTIPLY", self.math("COSINE", angle_v), radius), offset[2])
        w = self.math("ADD", self.math("MULTIPLY", self.math("SINE", angle_v), radius), offset[3])
        vec = self.node("ShaderNodeCombineXYZ")
        self.link(x, vec.inputs[0])
        self.link(y, vec.inputs[1])
        self.link(z, vec.inputs[2])
        return vec.outputs[0], w

    # --- textures ------------------------------------------------------------------------------------------------

    def noise(self, coords, detail=3.0, roughness=0.5, distortion=0.0):
        vec, w = coords
        n = self.node("ShaderNodeTexNoise", noise_dimensions="4D")
        self.link(vec, n.inputs["Vector"])
        self.link(w, n.inputs["W"])
        n.inputs["Scale"].default_value = 1.0
        n.inputs["Detail"].default_value = detail
        n.inputs["Roughness"].default_value = roughness
        n.inputs["Distortion"].default_value = distortion
        return n.outputs["Factor"]

    def voronoi(self, coords, feature="F1", randomness=1.0):
        """Returns (distance, random 0..1 per cell)."""
        vec, w = coords
        n = self.node("ShaderNodeTexVoronoi", voronoi_dimensions="4D", feature=feature)
        self.link(vec, n.inputs["Vector"])
        self.link(w, n.inputs["W"])
        n.inputs["Scale"].default_value = 1.0
        n.inputs["Randomness"].default_value = randomness
        if feature == "DISTANCE_TO_EDGE":
            return n.outputs["Distance"], None
        sep = self.node("ShaderNodeSeparateColor")
        self.links.new(n.outputs["Color"], sep.inputs[0])
        return n.outputs["Distance"], sep.outputs[0]

    # --- math ----------------------------------------------------------------------------------------------------

    def math(self, op, a, b=0.0, clamp=False):
        n = self.node("ShaderNodeMath", operation=op, use_clamp=clamp)
        self.link(a, n.inputs[0])
        self.link(b, n.inputs[1])
        return n.outputs[0]

    def remap(self, value, from_min, from_max, to_min=0.0, to_max=1.0, smooth=False):
        n = self.node("ShaderNodeMapRange", interpolation_type="SMOOTHSTEP" if smooth else "LINEAR", clamp=True)
        self.link(value, n.inputs["Value"])
        n.inputs["From Min"].default_value = from_min
        n.inputs["From Max"].default_value = from_max
        n.inputs["To Min"].default_value = to_min
        n.inputs["To Max"].default_value = to_max
        return n.outputs["Result"]

    def ramp(self, fac, stops, interpolation="EASE"):
        """stops: [(position, (r, g, b)), ...] in sRGB-ish display colors."""
        n = self.node("ShaderNodeValToRGB")
        n.color_ramp.interpolation = interpolation
        elements = n.color_ramp.elements
        while len(elements) > 1:
            elements.remove(elements[-1])
        for i, (position, color) in enumerate(stops):
            element = elements[0] if i == 0 else elements.new(position)
            element.position = position
            element.color = (*srgb_to_linear(color), 1.0)
        self.link(fac, n.inputs["Fac"])
        return n.outputs["Color"]

    def mix(self, a, b, fac, blend="MIX"):
        n = self.node("ShaderNodeMix", data_type="RGBA", blend_type=blend, clamp_result=True)
        self.link(fac, n.inputs[0])
        self.link(a, n.inputs[6])
        self.link(b, n.inputs[7])
        return n.outputs[2]

    def shade(self, color, height, dark=0.62, light=1.12):
        """Paints light and shadow into the color from the height, the hand-painted WoW way."""
        tone = self.remap(height, 0.0, 1.0, dark, light)
        tint = self.node("ShaderNodeCombineColor")
        for i in range(3):
            self.link(tone, tint.inputs[i])
        return self.mix(color, tint.outputs[0], 1.0, blend="MULTIPLY")

    def outputs(self, color, height, roughness, emission=0.0):
        """
        Ends the recipe in four labeled reroutes (VF_Color, VF_Height, VF_Roughness, VF_Emission), which the baker
        reads. Also wires a Principled BSDF so the material previews nicely in Blender's viewport.
        """
        self._column += 1
        self._row = 0
        channels = {}
        for name, value in (("Color", color), ("Height", height), ("Roughness", roughness), ("Emission", emission)):
            if not isinstance(value, bpy.types.NodeSocket):
                constant = self.node("ShaderNodeValue", label=name)
                constant.outputs[0].default_value = value
                value = constant.outputs[0]
            reroute = self.node("NodeReroute", label=name)
            reroute.name = "VF_" + name
            self.links.new(value, reroute.inputs[0])
            channels[name] = reroute.outputs[0]

        self._column += 1
        self._row = 0
        bsdf = self.node("ShaderNodeBsdfPrincipled")
        self.links.new(channels["Color"], bsdf.inputs["Base Color"])
        self.links.new(channels["Roughness"], bsdf.inputs["Roughness"])
        self.links.new(channels["Color"], bsdf.inputs["Emission Color"])
        self.links.new(channels["Emission"], bsdf.inputs["Emission Strength"])
        bump = self.node("ShaderNodeBump")
        bump.inputs["Distance"].default_value = 0.02
        self.links.new(channels["Height"], bump.inputs["Height"])
        self.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
        out = self.node("ShaderNodeOutputMaterial")
        self.links.new(bsdf.outputs[0], out.inputs["Surface"])
        return channels


def srgb_to_linear(color):
    return tuple(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in color)


def linear_to_srgb(rgb):
    rgb = np.clip(rgb, 0.0, 1.0)
    return np.where(rgb <= 0.0031308, rgb * 12.92, 1.055 * np.power(rgb, 1.0 / 2.4) - 0.055)


# ---------------------------------------------------------------------------------------------------------------------
# Block recipes. Colors are picked for a stylized, hand-painted vanilla-WoW feel: chunky shapes, bold value changes.
# ---------------------------------------------------------------------------------------------------------------------

def stone_channels(g, offset=(0.0, 0.0, 0.0, 0.0)):
    """Chunky cobbles with dark mortar-like cracks."""
    cells = g.torus(1.1, offset)
    edge, _ = g.voronoi(cells, "DISTANCE_TO_EDGE", randomness=0.85)
    _, cell_random = g.voronoi(cells, "F1", randomness=0.85)
    grit = g.noise(g.torus(4.0, offset), detail=6, roughness=0.6)
    bulge = g.remap(edge, 0.0, 0.16, smooth=True)
    height = g.math("ADD", g.math("MULTIPLY", bulge, g.remap(cell_random, 0, 1, 0.65, 1.0)),
                    g.math("MULTIPLY", grit, 0.12))
    height = g.math("MULTIPLY", height, 0.9, clamp=True)
    base = g.ramp(cell_random, [(0.0, (0.40, 0.42, 0.46)), (0.45, (0.50, 0.51, 0.53)), (1.0, (0.58, 0.55, 0.52))])
    base = g.mix(base, g.ramp(grit, [(0.3, (0.36, 0.37, 0.40)), (0.7, (0.62, 0.62, 0.62))]), 0.25)
    return base, height, cell_random


def make_stone(g):
    base, height, _ = stone_channels(g)
    return g.outputs(g.shade(base, height, 0.5, 1.15), height, 0.82)


def dirt_channels(g, offset=(3.0, 1.0, -2.0, 5.0)):
    """Warm soil with a few raised pebbles."""
    soil = g.noise(g.torus(2.2, offset), detail=5, roughness=0.55, distortion=0.4)
    pebble_dist, pebble_random = g.voronoi(g.torus(3.0, offset), "F1")
    # 4D Voronoi distances run larger than 2D ones, so "close to the cell centre" means under about 0.5.
    pebble = g.math("MULTIPLY", g.remap(pebble_dist, 0.5, 0.35, smooth=True), g.remap(pebble_random, 0.75, 0.78))
    height = g.math("ADD", g.math("MULTIPLY", soil, 0.55), g.math("MULTIPLY", pebble, 0.45), clamp=True)
    base = g.ramp(soil, [(0.25, (0.30, 0.20, 0.12)), (0.55, (0.42, 0.29, 0.18)), (0.8, (0.52, 0.37, 0.23))])
    base = g.mix(base, (0.58, 0.52, 0.44), pebble)
    return base, height


def make_dirt(g):
    base, height = dirt_channels(g)
    return g.outputs(g.shade(base, height, 0.65, 1.1), height, 0.9)


def grass_channels(g, offset=(-4.0, 2.0, 7.0, -1.0)):
    """Painted grass: swirly brush-stroke blades over soft clumps, deep green hollows and sunny patches."""
    clumps = g.noise(g.torus(2.0, offset), detail=3, roughness=0.5, distortion=0.8)
    blades = g.noise(g.torus(7.0, offset), detail=3, roughness=0.55, distortion=2.5)
    patches = g.noise(g.torus(0.8, offset), detail=2, roughness=0.4)
    height = g.math("ADD", g.math("MULTIPLY", clumps, 0.55), g.math("MULTIPLY", blades, 0.45))
    height = g.remap(height, 0.32, 0.68, smooth=True)
    base = g.ramp(height, [(0.0, (0.13, 0.30, 0.08)), (0.45, (0.27, 0.50, 0.13)), (0.85, (0.50, 0.68, 0.22))])
    sunny = g.ramp(patches, [(0.35, (0.0, 0.0, 0.0)), (0.65, (1.0, 1.0, 1.0))])
    base = g.mix(base, g.mix(base, (0.62, 0.72, 0.25), 0.5), sunny)
    return base, height


def make_grass_top(g):
    base, height = grass_channels(g)
    return g.outputs(g.shade(base, height, 0.75, 1.1), height, 0.88)


def make_grass_side(g):
    """Dirt with a wavy, dripping grass overhang along the top edge (v = 1 is the top of the block)."""
    dirt, dirt_height = dirt_channels(g)
    grass, grass_height = grass_channels(g)
    _, v = g.uv()
    wobble = g.noise(g.torus(2.5, (1.0, 1.0, 1.0, 1.0), v=0.0), detail=2, roughness=0.5)
    drips_dist, drips_random = g.voronoi(g.torus(2.8, (5.0, 0.0, 0.0, 2.0), v=0.0), "F1")
    drip = g.math("MULTIPLY", g.remap(drips_dist, 0.5, 0.1, smooth=True), g.remap(drips_random, 0.4, 1.0, 0.0, 0.22))
    edge = g.math("SUBTRACT", g.math("SUBTRACT", 0.87, g.math("MULTIPLY", wobble, 0.12)), drip)
    band = g.remap(g.math("SUBTRACT", v, edge), -0.015, 0.015)
    lip_shadow = g.remap(g.math("SUBTRACT", edge, v), 0.0, 0.09, 1.0, 0.0)
    shaded_dirt = g.mix(g.shade(dirt, dirt_height, 0.65, 1.1), (0.12, 0.08, 0.05), g.math("MULTIPLY", lip_shadow, 0.55))
    color = g.mix(shaded_dirt, g.shade(grass, grass_height, 0.75, 1.1), band)
    height = g.math("ADD", g.math("MULTIPLY", dirt_height, g.math("SUBTRACT", 1.0, band)),
                    g.math("MULTIPLY", g.math("ADD", g.math("MULTIPLY", grass_height, 0.6), 0.4), band))
    return g.outputs(color, height, 0.88)


def make_sand(g):
    """Pale desert sand: soft drifts, faint wind ripples that come and go, and darker grains."""
    offset = (2.0, -3.0, 1.0, 4.0)
    u, _ = g.uv()
    drifts = g.noise(g.torus(1.2, offset), detail=3, roughness=0.5, distortion=0.5)
    warp = g.noise(g.torus(1.6, (offset[1], offset[0], offset[3], offset[2])), detail=2, roughness=0.5)
    phase = g.math("ADD", g.math("MULTIPLY", u, 3.0 * math.tau), g.math("MULTIPLY", warp, 12.0))
    ripple = g.math("ADD", g.math("MULTIPLY", g.math("SINE", phase), 0.5), 0.5)
    # Ripples only show in some patches, so they don't line up block after block like planks.
    ripple_patch = g.remap(g.noise(g.torus(0.9, (5.0, 5.0, -5.0, 5.0)), detail=1), 0.45, 0.65, smooth=True)
    ripple = g.math("MULTIPLY", ripple, ripple_patch)
    grains_dist, grains_random = g.voronoi(g.torus(14.0, offset), "F1")
    grains = g.math("MULTIPLY", g.remap(grains_dist, 0.55, 0.3), g.remap(grains_random, 0.7, 0.72))
    height = g.math("ADD", g.math("MULTIPLY", drifts, 0.75), g.math("MULTIPLY", ripple, 0.25), clamp=True)
    base = g.ramp(drifts, [(0.3, (0.83, 0.71, 0.47)), (0.7, (0.91, 0.81, 0.58))])
    base = g.mix(base, (0.66, 0.52, 0.33), g.math("MULTIPLY", grains, 0.7))
    return g.outputs(g.shade(base, height, 0.88, 1.05), height, 0.92)


def strata_channels(g, offset, bands, colors, crack_scale):
    """Horizontal rock layers (v runs up the side of a block) with cracks between chunks."""
    _, v = g.uv()
    warp = g.noise(g.torus(1.2, offset), detail=3, roughness=0.5)
    layer = g.math("FRACT", g.math("ADD", g.math("MULTIPLY", v, float(bands)), g.math("MULTIPLY", warp, 0.6)))
    ledge = g.remap(layer, 0.0, 0.85, 0.35, 1.0, smooth=True)
    crack, _ = g.voronoi(g.torus(crack_scale, offset), "DISTANCE_TO_EDGE", randomness=0.7)
    crack = g.remap(crack, 0.0, 0.07, smooth=True)
    grit = g.noise(g.torus(5.0, offset), detail=5, roughness=0.6)
    height = g.math("MULTIPLY", g.math("ADD", g.math("MULTIPLY", ledge, crack), g.math("MULTIPLY", grit, 0.1)), 0.92,
                    clamp=True)
    band_id = g.math("FRACT", g.math("MULTIPLY", g.math("FLOOR", g.math("ADD", g.math("MULTIPLY", v, float(bands)),
                                                                        g.math("MULTIPLY", warp, 0.6))), 0.37))
    base = g.ramp(band_id, colors, interpolation="CONSTANT")
    base = g.mix(base, g.ramp(grit, [(0.3, (0.0, 0.0, 0.0)), (0.7, (1.0, 1.0, 1.0))]), 0.08, blend="OVERLAY")
    return base, height


def make_sandstone(g):
    base, height = strata_channels(g, (6.0, 2.0, -1.0, 3.0), 4,
                                   [(0.0, (0.80, 0.62, 0.40)), (0.34, (0.71, 0.52, 0.32)), (0.67, (0.86, 0.70, 0.48))],
                                   0.9)
    return g.outputs(g.shade(base, height, 0.6, 1.1), height, 0.85)


def make_red_rock(g):
    base, height = strata_channels(g, (-3.0, 6.0, 2.0, -4.0), 3,
                                   [(0.0, (0.62, 0.27, 0.16)), (0.34, (0.50, 0.20, 0.12)), (0.67, (0.72, 0.38, 0.22))],
                                   1.3)
    return g.outputs(g.shade(base, height, 0.5, 1.12), height, 0.8)


def make_snow(g):
    """Soft drifts, cool blue hollows and a few glints."""
    offset = (7.0, -5.0, 3.0, 1.0)
    drift = g.noise(g.torus(1.6, offset), detail=4, roughness=0.45, distortion=0.3)
    glint_dist, glint_random = g.voronoi(g.torus(10.0, offset), "F1")
    glint = g.math("MULTIPLY", g.remap(glint_dist, 0.25, 0.05), g.remap(glint_random, 0.93, 0.95))
    height = g.remap(drift, 0.25, 0.75, smooth=True)
    base = g.ramp(height, [(0.0, (0.66, 0.74, 0.88)), (0.5, (0.86, 0.90, 0.97)), (1.0, (0.97, 0.98, 1.0))])
    base = g.mix(base, (1.0, 1.0, 1.0), glint)
    roughness = g.remap(glint, 0.0, 1.0, 0.55, 0.15)
    return g.outputs(base, height, roughness)


def make_mana_crystal_ore(g):
    """Stone with clusters of glowing purple-blue mana crystals."""
    stone, stone_height, _ = stone_channels(g, (11.0, -7.0, 5.0, 2.0))
    offset = (-9.0, 4.0, 8.0, -6.0)
    # Big clusters: about a third of the Voronoi cells hold one, filling most of their cell.
    crystal_dist, crystal_random = g.voronoi(g.torus(1.3, offset), "F1")
    facets, _ = g.voronoi(g.torus(4.5, offset), "DISTANCE_TO_EDGE", randomness=0.9)
    facets = g.remap(facets, 0.0, 0.1)
    chosen = g.remap(crystal_random, 0.62, 0.66)
    inside = g.math("MULTIPLY", g.remap(crystal_dist, 0.78, 0.7), chosen)
    core = g.remap(crystal_dist, 0.78, 0.15)
    shard = g.math("ADD", g.math("MULTIPLY", core, 0.4), g.math("MULTIPLY", facets, 0.15))
    height = g.mix(stone_height, g.math("ADD", shard, 0.45), inside)
    crystal = g.ramp(g.math("ADD", g.math("MULTIPLY", core, 0.75), g.math("MULTIPLY", facets, 0.25)),
                     [(0.0, (0.30, 0.12, 0.60)), (0.45, (0.50, 0.25, 0.90)), (0.8, (0.55, 0.85, 1.0)),
                      (1.0, (0.85, 0.98, 1.0))])
    color = g.mix(g.shade(stone, stone_height, 0.5, 1.15), crystal, inside)
    emission = g.math("MULTIPLY", inside, g.remap(core, 0.0, 1.0, 0.35, 1.0))
    roughness = g.remap(inside, 0.0, 1.0, 0.82, 0.18)
    return g.outputs(color, g.math("ADD", height, 0.0, clamp=True), roughness, emission)


def make_log_bark(g):
    """Chopped-tree bark (Roadmap 3.2): deep vertical furrows that wander a little, rough plates between them."""
    offset = (4.0, -6.0, 2.0, 7.0)
    u, _ = g.uv()
    warp = g.noise(g.torus(1.4, offset), detail=3, roughness=0.5)
    # 5 furrows across the face; the warp comes from the torus, so the stripes still tile on both axes.
    phase = g.math("ADD", g.math("MULTIPLY", u, 5.0 * math.tau), g.math("MULTIPLY", warp, 5.0))
    ridge = g.remap(g.math("ADD", g.math("MULTIPLY", g.math("SINE", phase), 0.5), 0.5), 0.1, 0.8, smooth=True)
    plates, _ = g.voronoi(g.torus(0.8, offset), "DISTANCE_TO_EDGE", randomness=0.8)
    plates = g.remap(plates, 0.0, 0.05, 0.75, 1.0, smooth=True)
    grit = g.noise(g.torus(5.0, offset), detail=6, roughness=0.6)
    height = g.math("ADD", g.math("MULTIPLY", g.math("MULTIPLY", ridge, plates), 0.8), g.math("MULTIPLY", grit, 0.2),
                    clamp=True)
    base = g.ramp(height, [(0.0, (0.16, 0.10, 0.06)), (0.5, (0.36, 0.24, 0.14)), (1.0, (0.52, 0.38, 0.23))])
    return g.outputs(g.shade(base, height, 0.55, 1.12), height, 0.9)


def make_log_top(g):
    """The cut end of a log: warm growth rings in a rounded square, a dark heart, and a bark rim around the edge."""
    offset = (-2.0, 8.0, -6.0, 3.0)
    u, v = g.uv()
    du = g.math("ABSOLUTE", g.math("SUBTRACT", u, 0.5))
    dv = g.math("ABSOLUTE", g.math("SUBTRACT", v, 0.5))
    round_r = g.math("SQRT", g.math("ADD", g.math("MULTIPLY", du, du), g.math("MULTIPLY", dv, dv)))
    square_r = g.math("MAXIMUM", du, dv)
    wobble = g.noise(g.torus(1.5, offset), detail=2, roughness=0.5)
    r = g.math("ADD", g.math("ADD", g.math("MULTIPLY", round_r, 0.4), g.math("MULTIPLY", square_r, 0.6)),
               g.math("MULTIPLY", wobble, 0.03))
    # Wide pale growth rings separated by thin dark lines.
    ring = g.math("ADD", g.math("MULTIPLY", g.math("SINE", g.math("MULTIPLY", r, 14.0 * math.tau)), 0.5), 0.5)
    ring = g.remap(ring, 0.0, 0.35, smooth=True)
    grain = g.noise(g.torus(6.0, offset), detail=5, roughness=0.6)
    wood = g.ramp(ring, [(0.0, (0.50, 0.33, 0.18)), (1.0, (0.82, 0.64, 0.42))])
    wood = g.mix(wood, (0.70, 0.52, 0.32), g.math("MULTIPLY", grain, 0.35))
    wood = g.mix(wood, (0.42, 0.27, 0.15), g.remap(r, 0.07, 0.0, smooth=True))  # Dark heart.
    rim = g.remap(g.math("ADD", square_r, g.math("MULTIPLY", wobble, 0.02)), 0.435, 0.45, smooth=True)
    bark = g.ramp(grain, [(0.3, (0.18, 0.11, 0.06)), (0.7, (0.36, 0.24, 0.14))])
    color = g.mix(wood, bark, rim)
    wood_height = g.math("ADD", g.math("MULTIPLY", ring, 0.12), g.math("MULTIPLY", grain, 0.08))
    height = g.math("ADD", g.math("ADD", wood_height, 0.35),
                    g.math("MULTIPLY", rim, g.math("ADD", 0.15, g.math("MULTIPLY", grain, 0.3))), clamp=True)
    return g.outputs(g.shade(color, height, 0.7, 1.1), height, 0.85)


# Layer name -> (recipe, normal strength, AO strength, tiles vertically). Names must match the BlockTexture assets
# in Unity. Grass Side doesn't tile vertically (grass on top, dirt below), so its edges aren't wrapped into each other.
LAYERS = {
    "GrassTop": (make_grass_top, 3.0, 0.8, True),
    "GrassSide": (make_grass_side, 3.0, 0.8, False),
    "Dirt": (make_dirt, 3.0, 0.7, True),
    "Stone": (make_stone, 5.0, 1.0, True),
    "Sand": (make_sand, 1.5, 0.4, True),
    "Sandstone": (make_sandstone, 4.0, 0.9, True),
    "RedRock": (make_red_rock, 5.0, 1.0, True),
    "Snow": (make_snow, 1.5, 0.35, True),
    "ManaCrystalOre": (make_mana_crystal_ore, 5.0, 1.0, True),
    "LogBark": (make_log_bark, 4.0, 0.9, True),
    "LogTop": (make_log_top, 2.5, 0.6, False),
}


# ---------------------------------------------------------------------------------------------------------------------
# Baking
# ---------------------------------------------------------------------------------------------------------------------

def bake_scene():
    scene = bpy.data.scenes.get(SCENE_NAME) or bpy.data.scenes.new(SCENE_NAME)
    plane = scene.objects.get("BakePlane")
    if plane is None:
        mesh = bpy.data.meshes.new("BakePlane")
        mesh.from_pydata([(-1, -1, 0), (1, -1, 0), (1, 1, 0), (-1, 1, 0)], [], [(0, 1, 2, 3)])
        uv = mesh.uv_layers.new(name="UVMap")
        for loop, coord in zip(uv.data, [(0, 0), (1, 0), (1, 1), (0, 1)]):
            loop.uv = coord
        plane = bpy.data.objects.new("BakePlane", mesh)
        scene.collection.objects.link(plane)
    scene.render.engine = "CYCLES"
    scene.cycles.samples = SAMPLES
    scene.cycles.device = "CPU"
    scene.render.bake.margin = 0
    scene.view_settings.view_transform = "Standard"
    return scene, plane


def channel_image(name, colorspace):
    image = bpy.data.images.get(name)
    if image is not None and tuple(image.size) != (SIZE, SIZE):
        bpy.data.images.remove(image)
        image = None
    if image is None:
        image = bpy.data.images.new(name, SIZE, SIZE, alpha=False, float_buffer=True)
    image.colorspace_settings.name = colorspace
    return image


def bake_channel(scene, plane, material, socket_name, colorspace):
    """Bakes one VF_* reroute through an Emission shader. Returns an (H, W) or (H, W, 3) float array."""
    tree = material.node_tree
    source = tree.nodes[socket_name].outputs[0]
    emission = tree.nodes.new("ShaderNodeEmission")
    output = tree.nodes.new("ShaderNodeOutputMaterial")
    output.is_active_output = True
    tree.links.new(source, emission.inputs["Color"])
    tree.links.new(emission.outputs[0], output.inputs["Surface"])

    image = channel_image(f"VF_bake_{socket_name}", "Linear Rec.709")
    target = tree.nodes.new("ShaderNodeTexImage")
    target.image = image
    tree.nodes.active = target

    plane.data.materials.clear()
    plane.data.materials.append(material)
    with bpy.context.temp_override(scene=scene, active_object=plane, selected_objects=[plane],
                                   view_layer=scene.view_layers[0]):
        bpy.ops.object.bake(type="EMIT", use_clear=True)

    pixels = np.empty(SIZE * SIZE * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    for n in (target, emission, output):
        tree.nodes.remove(n)
    rgb = pixels.reshape(SIZE, SIZE, 4)[:, :, :3]
    return rgb if colorspace == "sRGB" else rgb[:, :, 0].copy()


def shift(a, offset, axis, wrap):
    """np.roll, or (when the texture doesn't tile on that axis) a shift that repeats the edge pixels instead."""
    if wrap:
        return np.roll(a, offset, axis=axis)
    index = np.clip(np.arange(a.shape[axis]) - offset, 0, a.shape[axis] - 1)
    return np.take(a, index, axis=axis)


def normal_from_height(height, strength, tile_v=True):
    """Tangent-space normal from a height map (rows run up the texture, so +Y is OpenGL style)."""
    dx = (shift(height, -1, 1, True) - shift(height, 1, 1, True)) * 0.5
    dy = (shift(height, -1, 0, tile_v) - shift(height, 1, 0, tile_v)) * 0.5
    n = np.stack([-dx * strength * SIZE / 64.0, -dy * strength * SIZE / 64.0, np.ones_like(height)], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def occlusion_from_height(height, strength, tile_v=True):
    """Darkens crevices: how far each pixel sits below the blurred height around it."""
    blurred = height.copy()
    for radius in (2, 4, 8):
        acc = np.zeros_like(height)
        for oy in (-radius, 0, radius):
            for ox in (-radius, 0, radius):
                acc += shift(shift(height, oy, 0, tile_v), ox, 1, True)
        blurred = (blurred + acc / 9.0) * 0.5
    return np.clip(1.0 - np.maximum(blurred - height, 0.0) * 4.0 * strength, 0.0, 1.0)


def save_png(name, rgb):
    """Writes an (H, W, 3) 0..1 array to an 8-bit PNG exactly as given (encode albedo to sRGB first)."""
    image = bpy.data.images.get(name) or bpy.data.images.new(name, SIZE, SIZE, alpha=False)
    image.colorspace_settings.name = "Non-Color"
    rgba = np.ones((SIZE, SIZE, 4), dtype=np.float32)
    rgba[:, :, :3] = np.clip(rgb, 0.0, 1.0)
    image.pixels.foreach_set(rgba.ravel())
    image.filepath_raw = os.path.join(output_dir(), name + ".png")
    image.file_format = "PNG"
    image.save()


def output_dir():
    path = os.path.normpath(OUT_DIR) if bpy.data.filepath else REPO_OUT_DIR
    os.makedirs(path, exist_ok=True)
    return path


def run(only=None):
    scene, plane = bake_scene()
    for layer, (recipe, normal_strength, ao_strength, tile_v) in LAYERS.items():
        if only and layer not in only:
            continue
        material = bpy.data.materials.get("VF_" + layer)
        if material is None or REBUILD:
            material = material or bpy.data.materials.new("VF_" + layer)
            material.use_nodes = True
            recipe(Graph(material))

        color = bake_channel(scene, plane, material, "VF_Color", "sRGB")
        height = bake_channel(scene, plane, material, "VF_Height", "Non-Color")
        roughness = bake_channel(scene, plane, material, "VF_Roughness", "Non-Color")
        emission = bake_channel(scene, plane, material, "VF_Emission", "Non-Color")

        save_png(f"{layer}_Albedo", linear_to_srgb(color))
        save_png(f"{layer}_Height", np.repeat(height[:, :, None], 3, axis=2))
        save_png(f"{layer}_Normal", normal_from_height(height, normal_strength, tile_v))
        mask = np.stack([occlusion_from_height(height, ao_strength, tile_v), 1.0 - roughness, emission], axis=-1)
        save_png(f"{layer}_Mask", mask)
        print(f"[Voxelforge] Baked {layer}")
    print(f"[Voxelforge] Block textures written to {output_dir()}")


if __name__ == "__main__":
    run()
