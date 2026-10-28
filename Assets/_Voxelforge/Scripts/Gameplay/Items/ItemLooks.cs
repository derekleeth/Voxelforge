using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>How one item looks on its own: in a hand, lying on the ground, or as a ghost (Roadmap 3.5).</summary>
    public readonly struct ItemLook
    {
        public readonly Mesh Mesh;
        public readonly Material Material;
        /// <summary>A model from an <see cref="ItemDefinition"/> (origin at its grip or bottom), not a centred block cube.</summary>
        public readonly bool IsModel;
        /// <summary>The definition behind a model, or null for a plain block.</summary>
        public readonly ItemDefinition Definition;
        /// <summary>Scale that fits the mesh into a 1-unit box, so drops of any model come out the same size.</summary>
        public readonly float FitScale;
        /// <summary>The middle of the mesh, in its own space.</summary>
        public readonly Vector3 Center;

        public ItemLook(Mesh mesh, Material material, ItemDefinition definition)
        {
            Mesh = mesh;
            Material = material;
            Definition = definition;
            IsModel = definition != null;
            Bounds bounds = mesh.bounds;
            float largest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            FitScale = largest > 1e-4f ? 1f / largest : 1f;
            Center = bounds.center;
        }
    }

    /// <summary>
    /// Builds and caches <see cref="ItemLook"/>s (Roadmap 3.5). Items with an <see cref="ItemDefinition"/> use its model
    /// and material; other blocks get a <see cref="BlockItemMesh"/> cube with the block material, like 3.2 drops.
    /// Presentation only, on any PC. Owns the block meshes it makes.
    /// </summary>
    public sealed class ItemLooks : IDisposable
    {
        readonly ItemRegistry _items;
        readonly Material _blockMaterial;
        readonly Dictionary<ushort, ItemLook> _looks = new Dictionary<ushort, ItemLook>();
        readonly List<Mesh> _ownedMeshes = new List<Mesh>();

        public ItemLooks(ItemRegistry items, Material blockMaterial)
        {
            _items = items;
            _blockMaterial = blockMaterial;
        }

        /// <param name="blocks">For block cubes. Not created on a PC without a world (remote, Phase 6): blocks are skipped.</param>
        public bool TryGet(ushort itemId, NativeArray<BlockInfo> blocks, out ItemLook look)
        {
            if (_looks.TryGetValue(itemId, out look))
                return true;

            if (_items != null && _items.TryGet(itemId, out ItemDefinition item) && item.Mesh != null && item.Material != null)
            {
                look = new ItemLook(item.Mesh, item.Material, item);
            }
            else if (ItemIds.IsBlock(itemId) && itemId != BlockIds.Air && blocks.IsCreated && itemId < blocks.Length &&
                     _blockMaterial != null)
            {
                Mesh mesh = BlockItemMesh.Create(blocks, itemId);
                _ownedMeshes.Add(mesh);
                look = new ItemLook(mesh, _blockMaterial, null);
            }
            else
            {
                return false;
            }
            _looks.Add(itemId, look);
            return true;
        }

        public void Dispose()
        {
            foreach (Mesh mesh in _ownedMeshes)
                UnityEngine.Object.Destroy(mesh);
            _ownedMeshes.Clear();
            _looks.Clear();
        }
    }
}
