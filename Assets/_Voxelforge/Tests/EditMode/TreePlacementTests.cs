using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 2.6: trees grow from the seed, one spot per cell, never touching, on real ground.</summary>
    public class TreePlacementTests
    {
        const string TerrainAssetPath = "Assets/_Voxelforge/Data/Terrain/TerrainSettings.asset";
        const int CellSize = 6;

        static readonly WorldSeed TestSeed = new WorldSeed(20261027);

        [Test]
        public void EveryCell_HasExactlyOneCandidate_IncludingNegativeCells()
        {
            uint seedHash = TreePlacement.SeedHash(TestSeed);
            for (int cellZ = -4; cellZ < 4; cellZ++)
            for (int cellX = -4; cellX < 4; cellX++)
            {
                int count = 0;
                for (int dz = 0; dz < CellSize; dz++)
                for (int dx = 0; dx < CellSize; dx++)
                {
                    if (TreePlacement.IsCandidate(seedHash, cellX * CellSize + dx, cellZ * CellSize + dz, CellSize))
                        count++;
                }
                Assert.AreEqual(1, count, $"Cell ({cellX}, {cellZ}).");
            }
        }

        [Test]
        public void Candidates_NeverTouch()
        {
            uint seedHash = TreePlacement.SeedHash(TestSeed);
            for (int z = -60; z < 60; z++)
            for (int x = -60; x < 60; x++)
            {
                if (!TreePlacement.IsCandidate(seedHash, x, z, CellSize))
                    continue;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if ((dx != 0 || dz != 0) && TreePlacement.IsCandidate(seedHash, x + dx, z + dz, CellSize))
                        Assert.Fail($"Tree spots ({x}, {z}) and ({x + dx}, {z + dz}) touch.");
                }
            }
        }

        [Test]
        public void TrunkHeight_StaysInRange_AndIsRepeatable()
        {
            uint seedHash = TreePlacement.SeedHash(TestSeed);
            for (int i = -200; i < 200; i++)
            {
                int height = TreePlacement.TrunkHeight(seedHash, i, i * 7, 4, 6);
                Assert.That(height, Is.InRange(4, 6));
                Assert.AreEqual(height, TreePlacement.TrunkHeight(seedHash, i, i * 7, 4, 6));
            }
        }

        [Test]
        public void Terrain_GrowsTrunks_OnTheBiomeSurface_WithAirAbove()
        {
            var settings = AssetDatabase.LoadAssetAtPath<TerrainSettings>(TerrainAssetPath);
            using NativeArray<BiomeInfo> biomes = settings.CreateBiomeLookup(Allocator.Persistent);
            TerrainParams terrain = settings.ToTerrainParams();
            using var sampler = new TerrainSampler(TestSeed, terrain, biomes, Allocator.Temp);

            int trees = 0;
            for (int z = 0; z < 512; z++)
            for (int x = 0; x < 512; x++)
            {
                TerrainColumn column = sampler.SampleColumn(x, z);
                if (column.TreeHeight == 0)
                    continue;

                trees++;
                Assert.That(column.TreeHeight, Is.InRange(terrain.TreeMinHeight, terrain.TreeMaxHeight));
                Assert.AreEqual(biomes[column.Biome].SurfaceBlock, sampler.BlockAt(new int3(x, column.Height, z), column),
                    $"Tree at ({x}, {z}) isn't standing on its biome's surface block.");
                for (int i = 1; i <= column.TreeHeight; i++)
                    Assert.AreEqual(BlockIds.TreeTrunk, sampler.BlockAt(new int3(x, column.Height + i, z), column));
                Assert.AreEqual(BlockIds.Air, sampler.BlockAt(new int3(x, column.Height + column.TreeHeight + 1, z), column));
            }
            Assert.Greater(trees, 100, "Expected forests somewhere in a 512 x 512 area.");
        }
    }
}
