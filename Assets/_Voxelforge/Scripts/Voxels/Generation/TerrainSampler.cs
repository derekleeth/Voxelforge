using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>One column of terrain: how high the ground is and which biome owns its surface.</summary>
    public struct TerrainColumn
    {
        /// <summary>World y of the top block of ground.</summary>
        public int Height;
        /// <summary>Index into the biome list of the biome whose blocks cover this column.</summary>
        public int Biome;
        /// <summary>Blended ore richness of the biomes here. 1 = normal.</summary>
        public float OreRichness;
        /// <summary>Trunk blocks stacked on the ground here (see <see cref="TreePlacement"/>). 0 = no tree.</summary>
        public int TreeHeight;
        /// <summary>
        /// How much of this column is mesa (Roadmap 4.7): 0 on open ground, 1 on a mesa's flat top, in between on its
        /// cliffs. Blended across biome borders like the height.
        /// </summary>
        public float Mesa;
    }

    /// <summary>
    /// Decides which block goes where. Every answer depends only on the seed, the settings and the world position,
    /// so any chunk can be generated alone, in any order, on any thread, and always comes out the same.
    /// <para>
    /// Burst-safe. Sticks to +, *, floor, sqrt and simplex noise (no sin/cos/exp/pow), whose results don't depend
    /// on math-library precision, to keep worlds identical across machines.
    /// </para>
    /// </summary>
    public struct TerrainSampler : IDisposable
    {
        /// <summary>
        /// Bump this whenever terrain output changes on purpose (new rules or new settings values).
        /// Saves (Roadmap 3.4) record it, so an old save can tell it was made by a different generator.
        /// </summary>
        public const int GeneratorVersion = 3; // 2: trees (Roadmap 2.6). 3: Thunderhoof Plains and mesas (4.7).

        /// <summary>A mesa's cliff shows banded rock where at least this much of the column is mesa; below it, the usual layers.</summary>
        public const float MesaRockFrom = 0.15f;

        /// <summary>Only a mesa's flat top (at least this much mesa) gets its biome's grass; its cliffs are bare rock.</summary>
        public const float MesaCapFrom = 0.9f;

        /// <summary>Spreads climate noise (which mostly stays near 0) across the whole -1..1 climate map.</summary>
        const float ClimateStretch = 1.6f;

        // Salts give each noise layer its own patch of the noise field.
        const uint TemperatureSalt = 1;
        const uint RuggednessSalt = 2;
        const uint JitterSalt = 3;
        const uint TunnelASalt = 4;
        const uint TunnelBSalt = 5;
        const uint CavernSalt = 6;
        const uint OreSalt = 7;
        const uint BiomeSaltBase = 100;
        const uint MesaSaltBase = 200;

        TerrainParams _params;
        NativeArray<BiomeInfo> _biomes; // Borrowed, not disposed here.
        NativeArray<float2> _biomeOffsets;
        NativeArray<float2> _mesaOffsets;
        NativeArray<float> _weights;
        float2 _temperatureOffset;
        float2 _ruggednessOffset;
        float3 _tunnelAOffset;
        float3 _tunnelBOffset;
        float3 _cavernOffset;
        float3 _oreOffset;
        uint _jitterHash;
        uint _treeHash;

        /// <param name="biomes">Read, not owned: the caller keeps it alive and disposes it.</param>
        public TerrainSampler(WorldSeed seed, TerrainParams terrain, NativeArray<BiomeInfo> biomes, Allocator allocator)
        {
            _params = terrain;
            _biomes = biomes;
            _biomeOffsets = new NativeArray<float2>(biomes.Length, allocator, NativeArrayOptions.UninitializedMemory);
            _weights = new NativeArray<float>(biomes.Length, allocator, NativeArrayOptions.UninitializedMemory);
            _mesaOffsets = new NativeArray<float2>(biomes.Length, allocator, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < biomes.Length; i++)
            {
                _biomeOffsets[i] = seed.NoiseOffset(BiomeSaltBase + (uint)i).xy;
                _mesaOffsets[i] = seed.NoiseOffset(MesaSaltBase + (uint)i).xy;
            }

            _temperatureOffset = seed.NoiseOffset(TemperatureSalt).xy;
            _ruggednessOffset = seed.NoiseOffset(RuggednessSalt).xy;
            _tunnelAOffset = seed.NoiseOffset(TunnelASalt);
            _tunnelBOffset = seed.NoiseOffset(TunnelBSalt);
            _cavernOffset = seed.NoiseOffset(CavernSalt);
            _oreOffset = seed.NoiseOffset(OreSalt);
            _jitterHash = seed.Hash(JitterSalt);
            _treeHash = TreePlacement.SeedHash(seed);
        }

        public int BiomeCount => _biomes.Length;

        /// <summary>
        /// The climate at a spot: x = temperature (-1 cold to +1 hot), y = ruggedness (-1 flat to +1 mountainous).
        /// Biomes claim the parts of this map nearest their own climate point.
        /// </summary>
        public float2 Climate(float2 position)
        {
            float2 p = position * _params.ClimateFrequency;
            float temperature = Fbm(p + _temperatureOffset, 2, 0.5f);
            float ruggedness = Fbm(p + _ruggednessOffset, 2, 0.5f);
            return math.clamp(new float2(temperature, ruggedness) * ClimateStretch, -1f, 1f);
        }

        /// <summary>
        /// How much each biome shapes the ground at <paramref name="climate"/>. The nearest biome always gets full
        /// weight; others fade in as they come within <see cref="TerrainParams.BiomeBlend"/> of being nearest.
        /// The weights add up to 1, and change smoothly, so there are no cliffs at biome borders.
        /// </summary>
        public void BiomeWeights(float2 climate, NativeArray<float> weights)
        {
            float nearest = float.MaxValue;
            for (int i = 0; i < _biomes.Length; i++)
            {
                weights[i] = math.distance(climate, _biomes[i].Climate);
                nearest = math.min(nearest, weights[i]);
            }

            float total = 0f;
            for (int i = 0; i < _biomes.Length; i++)
            {
                float w = math.saturate(1f - (weights[i] - nearest) / _params.BiomeBlend);
                w *= w;
                weights[i] = w;
                total += w;
            }

            for (int i = 0; i < _biomes.Length; i++)
                weights[i] /= total;
        }

        /// <summary>
        /// The biome colour for <paramref name="tint"/> at a spot (Roadmap 2.6), blended with the same weights that
        /// blend the ground's shape, so colours fade smoothly across biome borders. White for <see cref="BiomeTint.None"/>.
        /// </summary>
        public float3 TintAt(float2 position, BiomeTint tint)
        {
            if (tint == BiomeTint.None)
                return 1f;
            BiomeWeights(Climate(position), _weights);
            float3 colour = 0f;
            for (int i = 0; i < _biomes.Length; i++)
                colour += _weights[i] * (tint == BiomeTint.Grass ? _biomes[i].GrassTint : _biomes[i].FoliageTint);
            return colour;
        }

        /// <summary>The biome whose climate point is closest.</summary>
        public int NearestBiome(float2 climate)
        {
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < _biomes.Length; i++)
            {
                float d = math.distancesq(climate, _biomes[i].Climate);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }
            return best;
        }

        public TerrainColumn SampleColumn(int x, int z)
        {
            var position = new float2(x, z);
            float2 climate = Climate(position);
            BiomeWeights(climate, _weights);

            float height = 0f;
            float oreRichness = 0f;
            float mesa = 0f;
            for (int i = 0; i < _biomes.Length; i++)
            {
                float w = _weights[i];
                if (w <= 0f)
                    continue;
                height += w * BiomeHeight(i, position);
                oreRichness += w * _biomes[i].OreRichness;
                if (_biomes[i].MesaHeight > 0f)
                {
                    float m = w * MesaShape(i, position);
                    height += m * _biomes[i].MesaHeight;
                    mesa += m;
                }
            }

            var column = new TerrainColumn
            {
                Height = math.max((int)math.floor(height), 1),
                Biome = SurfaceBiome(climate, x, z),
                OreRichness = oreRichness,
                Mesa = mesa,
            };
            column.TreeHeight = TreeHeightAt(x, z, column);
            return column;
        }

        /// <summary>
        /// The biome whose blocks cover column (<paramref name="x"/>, <paramref name="z"/>), the same as
        /// <see cref="SampleColumn"/>'s <see cref="TerrainColumn.Biome"/>, but without working out the ground height.
        /// Decorations (Roadmap 2.6) use it to pick their plants.
        /// </summary>
        public int SurfaceBiome(int x, int z) => SurfaceBiome(Climate(new float2(x, z)), x, z);

        int SurfaceBiome(float2 climate, int x, int z)
        {
            // Nudge the climate a little per column so the line between two biomes' surface blocks is ragged, not straight.
            uint h = math.hash(new uint3((uint)x, (uint)z, _jitterHash));
            float2 jitter = new float2(h & 0xFFFF, h >> 16) * (1f / 65535f) - 0.5f;
            return NearestBiome(climate + jitter * _params.SurfaceJitter);
        }

        /// <summary>
        /// How tall the tree growing out of this column is, or 0. A tree needs its cell's tree spot, a lucky roll for
        /// its biome, the biome's own surface block on top (not snow above the snow line), and ground a cave hasn't eaten.
        /// </summary>
        int TreeHeightAt(int x, int z, in TerrainColumn column)
        {
            if (!TreePlacement.IsCandidate(_treeHash, x, z, _params.TreeCellSize))
                return 0;

            BiomeInfo biome = _biomes[column.Biome];
            if (TreePlacement.Roll(_treeHash, x, z) >= biome.TreeChance)
                return 0;
            if (SurfaceBlock(column) != biome.SurfaceBlock || IsCave(new int3(x, column.Height, z), column.Height))
                return 0;
            return TreePlacement.TrunkHeight(_treeHash, x, z, _params.TreeMinHeight, _params.TreeMaxHeight);
        }

        /// <summary>The block on top of <paramref name="column"/>: snow, a mesa cliff's band, or its biome's surface. The map paints it (4.8).</summary>
        public ushort SurfaceBlock(in TerrainColumn column)
        {
            if (column.Height >= _params.SnowLine)
                return _params.SnowBlock;
            BiomeInfo biome = _biomes[column.Biome];
            if (biome.MesaHeight > 0f && column.Mesa >= MesaRockFrom && column.Mesa < MesaCapFrom)
                return MesaBand(column.Height, biome);
            return biome.SurfaceBlock;
        }

        /// <summary>The block at world position <paramref name="w"/>, inside the column <paramref name="column"/>.</summary>
        public ushort BlockAt(int3 w, in TerrainColumn column)
        {
            if (w.y > column.Height)
                return w.y <= column.Height + column.TreeHeight ? _params.TreeTrunkBlock : BlockIds.Air;
            if (IsCave(w, column.Height))
                return BlockIds.Air;

            BiomeInfo biome = _biomes[column.Biome];
            int depth = column.Height - w.y;
            if (depth == 0)
                return SurfaceBlock(column);
            // A mesa (Roadmap 4.7): banded rock from its grass cap down to the plain it rose from.
            if (biome.MesaHeight > 0f && column.Mesa >= MesaRockFrom &&
                depth <= (int)(column.Mesa * biome.MesaHeight) + biome.SubsurfaceDepth)
                return MesaBand(w.y, biome);
            if (depth <= biome.SubsurfaceDepth)
                return biome.SubsurfaceBlock;
            return IsOre(w, column.OreRichness) ? _params.OreBlock : _params.StoneBlock;
        }

        /// <summary>Which of a mesa's two rocks is at height <paramref name="y"/>: level bands, the same all round the mesa.</summary>
        static ushort MesaBand(int y, in BiomeInfo biome)
        {
            int band = (int)math.floor((float)y / biome.MesaBandHeight);
            return (band & 1) == 0 ? biome.MesaBlock : biome.MesaBandBlock;
        }

        /// <summary>
        /// How much a biome's mesa lifts the ground here, 0-1: 0 off the mesa, 1 on its flat top, and a short steep climb
        /// between them (<see cref="BiomeInfo.MesaEdge"/>).
        /// </summary>
        float MesaShape(int index, float2 position)
        {
            BiomeInfo biome = _biomes[index];
            float n = noise.snoise(position * biome.MesaFrequency + _mesaOffsets[index]);
            return math.smoothstep(biome.MesaThreshold, biome.MesaThreshold + biome.MesaEdge, n);
        }

        float BiomeHeight(int index, float2 position)
        {
            BiomeInfo biome = _biomes[index];
            float2 p = position * biome.Frequency + _biomeOffsets[index];
            float n = biome.Ridged ? RidgedFbm(p, biome.Octaves, biome.Roughness) : Fbm(p, biome.Octaves, biome.Roughness);
            float height = biome.BaseHeight + biome.HeightAmplitude * n;
            return biome.TerraceStep > 0f ? Terrace(height, biome.TerraceStep) : height;
        }

        bool IsCave(int3 w, int surface)
        {
            if (w.y < _params.CaveFloorY)
                return false;

            float3 p = w;
            float depth = surface - w.y;

            // Winding "spaghetti" tunnels run where two noise fields both cross zero.
            // Squashing y makes them run more sideways than up and down.
            float entrance = math.saturate(depth / _params.CaveEntranceDepth);
            float width = _params.TunnelWidth * (0.5f + 0.5f * entrance);
            float3 t = p * _params.TunnelFrequency * new float3(1f, 1.5f, 1f);
            if (math.abs(noise.snoise(t + _tunnelAOffset)) < width && math.abs(noise.snoise(t + _tunnelBOffset)) < width)
                return true;

            // Big open caverns, deep down only. They thin out near their top height, so they get domed roofs.
            if (w.y >= _params.CavernMaxY || depth < _params.CaveEntranceDepth)
                return false;
            float roof = math.saturate((w.y - (_params.CavernMaxY - 16f)) / 16f);
            float threshold = _params.CavernThreshold + roof * (1f - _params.CavernThreshold);
            return noise.snoise(p * _params.CavernFrequency * new float3(1f, 2f, 1f) + _cavernOffset) > threshold;
        }

        bool IsOre(int3 w, float richness)
        {
            if (w.y > _params.OreMaxY)
                return false;
            return noise.snoise((float3)w * _params.OreFrequency + _oreOffset) * richness > _params.OreThreshold;
        }

        /// <summary>Layered simplex noise (fractal Brownian motion), roughly -1..1.</summary>
        static float Fbm(float2 p, int octaves, float roughness)
        {
            float sum = 0f;
            float amplitude = 1f;
            float total = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amplitude * noise.snoise(p);
                total += amplitude;
                amplitude *= roughness;
                p *= 2f;
            }
            return sum / total;
        }

        /// <summary>Like <see cref="Fbm"/> but with sharp crests, for dunes and jagged peaks. Roughly -1..1.</summary>
        static float RidgedFbm(float2 p, int octaves, float roughness)
        {
            float sum = 0f;
            float amplitude = 1f;
            float total = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float ridge = 1f - math.abs(noise.snoise(p));
                sum += amplitude * ridge * ridge;
                total += amplitude;
                amplitude *= roughness;
                p *= 2f;
            }
            return sum / total * 2f - 1f;
        }

        /// <summary>Flattens heights into steps with steep risers between them: cliffs and plateaus.</summary>
        static float Terrace(float height, float step)
        {
            float s = height / step;
            return (math.floor(s) + math.smoothstep(0.3f, 0.7f, math.frac(s))) * step;
        }

        public void Dispose()
        {
            if (_biomeOffsets.IsCreated)
                _biomeOffsets.Dispose();
            if (_mesaOffsets.IsCreated)
                _mesaOffsets.Dispose();
            if (_weights.IsCreated)
                _weights.Dispose();
        }
    }
}
