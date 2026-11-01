using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Core;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Personal loot (Roadmap 5.5, D-036), <b>host only</b> (CLAUDE.md Law #2). When a mob is defeated it rolls the mob's
    /// <see cref="LootTable"/> once for every hero who fought it (<see cref="LootShares"/>) and pops each hero's share out
    /// of the body as their own sack, in their colour, that only they can pick up. The first time each hero opens a
    /// ruin's Storage Chest (5.4) it rolls the ruin cache for them the same way, once per hero per chest, remembered in
    /// their profile. <b>Not a singleton:</b> it's handed its references (CLAUDE.md Law #1).
    /// </summary>
    public sealed class PersonalLoot : MonoBehaviour
    {
        [SerializeField] NetworkManager networkManager;

        [SerializeField] PlayerRegistry players;

        [SerializeField] StorageChests chests;

        [SerializeField] LootBagSpawner loot;

        [Tooltip("Data/Loot/LootBook.asset (built by Voxelforge > Build Loot).")]
        [SerializeField] LootBook book;

        [Tooltip("Data/Items/ItemRegistry.asset: item rarities, for how the sacks glow.")]
        [SerializeField] ItemRegistry items;

        [Tooltip("How fast a sack pops up out of a defeated mob or an opened chest, in metres per second.")]
        [SerializeField, Range(0f, 10f)] float popSpeed = 4f;

        readonly System.Random _random = new System.Random(System.Environment.TickCount);
        readonly List<PlayerId> _fighters = new List<PlayerId>();
        readonly List<ItemStack> _rolled = new List<ItemStack>();
        System.Func<PlayerId, bool> _isPresent;

        bool IsServer => networkManager == null || networkManager.IsServer || !networkManager.IsListening;

        void Awake() => _isPresent = id => Find(id) != null;

        void OnEnable()
        {
            Mob.ServerDefeated += OnMobDefeated;
            if (chests != null)
                chests.ServerOpened += OnChestOpened;
        }

        void OnDisable()
        {
            Mob.ServerDefeated -= OnMobDefeated;
            if (chests != null)
                chests.ServerOpened -= OnChestOpened;
        }

        void OnMobDefeated(Mob mob, PlayerId killer)
        {
            if (!IsServer || mob == null || book == null || mob.Definition == null ||
                !book.TryGetForMob(mob.Definition.Id, out LootTable table))
                return;
            LootShares.Fighters(mob.Rules != null ? mob.Rules.Threat.Entries : null, killer, _isPresent, _fighters);
            float3 middle = mob.Feet + new float3(0f, mob.HalfExtents.y, 0f);
            foreach (PlayerId fighter in _fighters)
                RollFor(Find(fighter), table, middle);
        }

        void OnChestOpened(PlayerController player, int3 block)
        {
            if (!IsServer || player == null || book == null || !player.TryGetComponent(out PlayerBlockBag bag) ||
                !LootShares.OwesRuinCache(chests.ServerIsGenerated(block), bag.ServerLootedCaches, block) ||
                !book.TryGetRuinCache(out LootTable table))
                return;
            bag.ServerLootedCaches.Add(block);
            // Out of the top of the chest, close enough that it flies straight into their bag.
            RollFor(player, table, (float3)block + new float3(0.5f, 1.2f, 0.5f));
        }

        void RollFor(PlayerController hero, LootTable table, float3 from)
        {
            if (hero == null || loot == null)
                return;
            LootRoller.Roll(table, _random, _rolled);
            if (_rolled.Count == 0)
                return;
            ItemRarity best = LootShares.Best(_rolled, items != null ? items : ItemRules.Default);
            loot.ServerSpawnFor(hero, from, _rolled, new float3(0f, popSpeed, 0f), best);
        }

        PlayerController Find(PlayerId id)
        {
            if (players == null)
                return null;
            foreach (PlayerController player in players.Players)
            {
                if (player != null && player.PlayerId == id)
                    return player;
            }
            return null;
        }
    }
}
