using System;
using Unity.Netcode;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// This hero's class (Roadmap 4.6): Adventurer, Runeforged Paladin, Geomancer Mage or Shaman. <b>The host</b> decides
    /// it: the owner only asks (<see cref="RequestChoose"/>, from the class window), and the host checks the request, then
    /// gives the hero the class's action bar (<see cref="PlayerActionBar"/>), mana pool (<see cref="PlayerCaster"/>) and
    /// health (<see cref="PlayerHealth"/>) (CLAUDE.md Law #2). Every PC sees the class through a network variable. It's
    /// saved in the player's profile and comes back with them (<see cref="PlayerRegistry"/>). Changing class is free for
    /// now.
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(PlayerActionBar), typeof(PlayerCaster))]
    public sealed class PlayerClass : NetworkBehaviour
    {
        [Tooltip("Data/Classes/ClassBook.asset: every class.")]
        [SerializeField] ClassBook classes;

        [Tooltip("Requests closer together than this are ignored, in seconds.")]
        [SerializeField, Range(0f, 2f)] float minRequestInterval = 0.5f;

        readonly NetworkVariable<HeroClassId> _class = new NetworkVariable<HeroClassId>();

        PlayerActionBar _actionBar;
        PlayerCaster _caster;
        PlayerHealth _health;
        float _lastRequestTime = float.NegativeInfinity;

        public ClassBook Book => classes;

        /// <summary>Every PC: the hero's class, as the host shared it.</summary>
        public HeroClassId Class => _class.Value;

        /// <summary>Every PC: the definition of <see cref="Class"/>, or null if the book doesn't have it.</summary>
        public ClassDefinition Definition => classes != null && classes.TryGet(_class.Value, out ClassDefinition definition)
            ? definition
            : null;

        /// <summary>Every PC: the hero's class changed (not when they first spawn with it).</summary>
        public event Action<HeroClassId> Changed;

        void Awake()
        {
            _actionBar = GetComponent<PlayerActionBar>();
            _caster = GetComponent<PlayerCaster>();
            _health = GetComponent<PlayerHealth>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _class.OnValueChanged += OnClassChanged;
        }

        public override void OnNetworkDespawn()
        {
            _class.OnValueChanged -= OnClassChanged;
            base.OnNetworkDespawn();
        }

        void OnClassChanged(HeroClassId before, HeroClassId after) => Changed?.Invoke(after);

        /// <summary>
        /// Host only, right after spawning (every component is on the network by then): gives the hero their saved class,
        /// or makes them an Adventurer if it's unknown.
        /// </summary>
        public void ServerRestore(HeroClassId id)
        {
            if (!IsServer)
                return;
            if (classes == null || !classes.TryGet(id, out ClassDefinition definition))
            {
                id = HeroClassId.Adventurer;
                if (classes == null || !classes.TryGet(id, out definition))
                {
                    Debug.LogError("PlayerClass has no ClassBook with an Adventurer (Voxelforge > Build Abilities).", this);
                    return;
                }
            }
            Apply(definition);
        }

        /// <summary>Owner only: asks the host to make this hero a <paramref name="id"/>.</summary>
        public void RequestChoose(HeroClassId id)
        {
            if (IsOwner && id != _class.Value)
                ChooseRpc(id);
        }

        [Rpc(SendTo.Server)]
        void ChooseRpc(HeroClassId id, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || Time.time - _lastRequestTime < minRequestInterval)
                return;
            _lastRequestTime = Time.time;
            if (id != _class.Value && classes != null && classes.TryGet(id, out ClassDefinition definition))
                Apply(definition);
        }

        void Apply(ClassDefinition definition)
        {
            _actionBar.ServerSetLoadout(definition.Loadout);
            _caster.ServerSetPool(definition.MaxMana, definition.ManaRegen);
            if (_health != null)
                _health.ServerSetMaxHealth(definition.MaxHealth);
            _class.Value = definition.Id;
        }
    }
}
