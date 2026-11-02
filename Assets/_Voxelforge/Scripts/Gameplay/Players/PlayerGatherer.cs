using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>What a hero is gathering right now (Roadmap 5.7), synced for the cast bar on every PC.</summary>
    public struct GatherState : INetworkSerializeByMemcpy, IEquatable<GatherState>
    {
        public bool Active;
        public GatherNodeKind Kind;
        /// <summary>The herb's item ID (Herbalism), for the cast bar's name and icon.</summary>
        public ushort Item;
        /// <summary>How long it takes, in seconds.</summary>
        public float Seconds;

        public bool Equals(GatherState other) =>
            Active == other.Active && Kind == other.Kind && Item == other.Item && Seconds.Equals(other.Seconds);

        public override bool Equals(object obj) => obj is GatherState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Active, Kind, Item, Seconds);
    }

    /// <summary>
    /// Herbalism (Roadmap 5.7, D-038). <b>The owner</b> presses Interact aiming at a herb (<see cref="PlayerCrafter"/>
    /// asks <see cref="OwnerTryGatherAimed"/> first, before chests and the crafting menu) and asks the host to gather it.
    /// <b>The host</b> checks the herb is really there and unpicked (<see cref="HerbField"/>), within reach, and that the
    /// hero has the skill (or tells them "Requires Herbalism 25"), then runs a short gather that moving, jumping or mining
    /// cancels. When it finishes, the herb is picked for everyone, its yield goes into this hero's bag (anything that
    /// doesn't fit into their own sack) and Herbalism may go up. The request carries no player ID: the host knows whose
    /// hero this is (CLAUDE.md Laws #1 and #2).
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerGatherer : NetworkBehaviour
    {
        [Tooltip("Owner: a herb this many blocks to the side of where they aim still counts, so small plants are easy to pick.")]
        [SerializeField, Range(0, 2)] int herbSearch = 1;

        // Moving the stick further than this, or drifting this far, cancels a gather (as a hearth cast).
        const float MoveDeadzone = 0.2f;
        const float DriftAllowed = 0.35f;

        readonly NetworkVariable<GatherState> _state = new NetworkVariable<GatherState>();
        readonly System.Random _random = new System.Random(Environment.TickCount);
        readonly List<ItemStack> _rolled = new List<ItemStack>();
        readonly List<ItemStack> _leftover = new List<ItemStack>();

        PlayerController _player;
        PlayerProfessions _professions;
        PlayerBlockBag _bag;
        float _startLocal;
        // Host: what's being gathered.
        int3 _cell;
        GatherNode _node;
        float _start;
        float3 _feet;

        /// <summary>Every PC: the hero is gathering something.</summary>
        public bool IsGathering => _state.Value.Active;

        /// <summary>Every PC: the herb being gathered, while <see cref="IsGathering"/>.</summary>
        public ushort GatheringItem => _state.Value.Item;

        /// <summary>Every PC: how long the gather takes, in seconds.</summary>
        public float GatherSeconds => _state.Value.Seconds;

        /// <summary>Every PC: how far through the gather the hero is, 0-1, timed on this PC.</summary>
        public float Progress => IsGathering && _state.Value.Seconds > 0f
            ? Mathf.Clamp01((Time.time - _startLocal) / _state.Value.Seconds)
            : 0f;

        /// <summary>Every PC: the hero finished gathering this item, for the cast bar's flash.</summary>
        public event Action<ushort> Gathered;

        HerbField Herbs => _player.Registry != null ? _player.Registry.Herbs : null;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _professions = GetComponent<PlayerProfessions>();
            _bag = GetComponent<PlayerBlockBag>();
        }

        public override void OnNetworkSpawn() => _state.OnValueChanged += OnStateChanged;

        public override void OnNetworkDespawn()
        {
            _state.OnValueChanged -= OnStateChanged;
            base.OnNetworkDespawn();
        }

        void OnStateChanged(GatherState before, GatherState after)
        {
            if (after.Active && (!before.Active || before.Item != after.Item))
                _startLocal = Time.time;
        }

        /// <summary>
        /// Owner: Interact was pressed. If they're aiming at a herb, asks the host to gather it and returns true, so the
        /// press opens nothing else. Also true while already gathering, so a second press doesn't open the crafting menu.
        /// </summary>
        public bool OwnerTryGatherAimed()
        {
            if (!IsOwner || !IsSpawned || !_player.HasLanded)
                return false;
            if (IsGathering)
                return true;
            HerbField herbs = Herbs;
            VoxelWorld world = _player.World;
            if (herbs == null || world == null ||
                !_player.Aim.TryAim(world, transform.position, _player.OwnerRawInput, out VoxelHit hit) ||
                !TryFindHerb(herbs, hit, out int3 cell))
                return false;
            GatherHerbRpc(new PickedHerb(cell));
            return true;
        }

        /// <summary>
        /// The herb nearest where the aim landed: one stands in the open block over a block, so look round the block above
        /// the one hit and the one in front of the face hit.
        /// </summary>
        bool TryFindHerb(HerbField herbs, in VoxelHit hit, out int3 cell)
        {
            cell = default;
            bool found = false;
            float best = float.MaxValue;
            int3 above = hit.Block + new int3(0, 1, 0);
            int3 front = hit.Block + (hit.Normal.Equals(int3.zero) ? new int3(0, 1, 0) : hit.Normal);
            foreach (int3 centre in new[] { above, front })
            {
                for (int dz = -herbSearch; dz <= herbSearch; dz++)
                for (int dx = -herbSearch; dx <= herbSearch; dx++)
                {
                    int3 candidate = centre + new int3(dx, 0, dz);
                    if (!herbs.TryGetHerb(candidate, out _))
                        continue;
                    float distance = math.distancesq((float3)candidate + new float3(0.5f, 0.3f, 0.5f), hit.Point);
                    if (distance < best)
                    {
                        best = distance;
                        cell = candidate;
                        found = true;
                    }
                }
            }
            return found;
        }

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || !_state.Value.Active)
                return;

            PlayerInputFrame input = _player.ServerInput;
            bool moved = math.length(input.Move) > MoveDeadzone ||
                         (input.Buttons & (PlayerButtons.Jump | PlayerButtons.Mine)) != 0 ||
                         math.distance(((float3)transform.position).xz, _feet.xz) > DriftAllowed;
            HerbField herbs = Herbs;
            if (moved || _player.IsKnockedOut || herbs == null || !herbs.TryGetHerb(_cell, out _))
            {
                Stop(done: false); // Someone else may have picked it first.
                return;
            }
            if (Time.time - _start < _node.GatherSeconds)
                return;

            if (!herbs.ServerTryPick(_cell, out ushort herb))
            {
                Stop(done: false);
                return;
            }
            ServerGive(herb);
            _professions.ServerGathered(_node);
            Stop(done: true);
        }

        /// <summary>The yield (and maybe a bonus herb) into this hero's bag; whatever doesn't fit, into their own sack.</summary>
        void ServerGive(ushort herb)
        {
            _rolled.Clear();
            if (_node.Yield != null)
                LootRoller.Roll(_node.Yield, _random, _rolled);
            if (_professions.ServerRollBonusYield(_node))
                _rolled.Add(new ItemStack(herb, 1));

            _leftover.Clear();
            foreach (ItemStack stack in _rolled)
            {
                int added = _bag != null ? _bag.ServerAdd(stack.ItemId, stack.Count) : 0;
                if (added > 0)
                    _bag.ServerTellPickedUp(stack.ItemId, added);
                if (added < stack.Count)
                    _leftover.Add(new ItemStack(stack.ItemId, (ushort)(stack.Count - added)));
            }
            LootBagSpawner loot = _player.Registry != null ? _player.Registry.Loot : null;
            if (_leftover.Count > 0 && loot != null)
                loot.ServerSpawnFor(_player, (float3)transform.position + new float3(0f, 1f, 0f), _leftover,
                    new float3(0f, 2f, 0f), ItemRarity.Common);
        }

        void Stop(bool done)
        {
            ushort item = _state.Value.Item;
            _state.Value = default;
            if (done)
                GatheredRpc(item);
        }

        [Rpc(SendTo.Server)]
        void GatherHerbRpc(PickedHerb herbAt, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || !_player.HasLanded || _player.IsKnockedOut ||
                _state.Value.Active || _professions == null)
                return;
            int3 cell = herbAt.Cell;
            HerbField herbs = Herbs;
            if (herbs == null || !_player.Aim.InReach(transform.position, cell) || !herbs.TryGetHerb(cell, out ushort herb) ||
                !_professions.TryGetNode(GatherNodeKind.Herb, herb, out GatherNode node))
                return;
            if (!_professions.CanGather(node))
            {
                _professions.ServerTellNeedsSkill(node);
                return;
            }

            _cell = cell;
            _node = node;
            _start = Time.time;
            _feet = transform.position;
            _state.Value = new GatherState { Active = true, Kind = GatherNodeKind.Herb, Item = herb, Seconds = node.GatherSeconds };
        }

        [Rpc(SendTo.Everyone)]
        void GatheredRpc(ushort item) => Gathered?.Invoke(item);
    }
}
