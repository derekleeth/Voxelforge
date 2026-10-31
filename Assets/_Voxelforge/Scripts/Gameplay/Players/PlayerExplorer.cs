using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The parts of the world this hero has explored, for their map's fog of war (Roadmap 4.8, D-031). <b>The host</b>
    /// decides it from where the hero really is (CLAUDE.md Law #2), keeps it in their profile (<see cref="WorldSaveManager"/>)
    /// and sends each newly explored batch to the owner's PC, which draws the fog from its copy. Other PCs don't need it:
    /// each player only sees their own fog.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerExplorer : NetworkBehaviour
    {
        [Tooltip("How far around the hero the map clears, in cells of 16 blocks. 3 is about 50 blocks.")]
        [SerializeField, Range(1, 8)] int revealRadius = 3;

        /// <summary>Most cells in one message to the owner, so a long saved map arrives in a few small parts.</summary>
        const int CellsPerMessage = 256;

        readonly ExploredMap _map = new ExploredMap();
        readonly List<int2> _added = new List<int2>();
        PlayerController _player;
        int2 _lastCell;
        bool _hasCell;

        /// <summary>
        /// On the host: every cell this hero has explored. On the owner's PC: its copy, as the host sends it. Empty on any
        /// other PC. The same object for the hero's whole life, so a map can keep watching it.
        /// </summary>
        public ExploredMap Map => _map;

        void Awake() => _player = GetComponent<PlayerController>();

        public override void OnNetworkDespawn()
        {
            _hasCell = false;
            base.OnNetworkDespawn();
        }

        /// <summary>Host only, right after spawning: gives the hero the map they'd explored last time, and sends it to their PC.</summary>
        public void ServerRestore(IEnumerable<int2> cells)
        {
            if (!IsServer || cells == null)
                return;
            _added.Clear();
            _added.AddRange(cells);
            _map.Add(_added);
            SendToOwner(_added);
        }

        void Update()
        {
            if (!IsServer || !IsSpawned || !_player.HasLanded)
                return;
            Vector3 feet = _player.transform.position;
            int2 cell = ExploredMap.CellOf(feet.x, feet.z);
            if (_hasCell && cell.Equals(_lastCell))
                return;
            _lastCell = cell;
            _hasCell = true;
            _added.Clear();
            if (_map.Reveal(cell, revealRadius, _added) > 0)
                SendToOwner(_added);
        }

        /// <summary>
        /// Sends <paramref name="cells"/> to the owner's PC, as x, z pairs. A hero owned by the host shares the host's map
        /// already, so nothing is sent.
        /// </summary>
        void SendToOwner(List<int2> cells)
        {
            if (IsOwner)
                return;
            for (int start = 0; start < cells.Count; start += CellsPerMessage)
            {
                int count = math.min(CellsPerMessage, cells.Count - start);
                var packed = new int[count * 2];
                for (int i = 0; i < count; i++)
                {
                    packed[i * 2] = cells[start + i].x;
                    packed[i * 2 + 1] = cells[start + i].y;
                }
                ExploredRpc(packed);
            }
        }

        [Rpc(SendTo.Owner)]
        void ExploredRpc(int[] packed)
        {
            if (IsServer)
                return;
            var cells = new List<int2>(packed.Length / 2);
            for (int i = 0; i + 1 < packed.Length; i += 2)
                cells.Add(new int2(packed[i], packed[i + 1]));
            _map.Add(cells);
        }
    }
}
