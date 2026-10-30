using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// This player's support spells (Roadmap 4.6): shields (<see cref="AbilityDelivery.Shield"/>, Bulwark Rune), circles
    /// on the ground (<see cref="AbilityDelivery.Zone"/>, Hallowed Forge) and totems (<see cref="AbilityDelivery.Totem"/>,
    /// the Shaman's, one of each kind). <see cref="PlayerCaster"/> decides <i>when</i>
    /// one goes off; then <b>the host</b> picks who it lands on from the aim it received (<see cref="SupportRules"/>) or
    /// spawns the zone at the hero's feet (<see cref="AbilityZone"/>). No position or target ever comes from a PC
    /// (CLAUDE.md Laws #1-#2).
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(PlayerHealth))]
    public sealed class PlayerSupport : NetworkBehaviour
    {
        [Tooltip("Prefabs/AbilityZone.prefab: the circle a Zone spell leaves on the ground.")]
        [SerializeField] AbilityZone zonePrefab;

        PlayerController _player;
        PlayerHealth _health;
        readonly List<float3> _partnerFeet = new List<float3>();
        readonly List<PlayerHealth> _partners = new List<PlayerHealth>();
        // Host: this caster's standing totems, one per kind (Roadmap 4.6).
        readonly Dictionary<ushort, AbilityZone> _totems = new Dictionary<ushort, AbilityZone>();

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _health = GetComponent<PlayerHealth>();
        }

        /// <summary>
        /// Host only: <paramref name="ability"/>'s shield goes on the partner nearest the hero's aim within its range and
        /// cone, or on the hero.
        /// </summary>
        public void ServerShield(AbilityDefinition ability, PlayerInputFrame input)
        {
            if (!IsServer || !IsSpawned || ability == null)
                return;
            _partnerFeet.Clear();
            _partners.Clear();
            PlayerRegistry registry = _player.Registry;
            if (registry != null)
            {
                foreach (PlayerController other in registry.Players)
                {
                    if (other == null || other == _player || !other.TryGetComponent(out PlayerHealth health))
                        continue;
                    _partnerFeet.Add(other.transform.position);
                    _partners.Add(health);
                }
            }
            int pick = SupportRules.PickPartner(_player.Aim.Eyes(transform.position), _player.Aim.Direction(input), _partnerFeet,
                ability.Range, ability.AimCone);
            PlayerHealth target = pick >= 0 ? _partners[pick] : _health;
            target.ServerShield(ability.ShieldAmount, ability.Duration, _player.PlayerId, ability.Id);
        }

        /// <summary>Host only: <paramref name="ability"/>'s circle on the ground, centred on the hero's feet.</summary>
        public void ServerZone(AbilityDefinition ability)
        {
            if (!IsServer || !IsSpawned || ability == null || zonePrefab == null)
                return;
            Spawn(ability, transform.position);
        }

        /// <summary>
        /// Host only: <paramref name="ability"/>'s totem stands in <paramref name="cell"/> (on the block under it). A totem of
        /// the same kind this hero already has is ended: one of each per Shaman.
        /// </summary>
        public void ServerTotem(AbilityDefinition ability, int3 cell)
        {
            if (!IsServer || !IsSpawned || ability == null || zonePrefab == null)
                return;
            if (_totems.TryGetValue(ability.Id, out AbilityZone old) && old != null && old.IsRunning)
                old.ServerEnd();
            _totems[ability.Id] = Spawn(ability, (float3)cell + new float3(0.5f, 0f, 0.5f));
        }

        AbilityZone Spawn(AbilityDefinition ability, Vector3 at)
        {
            AbilityZone zone = Instantiate(zonePrefab, at, Quaternion.identity);
            zone.ServerInit(ability, _player);
            zone.NetworkObject.Spawn(destroyWithScene: true);
            return zone;
        }
    }
}
