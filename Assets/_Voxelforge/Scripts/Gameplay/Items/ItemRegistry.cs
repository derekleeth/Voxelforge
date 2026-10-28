using System.Collections.Generic;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Every <see cref="ItemDefinition"/> in the game (Roadmap 3.5). Blocks are items too and need no entry here,
    /// unless they want a model when held or dropped (prop blocks such as torches).
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Item Registry", fileName = "ItemRegistry")]
    public sealed class ItemRegistry : ScriptableObject
    {
        [SerializeField] List<ItemDefinition> items = new List<ItemDefinition>();

        Dictionary<ushort, ItemDefinition> _byId;

        public IReadOnlyList<ItemDefinition> Items => items;

        public bool TryGet(ushort id, out ItemDefinition item)
        {
            if (_byId == null)
            {
                _byId = new Dictionary<ushort, ItemDefinition>();
                foreach (ItemDefinition definition in items)
                {
                    if (definition != null)
                        _byId.TryAdd(definition.Id, definition);
                }
            }
            return _byId.TryGetValue(id, out item);
        }

        /// <summary>What the item multiplies mining power by while held. Bare hands for blocks and unknown IDs.</summary>
        public ToolPower ToolPowerOf(ushort id) => TryGet(id, out ItemDefinition item) ? item.ToolPower : ToolPower.Hands;

        /// <summary>The item's name: its definition's, else its block's, else "Item 1234".</summary>
        public string DisplayName(ushort id, BlockRegistry blocks)
        {
            if (TryGet(id, out ItemDefinition item))
                return item.DisplayName;
            if (blocks != null && ItemIds.IsBlock(id) && blocks.TryGet(id, out BlockDefinition block))
                return block.DisplayName;
            return $"Item {id}";
        }

        /// <summary>Returns every problem with the registry. An empty list means it's valid.</summary>
        public List<string> Validate(BlockRegistry blocks)
        {
            var errors = new List<string>();
            var seen = new HashSet<ushort>();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDefinition item = items[i];
                if (item == null)
                {
                    errors.Add($"Entry {i} is empty.");
                    continue;
                }
                if (!seen.Add(item.Id))
                    errors.Add($"'{item.DisplayName}' uses ID {item.Id}, which another item already uses.");
                if (ItemIds.IsBlock(item.Id) && blocks != null && !blocks.TryGet(item.Id, out _))
                    errors.Add($"'{item.DisplayName}' uses ID {item.Id}, below {ItemIds.FirstNonBlock}, but no block has it.");
                if (!ItemIds.IsBlock(item.Id) && (item.Mesh == null || item.Material == null))
                    errors.Add($"'{item.DisplayName}' needs a mesh and a material.");
            }
            return errors;
        }

        void OnValidate() => _byId = null;

        /// <summary>Builds a registry in code, for tests and tools.</summary>
        internal static ItemRegistry Create(IEnumerable<ItemDefinition> definitions)
        {
            var registry = CreateInstance<ItemRegistry>();
            registry.items = new List<ItemDefinition>(definitions);
            return registry;
        }
    }
}
