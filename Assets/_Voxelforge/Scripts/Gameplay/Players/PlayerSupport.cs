using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// This player's support spells (Roadmap 4.6): shields (<see cref="AbilityDelivery.Shield"/>, Bulwark Rune) and circles
    /// on the ground (<see cref="AbilityDelivery.Zone"/>, Hallowed Forge). <see cref="PlayerCaster"/> decides <i>when</i>
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
            AbilityZone zone = Instantiate(zonePrefab, transform.position, Quaternion.identity);
            zone.ServerInit(ability, _player.PlayerId, _player.Registry);
            zone.NetworkObject.Spawn(destroyWithScene: true);
        }
    }
}
