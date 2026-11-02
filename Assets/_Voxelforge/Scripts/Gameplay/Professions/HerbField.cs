using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>A picked herb's block, as synced by <see cref="HerbField"/> (Roadmap 5.7).</summary>
    public struct PickedHerb : INetworkSerializeByMemcpy, IEquatable<PickedHerb>
    {
        public int3 Cell;

        public PickedHerb(int3 cell) => Cell = cell;

        public bool Equals(PickedHerb other) => math.all(Cell == other.Cell);
        public override bool Equals(object obj) => obj is PickedHerb other && Equals(other);
        public override int GetHashCode() => Cell.GetHashCode();
    }

    /// <summary>
    /// The herbs gatherers have picked (Roadmap 5.7, D-038). Where herbs grow comes from the seed
    /// (<see cref="HerbPlacement"/>), so the only thing to keep is which were picked: <b>the host</b> holds a sparse map
    /// of when each grows back (like D-003's mining damage, and saved with the world like Earthen Rock), and syncs the
    /// list to <b>every PC</b>, which hides those herbs in its decoration layer. Picking goes through
    /// <see cref="ServerTryPick"/>, called by a hero's <see cref="PlayerGatherer"/> after the host checked their request.
    /// <b>Not a singleton:</b> heroes reach it through <see cref="PlayerRegistry.Herbs"/> (CLAUDE.md Law #1).
    /// </summary>
    public sealed class HerbField : NetworkBehaviour
    {
        [SerializeField] VoxelWorldManager world;

        [Tooltip("Data/Professions/ProfessionBook.asset: how long a picked herb takes to grow back.")]
        [SerializeField] ProfessionBook book;

        [Tooltip("Host: seconds between checks for herbs that have grown back.")]
        [SerializeField, Range(0.1f, 5f)] float regrowCheck = 1f;

        readonly NetworkList<PickedHerb> _picked = new NetworkList<PickedHerb>();
        readonly EarthTimers _regrowAt = new EarthTimers(); // The same sparse cell → time map Earthen Rock uses.
        readonly HashSet<int3> _hidden = new HashSet<int3>();
        readonly HashSet<int3> _listed = new HashSet<int3>();
        readonly List<int3> _grown = new List<int3>();
        // Apart from _grown: removing from _picked runs SyncHidden at once on the host, which fills _grown for itself.
        readonly List<int3> _expired = new List<int3>();
        IReadOnlyDictionary<int3, float> _pendingRestore;
        DecorationLayer _layer; // The layer the hidden herbs were last given to; the world may make it after this spawns.
        float _nextCheck;

        /// <summary>Every PC: how many herbs are picked right now.</summary>
        public int PickedCount => _picked.Count;

        float RegrowSeconds => book != null ? book.HerbRegrowSeconds : 300f;

        public override void OnNetworkSpawn()
        {
            _picked.OnListChanged += OnListChanged;
            if (IsServer && _pendingRestore != null)
            {
                IReadOnlyDictionary<int3, float> saved = _pendingRestore;
                _pendingRestore = null;
                ServerRestore(saved);
            }
            SyncHidden();
        }

        public override void OnNetworkDespawn()
        {
            _picked.OnListChanged -= OnListChanged;
            base.OnNetworkDespawn();
        }

        /// <summary>
        /// Every PC that can see blocks: the herb (its item ID) growing in block <paramref name="cell"/>, if there is one
        /// and nobody has picked it.
        /// </summary>
        public bool TryGetHerb(int3 cell, out ushort herbItem)
        {
            herbItem = 0;
            if (world == null || world.World == null || world.Decorations == null || _hidden.Contains(cell))
                return false;
            VoxelWorld blocks = world.World;
            return world.Decorations.TryGetHerb(cell, blocks.GetVoxel(cell).BlockId,
                blocks.GetVoxel(cell - new int3(0, 1, 0)).BlockId, out herbItem);
        }

        /// <summary>
        /// Host only: picks the herb in <paramref name="cell"/>, which grows back after the book's regrow time. False if
        /// there's no herb there (any more), e.g. someone else got it first.
        /// </summary>
        public bool ServerTryPick(int3 cell, out ushort herbItem)
        {
            herbItem = 0;
            if (!IsServer || !IsSpawned || !TryGetHerb(cell, out herbItem))
                return false;
            _regrowAt.Add(cell, Time.time + RegrowSeconds);
            _picked.Add(new PickedHerb(cell));
            SyncHidden(); // At once on the host, so a second gatherer this frame finds it gone.
            return true;
        }

        /// <summary>Host only: every picked herb and the seconds until it grows back, for saving.</summary>
        public Dictionary<int3, float> ServerSnapshot() =>
            !IsSpawned && _pendingRestore != null ? new Dictionary<int3, float>(_pendingRestore) : _regrowAt.Snapshot(Time.time);

        /// <summary>Host only: the picked herbs from a save. Kept until this spawns, if it hasn't yet.</summary>
        public void ServerRestore(IReadOnlyDictionary<int3, float> secondsLeft)
        {
            if (!IsSpawned)
            {
                _pendingRestore = secondsLeft;
                return;
            }
            if (!IsServer)
                return;
            _regrowAt.Restore(secondsLeft, Time.time);
            _picked.Clear();
            if (secondsLeft != null)
            {
                foreach (int3 cell in secondsLeft.Keys)
                    _picked.Add(new PickedHerb(cell));
            }
            SyncHidden();
        }

        void Update()
        {
            DecorationLayer layer = world != null ? world.Decorations : null;
            if (layer != _layer && IsSpawned)
            {
                _layer = layer;
                foreach (int3 cell in _hidden)
                    _layer?.SetHerbHidden(cell, true);
            }
            if (!IsServer || !IsSpawned || Time.time < _nextCheck)
                return;
            _nextCheck = Time.time + regrowCheck;
            _expired.Clear();
            _regrowAt.CollectExpired(Time.time, _expired);
            if (_expired.Count == 0)
                return;
            foreach (int3 cell in _expired)
            {
                _regrowAt.Remove(cell);
                _picked.Remove(new PickedHerb(cell));
            }
            SyncHidden();
        }

        void OnListChanged(NetworkListEvent<PickedHerb> change) => SyncHidden();

        /// <summary>Hides the herbs on the list in this PC's decoration layer and shows the ones that left it.</summary>
        void SyncHidden()
        {
            _listed.Clear();
            foreach (PickedHerb herb in _picked)
                _listed.Add(herb.Cell);
            DecorationLayer layer = _layer = world != null ? world.Decorations : null;
            foreach (int3 cell in _listed)
            {
                if (_hidden.Add(cell))
                    layer?.SetHerbHidden(cell, true);
            }
            _grown.Clear();
            foreach (int3 cell in _hidden)
            {
                if (!_listed.Contains(cell))
                    _grown.Add(cell);
            }
            foreach (int3 cell in _grown)
            {
                _hidden.Remove(cell);
                layer?.SetHerbHidden(cell, false);
            }
        }
    }
}
