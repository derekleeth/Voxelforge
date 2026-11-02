using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Core;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>What a hero is gathering right now (Roadmap 5.7), synced for the cast bar on every PC.</summary>
    public struct GatherState : INetworkSerializeByMemcpy, IEquatable<GatherState>
    {
        public bool Active;
        public GatherNodeKind Kind;
        /// <summary>The herb's item ID, or the main thing a body gives (its hide or shard), for the cast bar's icon.</summary>
        public ushort Item;
        /// <summary>How long it takes, in seconds.</summary>
        public float Seconds;
        /// <summary>Skinning: the body's <see cref="NetworkObject"/> ID, for the cast bar's name.</summary>
        public ulong Target;

        public bool Equals(GatherState other) => Active == other.Active && Kind == other.Kind && Item == other.Item &&
                                                  Seconds.Equals(other.Seconds) && Target == other.Target;

        public override bool Equals(object obj) => obj is GatherState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Active, Kind, Item, Seconds, Target);
    }

    /// <summary>
    /// Herbalism and Skinning (Roadmap 5.7, D-038). <b>The owner</b> presses Interact aiming at a herb or at a defeated
    /// mob's body (<see cref="PlayerCrafter"/> asks <see cref="OwnerTryGatherAimed"/> first, before chests and the crafting
    /// menu) and asks the host to gather it. <b>The host</b> checks the herb is really there and unpicked
    /// (<see cref="HerbField"/>), or that this hero fought the mob and hasn't skinned it yet (<see cref="Mob"/>), that it's
    /// within reach, and that the hero has the skill (or tells them "Requires Skinning 40"). Then it runs a short gather
    /// that moving, jumping or mining cancels. When it finishes, the herb is picked for everyone or the body is skinned for
    /// this hero, the yield goes into this hero's bag (anything that doesn't fit into their own sack) and the skill may go
    /// up. Requests carry no player ID: the host knows whose hero this is (CLAUDE.md Laws #1 and #2).
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerGatherer : NetworkBehaviour
    {
        [Tooltip("Owner: a herb this many blocks to the side of where they aim still counts, so small plants are easy to pick.")]
        [SerializeField, Range(0, 2)] int herbSearch = 1;

        [Tooltip("Owner: a body within this angle of where they aim counts as aimed at, in degrees.")]
        [SerializeField, Range(5f, 60f)] float bodyAimAngle = 30f;

        [Tooltip("A body this far beyond the hero's reach (from its middle) can still be skinned, in metres: bodies are big.")]
        [SerializeField, Range(0f, 3f)] float bodyReachBonus = 1f;

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
        Mob _body;
        GatherNode _node;
        float _start;
        float3 _feet;

        /// <summary>Every PC: the hero is gathering something.</summary>
        public bool IsGathering => _state.Value.Active;

        /// <summary>Every PC: the herb, or the main thing a body gives, while <see cref="IsGathering"/>.</summary>
        public ushort GatheringItem => _state.Value.Item;

        /// <summary>Every PC: how long the gather takes, in seconds.</summary>
        public float GatherSeconds => _state.Value.Seconds;

        /// <summary>Every PC: when this gather started on this PC, so the cast bar knows a new gather of the same thing.</summary>
        public float GatherStarted => _startLocal;

        /// <summary>Every PC: how far through the gather the hero is, 0-1, timed on this PC.</summary>
        public float Progress => IsGathering && _state.Value.Seconds > 0f
            ? Mathf.Clamp01((Time.time - _startLocal) / _state.Value.Seconds)
            : 0f;

        /// <summary>
        /// Every PC: the hero finished gathering a herb or a body (and the item the cast bar showed), for its flash and
        /// sound.
        /// </summary>
        public event Action<GatherNodeKind, ushort> Gathered;

        HerbField Herbs => _player.Registry != null ? _player.Registry.Herbs : null;
        CombatTargets Targets => _player.Registry != null ? _player.Registry.Targets : null;

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
            if (after.Active && (!before.Active || before.Item != after.Item || before.Target != after.Target))
                _startLocal = Time.time;
        }

        /// <summary>
        /// Every PC: what the cast bar calls the gather: the herb's name, or "Skinning Thunder Rhino" ("Salvaging" for
        /// slimes).
        /// </summary>
        public string DescribeGather(Func<ushort, string> itemName)
        {
            GatherState state = _state.Value;
            if (state.Kind != GatherNodeKind.Mob)
                return itemName != null ? itemName(state.Item) : string.Empty;
            string verb = "Skinning";
            string name = "body";
            if (NetworkManager != null && NetworkManager.SpawnManager != null &&
                NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(state.Target, out NetworkObject target) &&
                target.TryGetComponent(out Mob mob) && mob.Definition != null)
            {
                name = mob.Definition.DisplayName;
                if (mob.Definition.Salvaged)
                    verb = "Salvaging";
            }
            return $"{verb} {name}";
        }

        /// <summary>
        /// Owner: Interact was pressed. If they're aiming at a herb, or at a body they may skin, asks the host to gather it
        /// and returns true, so the press opens nothing else. Also true while already gathering, so a second press doesn't
        /// open the crafting menu.
        /// </summary>
        public bool OwnerTryGatherAimed()
        {
            if (!IsOwner || !IsSpawned || !_player.HasLanded)
                return false;
            if (IsGathering)
                return true;
            VoxelWorld world = _player.World;
            if (world == null)
                return false;
            PlayerInputFrame input = _player.OwnerRawInput;
            bool aimed = _player.Aim.TryAim(world, transform.position, input, out VoxelHit hit);
            HerbField herbs = Herbs;
            if (aimed && herbs != null && TryFindHerb(herbs, hit, out int3 cell))
            {
                GatherHerbRpc(new PickedHerb(cell));
                return true;
            }
            if (TryFindBody(input, aimed ? hit.Distance : float.MaxValue, out Mob body))
            {
                SkinRpc(new NetworkObjectReference(body.NetworkObject));
                return true;
            }
            return false;
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

        /// <summary>
        /// The body this hero may skin that's nearest the middle of their aim, within reach and not behind the block the aim
        /// hit (<paramref name="blockDistance"/> from the eyes).
        /// </summary>
        bool TryFindBody(PlayerInputFrame input, float blockDistance, out Mob body)
        {
            body = null;
            CombatTargets targets = Targets;
            if (targets == null)
                return false;
            float3 eyes = _player.Aim.Eyes(transform.position);
            float3 aim = _player.Aim.Direction(input);
            float best = math.cos(math.radians(bodyAimAngle));
            foreach (ICombatCreature creature in targets.Creatures)
            {
                if (!(creature is Mob mob) || !mob.CanBeSkinnedBy(_player.PlayerId))
                    continue;
                float3 middle = mob.Feet + new float3(0f, mob.HalfExtents.y, 0f);
                float3 to = middle - eyes;
                float distance = math.length(to);
                if (distance > _player.Aim.Reach + bodyReachBonus + math.cmax(mob.HalfExtents.xz) ||
                    distance > blockDistance + math.cmax(mob.HalfExtents) + 0.5f)
                    continue;
                float facing = distance > 0.01f ? math.dot(to / distance, aim) : 1f;
                if (facing > best)
                {
                    best = facing;
                    body = mob;
                }
            }
            return body != null;
        }

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || !_state.Value.Active)
                return;

            PlayerInputFrame input = _player.ServerInput;
            bool moved = math.length(input.Move) > MoveDeadzone ||
                         (input.Buttons & (PlayerButtons.Jump | PlayerButtons.Mine)) != 0 ||
                         math.distance(((float3)transform.position).xz, _feet.xz) > DriftAllowed;
            if (moved || _player.IsKnockedOut || !StillThere())
            {
                Stop(done: false); // Someone else may have picked it first, or the body vanished.
                return;
            }
            if (Time.time - _start < _node.GatherSeconds)
                return;

            ushort bonus;
            if (_state.Value.Kind == GatherNodeKind.Herb)
            {
                if (!Herbs.ServerTryPick(_cell, out bonus))
                {
                    Stop(done: false);
                    return;
                }
            }
            else
            {
                if (!_body.ServerTakeSkin(_player.PlayerId))
                {
                    Stop(done: false);
                    return;
                }
                bonus = _state.Value.Item;
            }
            ServerGive(bonus);
            _professions.ServerGathered(_node);
            Stop(done: true);
        }

        bool StillThere()
        {
            if (_state.Value.Kind == GatherNodeKind.Herb)
                return Herbs != null && Herbs.TryGetHerb(_cell, out _);
            return _body != null && _body.IsSpawned && _body.CanBeSkinnedBy(_player.PlayerId);
        }

        /// <summary>
        /// The yield (and maybe one more <paramref name="bonus"/>, for a skilled gatherer) into this hero's bag; whatever
        /// doesn't fit, into their own sack.
        /// </summary>
        void ServerGive(ushort bonus)
        {
            _rolled.Clear();
            if (_node.Yield != null)
                LootRoller.Roll(_node.Yield, _random, _rolled);
            if (bonus != 0 && _professions.ServerRollBonusYield(_node))
                _rolled.Add(new ItemStack(bonus, 1));

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
            GatherState was = _state.Value;
            _state.Value = default;
            _body = null;
            if (done)
                GatheredRpc((byte)was.Kind, was.Item);
        }

        void Begin(GatherNodeKind kind, in GatherNode node, ushort item, ulong target)
        {
            _node = node;
            _start = Time.time;
            _feet = transform.position;
            _state.Value = new GatherState { Active = true, Kind = kind, Item = item, Seconds = node.GatherSeconds, Target = target };
        }

        /// <summary>Host: false if the request mustn't be served at all (wrong sender, busy, knocked out).</summary>
        bool CanServe(RpcParams rpcParams) =>
            rpcParams.Receive.SenderClientId == OwnerClientId && _player.HasLanded && !_player.IsKnockedOut &&
            !_state.Value.Active && _professions != null;

        [Rpc(SendTo.Server)]
        void GatherHerbRpc(PickedHerb herbAt, RpcParams rpcParams = default)
        {
            if (!CanServe(rpcParams))
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
            Begin(GatherNodeKind.Herb, node, herb, 0);
        }

        [Rpc(SendTo.Server)]
        void SkinRpc(NetworkObjectReference bodyRef, RpcParams rpcParams = default)
        {
            if (!CanServe(rpcParams) || !bodyRef.TryGet(out NetworkObject bodyObject) || !bodyObject.TryGetComponent(out Mob body) ||
                body.Definition == null || !body.CanBeSkinnedBy(_player.PlayerId) ||
                !_professions.TryGetNode(GatherNodeKind.Mob, (ushort)body.Definition.Id, out GatherNode node))
                return;
            float3 middle = body.Feet + new float3(0f, body.HalfExtents.y, 0f);
            if (math.distance(_player.Aim.Eyes(transform.position), middle) >
                _player.Aim.Reach + bodyReachBonus + math.cmax(body.HalfExtents.xz) + 0.5f)
                return;
            if (!_professions.CanGather(node))
            {
                _professions.ServerTellNeedsSkill(node);
                return;
            }
            _body = body;
            ushort main = node.Yield != null && node.Yield.Drops.Count > 0 ? node.Yield.Drops[0].Item : (ushort)0;
            Begin(GatherNodeKind.Mob, node, main, body.NetworkObjectId);
        }

        [Rpc(SendTo.Everyone)]
        void GatheredRpc(byte kind, ushort item) => Gathered?.Invoke((GatherNodeKind)kind, item);
    }
}
