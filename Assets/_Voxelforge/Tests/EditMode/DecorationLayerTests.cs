using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>
    /// Roadmap 2.6, end to end with the project's real assets: decorations appear when chunks mesh, follow block
    /// changes on the next remesh, and go away when their chunk unloads.
    /// </summary>
    public class DecorationLayerTests
    {
        const string TerrainAssetPath = "Assets/_Voxelforge/Data/Terrain/TerrainSettings.asset";
        const string RegistryAssetPath = "Assets/_Voxelforge/Data/Blocks/BlockRegistry.asset";
        const string DecorationSetPath = "Assets/_Voxelforge/Data/Decorations/DecorationSet.asset";

        static readonly WorldSeed TestSeed = new WorldSeed(20261027);
        static readonly ChunkCoord Center = new ChunkCoord(8, 2, 8); // Meadow near the default spawn, with trees.

        VoxelWorld _world;
        ChunkGenerationScheduler _generator;
        ChunkMeshScheduler _meshes;
        DecorationLayer _layer;
        GameObject _parent;

        [SetUp]
        public void SetUp()
        {
            var terrain = AssetDatabase.LoadAssetAtPath<TerrainSettings>(TerrainAssetPath);
            var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(RegistryAssetPath);
            var set = AssetDatabase.LoadAssetAtPath<DecorationSet>(DecorationSetPath);
            Assert.IsNotNull(set, $"Missing {DecorationSetPath}");
            CollectionAssert.IsEmpty(set.Validate(terrain, registry));

            _world = new VoxelWorld(registry.CreateLookup(Allocator.Persistent));
            _generator = new ChunkGenerationScheduler(_world, TestSeed, terrain.ToTerrainParams(),
                terrain.CreateBiomeLookup(Allocator.Persistent));
            _parent = new GameObject("Decoration test chunks");
            _meshes = new ChunkMeshScheduler(_world, _parent.transform, null);
            _layer = new DecorationLayer(set, terrain, registry, TestSeed, _generator.Terrain, _generator.Biomes);
            _meshes.Decorations = _layer;

            for (int y = 0; y < 8; y++)
            for (int z = -1; z <= 1; z++)
            for (int x = -1; x <= 1; x++)
                _generator.Request(new ChunkCoord(Center.X + x, y, Center.Z + z));
            _generator.CompleteAll();
            _meshes.CompleteAll();
        }

        [TearDown]
        public void TearDown()
        {
            _meshes.Dispose();
            _layer.Dispose();
            _generator.Dispose();
            _world.Dispose();
            Object.DestroyImmediate(_parent);
        }

        [Test]
        public void MeshedMeadow_HasPlantsAndTrees()
        {
            Assert.Greater(_layer.InstanceCount, 500);
            int trees = 0;
            foreach (DecorationLayer.TypeData type in _layer.Types)
            {
                if (type.Definition.Kind == DecorationKind.Tree)
                    trees += type.Allocator.UsedCount;
            }
            Assert.Greater(trees, 0, "Expected trees on the tree trunks in this meadow.");
        }

        [Test]
        public void RemovingTheBlockUnderAPlant_RemovesThePlant_OnRemesh()
        {
            Assert.IsTrue(TryFindGroundPlant(out int3 support), "No ground plant found in the test chunk.");
            int before = _layer.InstanceCount;

            _meshes.CompleteGatherJobs();
            Assert.IsTrue(_world.SetVoxel(support, Voxel.Air));
            _meshes.CompleteAll();

            Assert.AreEqual(before - 1, _layer.InstanceCount, "Exactly that plant should be gone.");
        }

        [Test]
        public void UnloadingAChunk_RemovesItsDecorations()
        {
            int inCenter = _layer.CountIn(Center);
            Assert.Greater(inCenter, 0);
            int before = _layer.InstanceCount;

            _meshes.ReleaseRenderer(Center);

            Assert.AreEqual(0, _layer.CountIn(Center));
            Assert.AreEqual(before - inCenter, _layer.InstanceCount);
        }

        /// <summary>Finds a grass tuft or flower in <see cref="Center"/> whose block has dirt (not a plant surface) under it.</summary>
        bool TryFindGroundPlant(out int3 support)
        {
            foreach (DecorationLayer.TypeData type in _layer.Types)
            {
                if (type.Definition.Kind != DecorationKind.Ground)
                    continue;
                for (int i = 0; i < type.Allocator.UsedEnd; i++)
                {
                    DecorationInstance instance = type.Instances[i];
                    if (instance.Scale <= 0f)
                        continue;
                    support = (int3)math.floor(instance.Position) - new int3(0, 1, 0);
                    if (VoxelMath.WorldToChunk(support).Equals(Center) &&
                        _world.GetVoxel(support - new int3(0, 1, 0)).BlockId == BlockIds.Dirt)
                        return true;
                }
            }
            support = default;
            return false;
        }
    }
}
