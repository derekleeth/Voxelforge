using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Core;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// A drawstring sack of items lying in the world (Roadmap 4.5, D-028): a stack someone dropped from their bag, what
    /// didn't fit when a partner sent them something, or what spilled from a broken Storage Chest. <b>The host</b> moves
    /// it like a block drop (<see cref="DropPhysics"/>) and <see cref="LootBagSpawner"/> hands its stacks to the first hero
    /// who walks over it. Every PC sees the sack sitting there, swaying a little; the stacks are synced so a later
    /// tooltip could show them.
    /// </summary>
    public sealed class LootBag : NetworkBehaviour
    {
        /// <summary>The most stacks one sack holds; more spill into more sacks.</summary>
        public const int MaxStacks = 8;

        [Tooltip("The sack model.")]
        [SerializeField] Transform visual;

        [Tooltip("Half its size as a box, for landing on blocks, in metres.")]
        [SerializeField, Range(0.05f, 0.5f)] float halfSize = 0.18f;

        [SerializeField, Min(0f)] float gravity = 25f;

        [Tooltip("Degrees per second it slowly turns, so it catches the eye.")]
        [SerializeField] float turnSpeed = 25f;

        readonly NetworkList<ItemStack> _stacks = new NetworkList<ItemStack>();

        // Host-only state.
        VoxelWorld _world;
        readonly List<ItemStack> _assigned = new List<ItemStack>();
        float3 _velocity;
        bool _pulled;
        PlayerId _notFor;
        float _phase;

        /// <summary>Raised on every PC when a sack spawns, so <see cref="LootBagSpawner"/> can keep track of it.</summary>
        public static event Action<LootBag> Spawned;

        public int StackCount => _stacks.Count;

        public ItemStack GetStack(int index) => _stacks[index];

        /// <summary>Host only: when it appeared, in game seconds.</summary>
        public float SpawnTime { get; private set; }

        /// <summary>Host only: lying still on the ground.</summary>
        public bool IsResting { get; private set; }

        public bool IsEmpty
        {
            get
            {
                for (int i = 0; i < _stacks.Count; i++)
                {
                    if (!_stacks[i].IsEmpty)
                        return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Host only, before spawning: what's in it and how it starts moving. <paramref name="notFor"/> (whoever dropped
        /// it) can't pick it up until they've walked away from it once (<see cref="ServerForgetDropper"/>), or it would fly
        /// straight back into their bag. Everyone else can take it at once.
        /// </summary>
        internal void ServerInitialize(VoxelWorld world, IEnumerable<ItemStack> stacks, float3 velocity, PlayerId notFor)
        {
            _world = world;
            _assigned.Clear();
            foreach (ItemStack stack in stacks)
            {
                if (!stack.IsEmpty && _assigned.Count < MaxStacks)
                    _assigned.Add(stack);
            }
            _velocity = velocity;
            _notFor = notFor;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                foreach (ItemStack stack in _assigned)
                    _stacks.Add(stack);
                SpawnTime = Time.time;
            }
            _phase = UnityEngine.Random.value * 360f;
            name = $"Loot Bag ({_stacks.Count} stacks)";
            Spawned?.Invoke(this);
        }

        /// <summary>Host only: may this player pick it up yet?</summary>
        internal bool ServerCanTake(PlayerId player) => !_notFor.IsValid || player != _notFor;

        /// <summary>Host only: whoever dropped it, until they've walked away from it; <see cref="PlayerId.None"/> after.</summary>
        internal PlayerId ServerDropper => _notFor;

        /// <summary>Host only: the dropper walked away, so from now on they can pick it up like anyone.</summary>
        internal void ServerForgetDropper() => _notFor = PlayerId.None;

        /// <summary>Host only: puts what fits into <paramref name="bag"/>, keeping the rest. Reports each stack given.</summary>
        internal void ServerGiveTo(PlayerBlockBag bag, List<ItemStack> given)
        {
            given.Clear();
            for (int i = _stacks.Count - 1; i >= 0; i--)
            {
                ItemStack stack = _stacks[i];
                int added = bag.ServerAdd(stack.ItemId, stack.Count);
                if (added <= 0)
                    continue;
                given.Add(new ItemStack(stack.ItemId, (ushort)added));
                if (added >= stack.Count)
                    _stacks.RemoveAt(i);
                else
                    _stacks[i] = new ItemStack(stack.ItemId, (ushort)(stack.Count - added));
            }
        }

        /// <summary>Host only: flies toward <paramref name="target"/> this step, as if sucked in.</summary>
        internal void ServerPull(float3 target, float speed)
        {
            _pulled = true;
            IsResting = false;
            float3 toTarget = target - (float3)transform.position;
            float distance = math.length(toTarget);
            _velocity = distance > 1e-4f ? toTarget / distance * speed : float3.zero;
        }

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || _world == null)
                return;

            float dt = Time.fixedDeltaTime;
            float3 position = transform.position;
            if (_pulled)
            {
                transform.position = position + _velocity * dt;
                _pulled = false; // The spawner pulls again next step while a hero is close.
                return;
            }
            IsResting = DropPhysics.Step(_world, ref position, ref _velocity, halfSize, gravity, dt);
            transform.position = position;
        }

        void Update()
        {
            if (visual == null)
                return;
            // The model's origin is its bottom, the body's is its middle: sit the sack on the ground and sway it a little.
            float t = Time.time;
            visual.localPosition = new Vector3(0f, -halfSize, 0f);
            visual.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.7f + _phase) * 4f, _phase + t * turnSpeed, 0f);
        }
    }
}
