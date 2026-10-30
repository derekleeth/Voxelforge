using System;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// A small block lying in the world after a block is mined or chopped (Roadmap 3.2, D-015). <b>The host</b> moves
    /// it: a little hop, gravity, and stopping on solid blocks. A <c>NetworkTransform</c> carries the position to
    /// every PC. <see cref="BlockDropSpawner"/> spawns drops, merges them, pulls them toward nearby players and puts
    /// them in their bags. The spinning, bobbing mini block is presentation only, drawn on every PC.
    /// </summary>
    public sealed class BlockDrop : NetworkBehaviour
    {
        [Tooltip("The spinning mini block. Its MeshFilter gets the block's mesh.")]
        [SerializeField] MeshFilter visual;

        [Tooltip("Size of the mini block, in metres.")]
        [SerializeField, Range(0.1f, 0.6f)] float size = 0.25f;

        [SerializeField, Min(0f)] float gravity = 25f;

        [Tooltip("Degrees per second the mini block spins.")]
        [SerializeField] float spinSpeed = 90f;

        [Tooltip("How far the mini block bobs up and down, in metres.")]
        [SerializeField, Range(0f, 0.2f)] float bobHeight = 0.05f;

        readonly NetworkVariable<ushort> _blockId = new NetworkVariable<ushort>();
        readonly NetworkVariable<ushort> _count = new NetworkVariable<ushort>();

        // Host-only state.
        VoxelWorld _world;
        ushort _assignedBlockId;
        ushort _assignedCount;
        float3 _velocity;
        bool _pulled;
        float _bobPhase;
        Vector3 _lookOffset;

        public ushort BlockId => _blockId.Value;
        public int Count => _count.Value;

        /// <summary>Host only: when this drop appeared, in game seconds.</summary>
        public float SpawnTime { get; private set; }

        /// <summary>Host only: lying still on the ground.</summary>
        public bool IsResting { get; private set; }

        /// <summary>Raised on every PC when a drop spawns, so <see cref="BlockDropSpawner"/> can give it its look.</summary>
        public static event Action<BlockDrop> Spawned;

        /// <summary>Host only, before spawning: what this drop is and how it starts moving.</summary>
        internal void ServerInitialize(VoxelWorld world, ushort blockId, int count, float3 velocity)
        {
            _world = world;
            _assignedBlockId = blockId;
            _assignedCount = (ushort)math.clamp(count, 1, ushort.MaxValue);
            _velocity = velocity;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                _blockId.Value = _assignedBlockId;
                _count.Value = _assignedCount;
                SpawnTime = Time.time;
            }
            _bobPhase = UnityEngine.Random.value * Mathf.PI * 2f;
            name = $"Block Drop ({BlockId} x{Count})";
            Spawned?.Invoke(this);
        }

        /// <summary>
        /// Any PC: shows this drop as <paramref name="look"/>. A block is a cube <c>size</c> across; a model (a torch,
        /// Roadmap 3.5) is fitted to that size times its <see cref="ItemDefinition.DropSize"/> and centred.
        /// </summary>
        public void SetLook(ItemLook look)
        {
            if (visual == null)
                return;
            visual.sharedMesh = look.Mesh;
            if (visual.TryGetComponent(out MeshRenderer meshRenderer))
                meshRenderer.sharedMaterial = look.Material;
            float scale = look.IsModel ? size * look.Definition.DropSize * look.FitScale : size;
            visual.transform.localScale = Vector3.one * scale;
            _lookOffset = -look.Center * scale;
        }

        /// <summary>Host only: changes how many blocks this drop holds (merging, or a bag that was nearly full).</summary>
        internal void ServerSetCount(int count) => _count.Value = (ushort)math.clamp(count, 1, ushort.MaxValue);

        /// <summary>Host only: flies toward <paramref name="target"/> this step, through blocks, as if sucked in.</summary>
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
                _pulled = false; // The spawner pulls again next step while a player is close.
                return;
            }

            IsResting = DropPhysics.Step(_world, ref position, ref _velocity, size * 0.5f, gravity, dt);
            transform.position = position;
        }

        void Update()
        {
            if (visual == null)
                return;
            float t = Time.time + _bobPhase;
            Transform look = visual.transform;
            Quaternion spin = Quaternion.Euler(0f, t * spinSpeed, 0f);
            look.localRotation = spin;
            // Spin around the middle of the model, not its origin.
            look.localPosition = new Vector3(0f, bobHeight * (1f + Mathf.Sin(t * 2.5f)), 0f) + spin * _lookOffset;
        }
    }
}
