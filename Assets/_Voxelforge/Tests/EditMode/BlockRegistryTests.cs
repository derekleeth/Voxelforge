using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Voxelforge.Voxels;
using Object = UnityEngine.Object;

namespace Voxelforge.Tests
{
    public class BlockRegistryTests
    {
        const string RegistryAssetPath = "Assets/_Voxelforge/Data/Blocks/BlockRegistry.asset";

        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in _created)
                Object.DestroyImmediate(obj);
            _created.Clear();
        }

        [Test]
        public void ProjectRegistry_IsValid_AndMatchesBlockIds()
        {
            var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(RegistryAssetPath);
            Assert.IsNotNull(registry, $"Missing {RegistryAssetPath}");

            CollectionAssert.IsEmpty(registry.Validate());

            using NativeArray<BlockInfo> lookup = registry.CreateLookup(Allocator.Temp);
            foreach (ushort id in new[] { BlockIds.Air, BlockIds.Grass, BlockIds.Dirt, BlockIds.Stone, BlockIds.ManaCrystalOre,
                         BlockIds.Sand, BlockIds.Snow, BlockIds.Sandstone, BlockIds.RedRock })
                Assert.IsTrue(lookup[id].IsRegistered, $"Block ID {id} has no definition in the registry.");

            Assert.IsFalse(lookup[BlockIds.Air].IsSolid);
            Assert.IsTrue(lookup[BlockIds.Stone].IsSolid);
            Assert.Greater(lookup[BlockIds.ManaCrystalOre].LightEmission, 0);
        }

        [Test]
        public void ProjectRegistry_EveryOpaqueBlockHasTextures_AndArraysMatch()
        {
            var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(RegistryAssetPath);
            List<BlockTexture> layers = registry.GetTextureLayers();
            using NativeArray<BlockInfo> lookup = registry.CreateLookup(Allocator.Temp);

            foreach (BlockInfo info in lookup)
            {
                if (!info.IsOpaque)
                    continue;
                foreach (ushort layer in new[] { info.TopLayer, info.SideLayer, info.BottomLayer })
                    Assert.Less(layer, layers.Count, $"Block {info.Id} is missing a texture.");
            }

            BlockInfo grass = lookup[BlockIds.Grass];
            Assert.AreNotEqual(grass.TopLayer, grass.SideLayer, "Grass needs a different texture on its sides.");
            Assert.AreEqual(lookup[BlockIds.Dirt].TopLayer, grass.BottomLayer, "The bottom of Grass is Dirt.");

            // The texture arrays must have been rebuilt since the last texture change (Voxelforge > Rebuild Block Textures).
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Voxelforge/Materials/VoxelBlocks.mat");
            Assert.IsNotNull(material);
            foreach (string property in new[] { "_AlbedoHeightArray", "_NormalArray", "_MaskArray" })
            {
                var array = material.GetTexture(property) as Texture2DArray;
                Assert.IsNotNull(array, $"{property} is not set on the block material.");
                Assert.AreEqual(layers.Count, array.depth, $"{property} is out of date. Run Voxelforge > Rebuild Block Textures.");
                Assert.AreEqual(128, array.width);
                Assert.AreEqual(8, array.mipmapCount);
            }
        }

        [Test]
        public void CreateLookup_IndexesByIdAndLeavesGapsUnregistered()
        {
            BlockRegistry registry = MakeRegistry(
                MakeBlock(BlockIds.Air, "Air", isSolid: false, isTransparent: true),
                MakeBlock(5, "Glowstone", lightEmission: 15));

            using NativeArray<BlockInfo> lookup = registry.CreateLookup(Allocator.Temp);

            Assert.AreEqual(6, lookup.Length);
            Assert.IsTrue(lookup[5].IsRegistered);
            Assert.AreEqual(15, lookup[5].LightEmission);
            Assert.IsTrue(lookup[0].IsTransparent);
            Assert.IsFalse(lookup[3].IsRegistered);
        }

        [Test]
        public void Validate_RejectsDuplicateIds()
        {
            BlockRegistry registry = MakeRegistry(
                MakeBlock(BlockIds.Air, "Air", isSolid: false),
                MakeBlock(7, "Copper"),
                MakeBlock(7, "Tin"));

            List<string> errors = registry.Validate();

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("ID 7", errors[0]);
            Assert.Throws<InvalidOperationException>(() => registry.CreateLookup(Allocator.Temp));
        }

        [Test]
        public void Validate_RequiresNonSolidAirAtIdZero()
        {
            Assert.IsNotEmpty(MakeRegistry(MakeBlock(BlockIds.Stone, "Stone")).Validate());
            Assert.IsNotEmpty(MakeRegistry(MakeBlock(BlockIds.Air, "Air", isSolid: true)).Validate());
        }

        [Test]
        public void TryGet_FindsDefinitionById()
        {
            BlockDefinition stone = MakeBlock(BlockIds.Stone, "Stone", maxDurability: 60);
            BlockRegistry registry = MakeRegistry(MakeBlock(BlockIds.Air, "Air", isSolid: false), stone);

            Assert.IsTrue(registry.TryGet(BlockIds.Stone, out BlockDefinition found));
            Assert.AreSame(stone, found);
            Assert.IsFalse(registry.TryGet(99, out _));
        }

        BlockDefinition MakeBlock(ushort id, string displayName, int maxDurability = 20,
            bool isSolid = true, bool isTransparent = false, int lightEmission = 0)
        {
            BlockDefinition block = BlockDefinition.Create(id, displayName, maxDurability, isSolid, isTransparent, lightEmission);
            _created.Add(block);
            return block;
        }

        BlockRegistry MakeRegistry(params BlockDefinition[] blocks)
        {
            BlockRegistry registry = BlockRegistry.Create(blocks);
            _created.Add(registry);
            return registry;
        }
    }
}
