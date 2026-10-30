using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// What's in every Storage Chest (Roadmap 4.5, D-028), by the chest block's position. <b>Host only</b> (CLAUDE.md
    /// Law #2): heroes open a chest and move stacks through their <see cref="PlayerChest"/>, which asks the host; this
    /// keeps the slots, tells everyone looking into a chest when it changes, saves with the world
    /// (<see cref="WorldSaveManager"/>) and spills a broken chest's contents as loot bags. A chest gets its slots the
    /// first time someone opens it; empty chests aren't saved.
    /// <b>Not a singleton:</b> systems that need it get a reference (CLAUDE.md Law #1).
    /// </summary>
    public sealed class StorageChests : MonoBehaviour
    {
        public const int Slots = 27;

        [SerializeField] NetworkManager networkManager;

        [SerializeField] VoxelWorldManager world;

        [Tooltip("Who might be looking into a chest.")]
        [SerializeField] PlayerRegistry players;

        [Tooltip("Where a broken chest's contents go.")]
        [SerializeField] LootBagSpawner loot;

        [Tooltip("Data/Items/ItemRegistry.asset: stack sizes.")]
        [SerializeField] ItemRegistry items;

        readonly Dictionary<int3, ItemStacks> _chests = new Dictionary<int3, ItemStacks>();
        readonly List<ItemStack> _spill = new List<ItemStack>();

        bool IsServer => networkManager == null || networkManager.IsServer || !networkManager.IsListening;

        /// <summary>Host only: how many chests have slots (opened at least once, or loaded with something in them).</summary>
        public int Count => _chests.Count;

        void OnEnable()
        {
            if (world != null)
                world.EditApplied += OnEdit;
        }

        void OnDisable()
        {
            if (world != null)
                world.EditApplied -= OnEdit;
        }

        /// <summary>Host only: is there a Storage Chest at <paramref name="block"/> right now?</summary>
        public bool ServerIsChest(int3 block)
        {
            VoxelWorld blocks = world != null ? world.World : null;
            return blocks != null && blocks.TryGetChunk(VoxelMath.WorldToChunk(block), out _) &&
                   blocks.GetVoxel(block).BlockId == BlockIds.Chest;
        }

        /// <summary>Host only: the chest's slots, made empty the first time. Null if no chest stands there.</summary>
        internal ItemStacks ServerSlots(int3 block)
        {
            if (!IsServer || !ServerIsChest(block))
                return null;
            if (!_chests.TryGetValue(block, out ItemStacks slots))
                _chests[block] = slots = new ItemStacks(Slots, items != null ? items : ItemRules.Default);
            return slots;
        }

        /// <summary>Host only: slots of the chest at <paramref name="block"/> changed; show everyone who has it open.</summary>
        internal void ServerChanged(int3 block, IReadOnlyList<int> changed)
        {
            if (players == null || !_chests.TryGetValue(block, out ItemStacks slots))
                return;
            foreach (PlayerController player in players.Players)
            {
                if (player != null && player.TryGetComponent(out PlayerChest chest) && chest.ServerIsLookingInto(block))
                    chest.ServerShow(slots, changed);
            }
        }

        /// <summary>Host only, at load (Roadmap 3.4): every saved chest's slots, in place.</summary>
        public void ServerRestore(Dictionary<int3, List<ItemStack>> saved)
        {
            _chests.Clear();
            if (saved == null)
                return;
            IItemRules rules = items != null ? items : ItemRules.Default;
            foreach (KeyValuePair<int3, List<ItemStack>> chest in saved)
            {
                var slots = new ItemStacks(Slots, rules);
                List<ItemStack> lost = slots.Restore(chest.Value, Slots);
                if (lost.Count > 0)
                    Debug.LogWarning($"[Voxelforge] {lost.Count} saved stacks didn't fit in the chest at {chest.Key}.", this);
                _chests[chest.Key] = slots;
            }
        }

        /// <summary>Host only: a copy of every chest with something in it, for saving.</summary>
        public Dictionary<int3, List<ItemStack>> ServerSnapshot()
        {
            var saved = new Dictionary<int3, List<ItemStack>>();
            foreach (KeyValuePair<int3, ItemStacks> chest in _chests)
            {
                if (chest.Value.UsedSlots > 0)
                    saved[chest.Key] = new List<ItemStack>(chest.Value.Stacks);
            }
            return saved;
        }

        // Whatever broke a chest (mining, or anything else that changes the block), its contents fly out as loot bags.
        void OnEdit(VoxelEdit edit)
        {
            if (edit.Before.BlockId != BlockIds.Chest || edit.After.BlockId == BlockIds.Chest)
                return;
            if (!_chests.TryGetValue(edit.Coord, out ItemStacks slots))
                return;
            _chests.Remove(edit.Coord);

            if (players != null)
            {
                foreach (PlayerController player in players.Players)
                {
                    if (player != null && player.TryGetComponent(out PlayerChest chest) && chest.ServerIsLookingInto(edit.Coord))
                        chest.ServerClose();
                }
            }

            _spill.Clear();
            foreach (ItemStack stack in slots.Stacks)
            {
                if (!stack.IsEmpty)
                    _spill.Add(stack);
            }
            if (_spill.Count == 0)
                return;
            if (loot != null)
                loot.ServerSpawn((float3)edit.Coord + new float3(0.5f, 0.6f, 0.5f), _spill, new float3(0f, 3f, 0f));
            else
                Debug.LogError($"[Voxelforge] A chest broke at {edit.Coord} with nowhere to spill {_spill.Count} stacks.", this);
        }
    }
}
