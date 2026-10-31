using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 4.7: the Thunderhoof Plains' flat-topped, banded mesas.</summary>
    public class MesaTests
    {
        const string TerrainAssetPath = "Assets/_Voxelforge/Data/Terrain/TerrainSettings.asset";
        static readonly WorldSeed Seed = new WorldSeed(20261031);

        /// <summary>A flat plain at y 70 that's all one biome, with mesas <paramref name="mesaHeight"/> tall (0 = none).</summary>
        static NativeArray<BiomeInfo> Plain(float mesaHeight)
        {
            var biomes = new NativeArray<BiomeInfo>(1, Allocator.Temp);
            biomes[0] = new BiomeInfo
            {
                BaseHeight = 70f, HeightAmplitude = 0f, Frequency = 0.01f, Octaves = 1, Roughness = 0.5f,
                SurfaceBlock = BlockIds.Grass, SubsurfaceBlock = BlockIds.Dirt, SubsurfaceDepth = 3, OreRichness = 0f,
                MesaHeight = mesaHeight, MesaFrequency = 0.02f, MesaThreshold = 0.4f, MesaEdge = 0.05f,
                MesaBlock = BlockIds.RedRock, MesaBandBlock = BlockIds.Sandstone, MesaBandHeight = 3,
                GrassTint = 1f, FoliageTint = 1f,
            };
            return biomes;
        }

        static TerrainParams Params()
        {
            TerrainParams terrain = AssetDatabase.LoadAssetAtPath<TerrainSettings>(TerrainAssetPath).ToTerrainParams();
            terrain.TreeCellSize = 1000; // Trees would only get in the way.
            terrain.TunnelWidth = 0f;
            terrain.CavernThreshold = 1f;
            terrain.SnowLine = 1000;
            return terrain;
        }

        /// <summary>A column on top of a mesa, found by searching outward from the origin.</summary>
        static bool FindMesaTop(in TerrainSampler sampler, out int2 at, out TerrainColumn column)
        {
            for (int z = 0; z < 400; z += 3)
            for (int x = 0; x < 400; x += 3)
            {
                column = sampler.SampleColumn(x, z);
                if (column.Mesa >= 1f)
                {
                    at = new int2(x, z);
                    return true;
                }
            }
            at = default;
            column = default;
            return false;
        }

        [Test]
        public void NoMesas_LeavesThePlainFlat()
        {
            using NativeArray<BiomeInfo> biomes = Plain(0f);
            using var sampler = new TerrainSampler(Seed, Params(), biomes, Allocator.Temp);
            for (int z = 0; z < 200; z += 7)
            for (int x = 0; x < 200; x += 7)
            {
                TerrainColumn column = sampler.SampleColumn(x, z);
                Assert.AreEqual(0f, column.Mesa);
                Assert.AreEqual(70, column.Height);
            }
        }

        [Test]
        public void MesaTop_IsFlat_AndRaisedByItsHeight()
        {
            using NativeArray<BiomeInfo> biomes = Plain(14f);
            using var sampler = new TerrainSampler(Seed, Params(), biomes, Allocator.Temp);
            Assert.IsTrue(FindMesaTop(sampler, out int2 at, out TerrainColumn top), "No mesa in 400 x 400 blocks.");
            Assert.AreEqual(84, top.Height);
            Assert.AreEqual(BlockIds.Grass, sampler.BlockAt(new int3(at.x, top.Height, at.y), top), "A grass cap.");
        }

        [Test]
        public void MesaCliff_IsBandedRock_DownToThePlain()
        {
            using NativeArray<BiomeInfo> biomes = Plain(14f);
            using var sampler = new TerrainSampler(Seed, Params(), biomes, Allocator.Temp);
            Assert.IsTrue(FindMesaTop(sampler, out int2 at, out TerrainColumn top));
            for (int y = top.Height - 1; y >= 71; y--)
            {
                ushort block = sampler.BlockAt(new int3(at.x, y, at.y), top);
                ushort expected = ((int)math.floor(y / 3f) & 1) == 0 ? BlockIds.RedRock : BlockIds.Sandstone;
                Assert.AreEqual(expected, block, $"Band at y {y}.");
            }
        }

        [Test]
        public void MesaCliffs_AreBareRock_OnlyTheTopIsGrass()
        {
            using NativeArray<BiomeInfo> biomes = Plain(14f);
            using var sampler = new TerrainSampler(Seed, Params(), biomes, Allocator.Temp);
            int cliffs = 0;
            for (int z = 0; z < 400; z += 2)
            for (int x = 0; x < 400; x += 2)
            {
                TerrainColumn column = sampler.SampleColumn(x, z);
                if (column.Mesa < 0.2f || column.Mesa > 0.8f)
                    continue;
                ushort top = sampler.BlockAt(new int3(x, column.Height, z), column);
                Assert.IsTrue(top == BlockIds.RedRock || top == BlockIds.Sandstone, $"Cliff top at ({x}, {z}) is block {top}.");
                cliffs++;
            }
            Assert.Greater(cliffs, 0, "No mesa cliffs in 400 x 400 blocks.");
        }

        [Test]
        public void ProjectPlains_HaveMesas()
        {
            var settings = AssetDatabase.LoadAssetAtPath<TerrainSettings>(TerrainAssetPath);
            BiomeSettings plains = null;
            foreach (BiomeSettings biome in settings.Biomes)
            {
                if (biome.Name == "Thunderhoof Plains")
                    plains = biome;
                else
                    Assert.IsFalse(biome.HasMesas, $"Only the Plains have mesas, not '{biome.Name}'.");
            }
            Assert.IsNotNull(plains, "No Thunderhoof Plains biome.");
            Assert.IsTrue(plains.HasMesas);
        }
    }
}
