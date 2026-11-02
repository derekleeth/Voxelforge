using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Voxels;
using Object = UnityEngine.Object;

namespace Voxelforge.Gameplay
{
    /// <summary>Which heat map a player's map shows (Roadmap 5.7).</summary>
    public enum HeatMode : byte
    {
        Off = 0,
        Ore = 1,
        Herbs = 2,
    }

    /// <summary>What one player's map should show of the heat maps this frame (Roadmap 5.7).</summary>
    public readonly struct MapHeatView
    {
        public readonly MapHeat Heat;
        public readonly HeatMode Mode;
        /// <summary>The tiers they can gather, a bit per <see cref="MapHeat"/> layer. 0: nothing to show.</summary>
        public readonly int Mask;

        public MapHeatView(MapHeat heat, HeatMode mode, int mask)
        {
            Heat = heat;
            Mode = mode;
            Mask = mask;
        }

        public bool IsShowing => Heat != null && Mode != HeatMode.Off && Mask != 0;
    }

    /// <summary>
    /// The resource heat maps (Roadmap 5.7, D-038): a soft wash over a player's World Map and minimap showing where ore or
    /// herbs are rich, in each tier's colour (blended where several are), counting only the tiers that player can gather.
    /// The commonest ore (copper, rich nearly everywhere) drops off the map once a rarer one can be mined, or it would
    /// cover everything. What's in the ground comes
    /// from the seed (<see cref="ResourceHeatCache"/>); this colours it. One per PC, shared by every local player: the
    /// textures are kept per tile, mode and set of tiers, so two players with the same skills share them.
    /// </summary>
    public sealed class MapHeat : IDisposable
    {
        /// <summary>One tier on a heat map: an ore vein or a herb, its colour and the skill that gathers it.</summary>
        public readonly struct Layer
        {
            public readonly string Name;
            public readonly Color32 Color;
            public readonly ProfessionId Profession;
            public readonly int Skill;
            /// <summary>Ore: the vein's index in the terrain's ore veins. -1 for herbs.</summary>
            public readonly int Vein;
            /// <summary>Herbs: how much it grows in each biome, 0-1, by biome index.</summary>
            public readonly float[] ByBiome;

            public Layer(string name, Color32 color, ProfessionId profession, int skill, int vein, float[] byBiome)
            {
                Name = name;
                Color = color;
                Profession = profession;
                Skill = skill;
                Vein = vein;
                ByBiome = byBiome;
            }
        }

        // Below this a patch shows nothing for a tier, so the wash only marks places worth going.
        const float ShowFrom = 0.3f;
        // How see-through the wash is, from barely rich to richest: the land and markers must still read through it.
        const float LeastAlpha = 0.15f;
        const float MostAlpha = 0.5f;
        // A herb spot (one per 12x12 blocks) grows at most this often in its best biome (FoliageDecorationBuilder).
        const float RichestHerb = 0.35f;
        const int Capacity = 256;

        readonly ResourceHeatCache _cache;
        readonly ProfessionBook _book;
        readonly List<Layer> _ore = new List<Layer>();
        readonly List<Layer> _herbs = new List<Layer>();
        readonly Dictionary<(int2 tile, HeatMode mode, int mask), (Texture2D texture, int used)> _textures =
            new Dictionary<(int2, HeatMode, int), (Texture2D, int)>();
        readonly List<(int2, HeatMode, int)> _evict = new List<(int2, HeatMode, int)>();
        readonly Color32[] _pixels = new Color32[ResourceHeatRules.Cells * ResourceHeatRules.Cells];
        int _frame;

        /// <param name="ore">The ore tiers, rarest first (the terrain's vein order): where two are rich, the first shows.</param>
        /// <param name="herbs">The herbs, in the same way.</param>
        public MapHeat(ResourceHeatCache cache, ProfessionBook book, IEnumerable<Layer> ore, IEnumerable<Layer> herbs)
        {
            _cache = cache;
            _book = book;
            _ore.AddRange(ore);
            _herbs.AddRange(herbs);
        }

        public IReadOnlyList<Layer> OreLayers => _ore;
        public IReadOnlyList<Layer> HerbLayers => _herbs;

        /// <summary>
        /// The heat maps for this world: a layer per ore vein (colour by block) and per herb (colour by item), with the
        /// skill each needs from the Profession Book. Null if the world or book isn't ready.
        /// </summary>
        public static MapHeat Create(VoxelWorldManager world, ProfessionBook book)
        {
            ResourceHeatCache cache = world != null ? world.ResourceHeat : null;
            if (cache == null || book == null || world.TerrainSettings == null)
                return null;
            var ore = new List<Layer>();
            IReadOnlyList<OreVein> veins = world.TerrainSettings.OreVeins;
            for (int v = 0; v < veins.Count && v < 4; v++)
            {
                BlockDefinition block = veins[v] != null ? veins[v].Block : null;
                if (block == null || !book.TryGetNode(GatherNodeKind.Ore, block.Id, out GatherNode node))
                    continue;
                ore.Add(new Layer(block.DisplayName, ColorOf(block.Id), node.Profession, node.RequiredSkill, v, null));
            }

            var herbs = new List<Layer>();
            IReadOnlyList<BiomeSettings> biomes = world.TerrainSettings.Biomes;
            if (world.Decorations != null)
            {
                foreach (DecorationLayer.TypeData type in world.Decorations.Types)
                {
                    DecorationDefinition herb = type.Definition;
                    if (herb == null || herb.Kind != DecorationKind.Herb ||
                        !book.TryGetNode(GatherNodeKind.Herb, herb.HerbItem, out GatherNode node))
                        continue;
                    var byBiome = new float[biomes.Count];
                    for (int b = 0; b < biomes.Count; b++)
                        byBiome[b] = math.saturate(herb.Density * herb.WeightIn(biomes[b].Name) / RichestHerb);
                    herbs.Add(new Layer(NameOf(herb), ColorOf(herb.HerbItem), node.Profession, node.RequiredSkill, -1, byBiome));
                }
            }
            herbs.Sort((a, b) => b.Skill.CompareTo(a.Skill)); // Rarest (most skilled) first, like the ore.
            return new MapHeat(cache, book, ore, herbs);
        }

        static string NameOf(DecorationDefinition herb) =>
            herb.name.StartsWith("Herb", StringComparison.Ordinal) ? herb.name.Substring(4) : herb.name;

        /// <summary>Each tier's colour on the map: the ore's or the flower's own.</summary>
        static Color32 ColorOf(ushort id) => id switch
        {
            BlockIds.CopperOre => new Color32(240, 130, 50, 255),
            BlockIds.IronOre => new Color32(200, 60, 45, 255),
            BlockIds.ManaCrystalOre => new Color32(150, 80, 255, 255),
            ItemIds.Sunpetal => new Color32(225, 165, 10, 255), // A deeper gold, so its name reads on the parchment.
            ItemIds.Frostbloom => new Color32(90, 190, 255, 255),
            ItemIds.Emberthorn => new Color32(255, 70, 30, 255),
            _ => new Color32(255, 255, 255, 255),
        };

        IReadOnlyList<Layer> LayersOf(HeatMode mode) => mode == HeatMode.Ore ? _ore : mode == HeatMode.Herbs ? _herbs : null;

        static ProfessionId ProfessionOf(HeatMode mode) => mode == HeatMode.Ore ? ProfessionId.Mining : ProfessionId.Herbalism;

        /// <summary>True if <paramref name="skills"/> are enough to see <paramref name="mode"/>'s heat map at all.</summary>
        public static bool IsUnlocked(HeatMode mode, ProfessionSkills skills, ProfessionBook book) =>
            mode != HeatMode.Off && ProfessionRules.HasHeatMap(skills.Get(ProfessionOf(mode)), book);

        /// <summary>The next heat map after <paramref name="mode"/> that <paramref name="skills"/> unlock, ending on Off.</summary>
        public static HeatMode Next(HeatMode mode, ProfessionSkills skills, ProfessionBook book)
        {
            for (int step = 1; step <= 2; step++)
            {
                var next = (HeatMode)(((int)mode + step) % 3);
                if (next == HeatMode.Off || IsUnlocked(next, skills, book))
                    return next;
            }
            return HeatMode.Off;
        }

        /// <summary>What a player with <paramref name="skills"/> sees of <paramref name="mode"/>'s heat map.</summary>
        public MapHeatView ViewFor(HeatMode mode, ProfessionSkills skills) => new MapHeatView(this, mode, MaskFor(mode, skills));

        /// <summary>Which of <paramref name="mode"/>'s tiers (a bit per layer) <paramref name="skills"/> can gather.</summary>
        public int MaskFor(HeatMode mode, ProfessionSkills skills)
        {
            IReadOnlyList<Layer> layers = LayersOf(mode);
            if (layers == null || !IsUnlocked(mode, skills, _book))
                return 0;
            int mask = 0, unlocked = 0;
            for (int i = 0; i < layers.Count; i++)
            {
                if (skills.Get(layers[i].Profession) >= layers[i].Skill)
                {
                    mask |= 1 << i;
                    unlocked++;
                }
            }
            // The commonest ore is the last layer: once anything rarer shows, leave it off.
            if (mode == HeatMode.Ore && unlocked > 1)
                mask &= ~(1 << (layers.Count - 1));
            return mask;
        }

        /// <summary>The names of the tiers in <paramref name="mask"/>, e.g. "Iron, Copper", for the map's key.</summary>
        public string Describe(HeatMode mode, int mask)
        {
            IReadOnlyList<Layer> layers = LayersOf(mode);
            if (layers == null)
                return string.Empty;
            var names = new List<string>();
            for (int i = 0; i < layers.Count; i++)
            {
                if ((mask & (1 << i)) != 0)
                    names.Add($"<color=#{ColorUtility.ToHtmlStringRGB(layers[i].Color)}>{layers[i].Name}</color>");
            }
            return string.Join(", ", names);
        }

        /// <summary>
        /// The colour of one patch: the tiers in <paramref name="mask"/> that are rich enough there, their colours blended
        /// by how rich each is, more opaque the richer the richest is; clear if none is.
        /// </summary>
        public Color32 Paint(HeatMode mode, int mask, in ResourceHeat heat)
        {
            IReadOnlyList<Layer> layers = LayersOf(mode);
            if (layers == null)
                return default;
            float3 colour = float3.zero;
            float weights = 0f, best = 0f;
            for (int i = 0; i < layers.Count; i++)
            {
                if ((mask & (1 << i)) == 0)
                    continue;
                Layer layer = layers[i];
                float score = layer.Vein >= 0
                    ? heat.Ore[layer.Vein]
                    : heat.Biome < layer.ByBiome.Length ? layer.ByBiome[heat.Biome] : 0f;
                if (score < ShowFrom)
                    continue;
                colour += new float3(layer.Color.r, layer.Color.g, layer.Color.b) * score;
                weights += score;
                best = math.max(best, score);
            }
            if (weights <= 0f)
                return default;
            colour /= weights;
            float alpha = math.lerp(LeastAlpha, MostAlpha, math.saturate((best - ShowFrom) / (1f - ShowFrom)));
            return new Color32((byte)colour.x, (byte)colour.y, (byte)colour.z, (byte)(255f * alpha));
        }

        /// <summary>
        /// The heat map of <paramref name="tile"/> for <paramref name="mode"/> and the tiers in <paramref name="mask"/>, or
        /// false (and it's queued) if the tile isn't worked out yet.
        /// </summary>
        public bool TryGetTexture(int2 tile, HeatMode mode, int mask, out Texture2D texture)
        {
            texture = null;
            if (mode == HeatMode.Off || mask == 0)
                return false;
            (int2, HeatMode, int) key = (tile, mode, mask);
            if (_textures.TryGetValue(key, out (Texture2D texture, int used) cached))
            {
                _textures[key] = (cached.texture, _frame);
                texture = cached.texture;
                return true;
            }
            if (!_cache.TryGet(tile, out ResourceHeat[] heat))
                return false;
            for (int i = 0; i < heat.Length; i++)
                _pixels[i] = Paint(mode, mask, heat[i]);
            texture = new Texture2D(ResourceHeatRules.Cells, ResourceHeatRules.Cells, TextureFormat.RGBA32, mipChain: false)
            {
                name = $"Heat {mode} {tile.x},{tile.y}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            texture.SetPixels32(_pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            _textures[key] = (texture, _frame);
            return true;
        }

        /// <summary>Frees the textures nobody drew lately, once there are too many. Call once a frame, after drawing.</summary>
        public void EndFrame()
        {
            int extra = _textures.Count - Capacity;
            if (extra > 0)
            {
                _evict.Clear();
                foreach (KeyValuePair<(int2, HeatMode, int), (Texture2D texture, int used)> pair in _textures)
                {
                    if (pair.Value.used < _frame)
                        _evict.Add(pair.Key);
                }
                _evict.Sort((a, b) => _textures[a].used.CompareTo(_textures[b].used));
                for (int i = 0; i < extra && i < _evict.Count; i++)
                {
                    Object.Destroy(_textures[_evict[i]].texture);
                    _textures.Remove(_evict[i]);
                }
            }
            _frame++;
        }

        public void Dispose()
        {
            foreach ((Texture2D texture, int _) in _textures.Values)
            {
                if (Application.isPlaying)
                    Object.Destroy(texture);
                else
                    Object.DestroyImmediate(texture);
            }
            _textures.Clear();
        }
    }
}
