using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Block drops (Roadmap 3.2, D-015). On the host it watches <see cref="VoxelWorldManager.EditApplied"/>: whenever a
    /// block is mined, chopped or blasted away, its <see cref="BlockInfo.DropId"/> pops out as a <see cref="BlockDrop"/>.
    /// Blocks broken in the same frame pop out together, one stack per drop and area (<see cref="DropBatcher"/>, Roadmap
    /// 4.3), so a crater doesn't spawn a network object per block. It
    /// then merges drops lying close together, pulls drops toward the nearest player and puts them in that player's
    /// <see cref="PlayerBlockBag"/>, and clears away drops nobody picks up. On every PC it gives each drop the mesh of
    /// its block, or its model for prop blocks such as torches (<see cref="ItemLooks"/>, Roadmap 3.5).
    /// </summary>
    public sealed class BlockDropSpawner : MonoBehaviour
    {
        [SerializeField] NetworkManager networkManager;

        [SerializeField] VoxelWorldManager world;

        [Tooltip("Who can pick drops up.")]
        [SerializeField] PlayerRegistry players;

        [SerializeField] BlockDrop dropPrefab;

        [Tooltip("Data/Items/ItemRegistry.asset: models for prop blocks such as torches.")]
        [SerializeField] ItemRegistry items;

        [Tooltip("Materials/VoxelBlocks.mat: for plain block drops.")]
        [SerializeField] Material blockMaterial;

        [Tooltip("Drops closer than this to a hero's middle fly toward them, in metres.")]
        [SerializeField, Range(0.5f, 5f)] float pullRadius = 2f;

        [Tooltip("Drops closer than this to a hero's middle are picked up, in metres.")]
        [SerializeField, Range(0.1f, 2f)] float pickupRadius = 0.6f;

        [Tooltip("How fast pulled drops fly, in metres per second.")]
        [SerializeField, Min(1f)] float pullSpeed = 8f;

        [Tooltip("Resting drops of the same block closer than this merge into one, in metres.")]
        [SerializeField, Range(0f, 2f)] float mergeRadius = 1f;

        [Tooltip("Drops nobody picks up disappear after this many seconds.")]
        [SerializeField, Min(10f)] float lifetimeSeconds = 300f;

        [Tooltip("How fast a new drop hops out of its block, in metres per second.")]
        [SerializeField, Range(0f, 10f)] float popSpeed = 3f;

        [Tooltip("Blocks broken in the same frame closer than about this share one stack, in metres (Roadmap 4.3).")]
        [SerializeField, Range(1f, 8f)] float stackArea = 3f;

        [Tooltip("Extra hop for a stack of many blocks (a crater), in metres per second.")]
        [SerializeField, Range(0f, 10f)] float blastPopSpeed = 3f;

        // How high above the feet a hero's middle is, where drops fly to.
        const float HeroMiddle = 0.8f;

        readonly List<BlockDrop> _drops = new List<BlockDrop>();
        readonly List<PendingDrop> _pending = new List<PendingDrop>();
        DropBatcher _batcher;
        ItemLooks _looks;

        /// <summary>Host only: drops lying in the world right now.</summary>
        public int DropCount => _drops.Count;

        /// <summary>Host only: a drop went into a bag here (block ID, how many), e.g. for a pop sound (Roadmap 3.9).</summary>
        public event System.Action<float3, ushort, int> PickedUp;

        bool IsServer => networkManager != null && networkManager.IsServer;

        void OnEnable()
        {
            if (world != null)
                world.EditApplied += OnEditApplied;
            BlockDrop.Spawned += OnDropSpawned;
        }

        void OnDisable()
        {
            if (world != null)
                world.EditApplied -= OnEditApplied;
            BlockDrop.Spawned -= OnDropSpawned;
        }

        void OnDestroy()
        {
            _looks?.Dispose();
        }

        void OnEditApplied(VoxelEdit edit)
        {
            if (!IsServer || !edit.After.IsAir || dropPrefab == null)
                return;
            BlockInfo block = world.World.Blocks[edit.Before.BlockId];
            if (block.DropCount == 0)
                return;
            _batcher ??= new DropBatcher(stackArea);
            _batcher.Add((float3)edit.Coord + 0.5f, block.DropId, block.DropCount);
        }

        void LateUpdate()
        {
            if (_batcher == null || _batcher.IsEmpty || !IsServer)
                return;
            _pending.Clear();
            _batcher.Flush(_pending);
            foreach (PendingDrop pending in _pending)
            {
                // A small hop in a random direction, so stacks spread out a little; a stack from a crater hops higher.
                float angle = UnityEngine.Random.value * math.PI * 2f;
                float up = popSpeed + (pending.Blocks > 1 ? blastPopSpeed * math.saturate(pending.Blocks / 16f) : 0f);
                var velocity = new float3(math.sin(angle) * 0.6f, up, math.cos(angle) * 0.6f);
                BlockDrop drop = Instantiate(dropPrefab, pending.Position, Quaternion.identity);
                drop.ServerInitialize(world.World, pending.BlockId, pending.Count, velocity);
                drop.NetworkObject.Spawn(destroyWithScene: true);
                _drops.Add(drop);
            }
        }

        void OnDropSpawned(BlockDrop drop)
        {
            if (world == null || world.World == null)
                return; // A remote PC can't see blocks yet (Phase 6).
            _looks ??= new ItemLooks(items, blockMaterial);
            if (_looks.TryGet(drop.BlockId, world.World.Blocks, out ItemLook look))
                drop.SetLook(look);
        }

        void FixedUpdate()
        {
            if (!IsServer)
                return;

            _drops.RemoveAll(drop => drop == null || !drop.IsSpawned);
            for (int i = _drops.Count - 1; i >= 0; i--)
            {
                BlockDrop drop = _drops[i];
                if (Time.time - drop.SpawnTime > lifetimeSeconds || TryPickUp(drop) || TryMerge(drop, i))
                    Despawn(i);
            }
        }

        /// <summary>Pulls the drop toward the nearest hero, and into their bag once close. True if it's all gone.</summary>
        bool TryPickUp(BlockDrop drop)
        {
            if (players == null)
                return false;

            float3 position = drop.transform.position;
            PlayerBlockBag nearestBag = null;
            float3 nearestMiddle = default;
            float nearest = pullRadius;
            foreach (PlayerController player in players.Players)
            {
                if (player == null || !player.HasLanded || !player.TryGetComponent(out PlayerBlockBag bag))
                    continue;
                float3 middle = (float3)player.transform.position + new float3(0f, HeroMiddle, 0f);
                float distance = math.distance(position, middle);
                // A full bag can't take this block, so the drop stays for someone else.
                if (distance < nearest && bag.CountOf(drop.BlockId) < ItemStacks.MaxPerItem)
                {
                    nearest = distance;
                    nearestBag = bag;
                    nearestMiddle = middle;
                }
            }

            if (nearestBag == null)
                return false;
            if (nearest > pickupRadius)
            {
                drop.ServerPull(nearestMiddle, pullSpeed);
                return false;
            }

            int added = nearestBag.ServerAdd(drop.BlockId, drop.Count);
            if (added > 0)
            {
                PickedUp?.Invoke(nearestMiddle, drop.BlockId, added);
                nearestBag.ServerTellPickedUp(drop.BlockId, added);
            }
            if (added >= drop.Count)
                return true;
            if (added > 0)
                drop.ServerSetCount(drop.Count - added);
            return false;
        }

        /// <summary>Folds a resting drop into an older resting drop of the same block nearby. True if it merged.</summary>
        bool TryMerge(BlockDrop drop, int index)
        {
            if (!drop.IsResting || mergeRadius <= 0f)
                return false;
            for (int i = 0; i < index; i++)
            {
                BlockDrop other = _drops[i];
                if (other.BlockId != drop.BlockId || !other.IsResting ||
                    math.distance(other.transform.position, drop.transform.position) > mergeRadius ||
                    other.Count + drop.Count > ushort.MaxValue)
                    continue;
                other.ServerSetCount(other.Count + drop.Count);
                return true;
            }
            return false;
        }

        void Despawn(int index)
        {
            BlockDrop drop = _drops[index];
            _drops.RemoveAt(index);
            drop.NetworkObject.Despawn(destroy: true);
        }
    }
}
