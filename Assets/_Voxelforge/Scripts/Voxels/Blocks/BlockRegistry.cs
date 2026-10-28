using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// The list of every block type in the game. <see cref="CreateLookup"/> turns it into a
    /// <c>NativeArray&lt;BlockInfo&gt;</c> that jobs index by block ID.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Block Registry", fileName = "BlockRegistry")]
    public sealed class BlockRegistry : ScriptableObject
    {
        [SerializeField] List<BlockDefinition> blocks = new List<BlockDefinition>();

        public IReadOnlyList<BlockDefinition> Blocks => blocks;

        /// <summary>Returns every problem with the registry. An empty list means it's valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            var seen = new Dictionary<ushort, BlockDefinition>();

            for (int i = 0; i < blocks.Count; i++)
            {
                BlockDefinition block = blocks[i];
                if (block == null)
                {
                    errors.Add($"Entry {i} is empty.");
                    continue;
                }

                if (seen.TryGetValue(block.Id, out BlockDefinition existing))
                    errors.Add($"'{block.DisplayName}' and '{existing.DisplayName}' both use ID {block.Id}.");
                else
                    seen.Add(block.Id, block);
            }

            if (!seen.TryGetValue(BlockIds.Air, out BlockDefinition air))
                errors.Add($"No block uses ID {BlockIds.Air}. Air must be ID {BlockIds.Air}.");
            else if (air.IsSolid)
                errors.Add($"'{air.DisplayName}' uses the Air ID ({BlockIds.Air}) but is marked solid.");

            return errors;
        }

        /// <summary>
        /// Builds a lookup indexed by block ID. IDs with no definition are all zeros
        /// (<see cref="BlockInfo.IsRegistered"/> is false). The caller owns the array and must Dispose it.
        /// </summary>
        /// <exception cref="InvalidOperationException">The registry fails <see cref="Validate"/>.</exception>
        public NativeArray<BlockInfo> CreateLookup(Allocator allocator)
        {
            List<string> errors = Validate();
            if (errors.Count > 0)
                throw new InvalidOperationException($"Block registry '{name}' is invalid:\n{string.Join("\n", errors)}");

            int maxId = 0;
            foreach (BlockDefinition block in blocks)
                maxId = Math.Max(maxId, block.Id);

            List<BlockTexture> layers = GetTextureLayers();
            var lookup = new NativeArray<BlockInfo>(maxId + 1, allocator);
            foreach (BlockDefinition block in blocks)
            {
                BlockInfo info = block.ToBlockInfo();
                info.TopLayer = LayerOf(layers, block.TopTexture);
                info.SideLayer = LayerOf(layers, block.SideTexture);
                info.BottomLayer = LayerOf(layers, block.BottomTexture);
                lookup[block.Id] = info;
            }
            return lookup;
        }

        /// <summary>
        /// Every texture the blocks use, each once, in a fixed order (blocks in list order; top, side, bottom).
        /// Its index is the texture array layer, so the array builder and <see cref="CreateLookup"/> always agree.
        /// </summary>
        public List<BlockTexture> GetTextureLayers()
        {
            var layers = new List<BlockTexture>();
            foreach (BlockDefinition block in blocks)
            {
                if (block == null)
                    continue;
                foreach (BlockTexture texture in new[] { block.TopTexture, block.SideTexture, block.BottomTexture })
                {
                    if (texture != null && !layers.Contains(texture))
                        layers.Add(texture);
                }
            }
            return layers;
        }

        static ushort LayerOf(List<BlockTexture> layers, BlockTexture texture)
        {
            int index = texture != null ? layers.IndexOf(texture) : -1;
            return index >= 0 ? (ushort)index : BlockInfo.NoTexture;
        }

        public bool TryGet(ushort id, out BlockDefinition definition)
        {
            foreach (BlockDefinition block in blocks)
            {
                if (block != null && block.Id == id)
                {
                    definition = block;
                    return true;
                }
            }

            definition = null;
            return false;
        }

        /// <summary>Builds a registry in code, for tests and tools.</summary>
        internal static BlockRegistry Create(IEnumerable<BlockDefinition> definitions)
        {
            var registry = CreateInstance<BlockRegistry>();
            registry.blocks = new List<BlockDefinition>(definitions);
            return registry;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            foreach (string error in Validate())
                Debug.LogWarning($"[BlockRegistry] {error}", this);
        }
#endif
    }
}
