using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// This hero's potions and elixir (Roadmap 5.8, D-039). Drinking is an ability (<see cref="AbilityDelivery.Drink"/>):
    /// <see cref="PlayerCaster"/> decides when one goes off, from action bar slots 7 and 8 or from Use in the bag, and then
    /// <b>the host</b> takes the item out of the bag and gives the effect (CLAUDE.md Law #2). The host also keeps the two
    /// potion slots on the best potions owned. One elixir at a time: its stats add to worn gear's
    /// (<see cref="PlayerBlockBag.CombatStats"/>) until its time is up, and it's saved in the profile.
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(PlayerBlockBag), typeof(PlayerCaster))]
    public sealed class PlayerConsumables : NetworkBehaviour
    {
        readonly NetworkVariable<ElixirState> _elixir = new NetworkVariable<ElixirState>();
        readonly List<ItemStack> _stacks = new List<ItemStack>();

        PlayerController _player;
        PlayerBlockBag _bag;
        PlayerCaster _caster;
        PlayerHealth _health;
        PlayerActionBar _bar;
        AbilityDefinition _elixirAbility;
        float _elixirEndsAt;

        /// <summary>Every PC: the elixir the hero is under, or null.</summary>
        public AbilityDefinition Elixir => _elixirAbility;

        /// <summary>Every PC: what the elixir adds to the hero's stats.</summary>
        public GearStats ElixirStats => _elixirAbility != null ? _elixirAbility.Buff : default;

        /// <summary>Every PC: seconds until the elixir wears off, 0 with none.</summary>
        public float ElixirSecondsLeft => _elixirAbility != null ? Mathf.Max(0f, _elixirEndsAt - Time.time) : 0f;

        /// <summary>Every PC: the hero started or lost an elixir. The host uses it to update max health.</summary>
        public event Action ElixirChanged;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _bag = GetComponent<PlayerBlockBag>();
            _caster = GetComponent<PlayerCaster>();
            _health = GetComponent<PlayerHealth>();
            _bar = GetComponent<PlayerActionBar>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _elixir.OnValueChanged += OnElixirChanged;
            ApplyElixir(_elixir.Value);
            if (!IsServer)
                return;
            _bag.ServerContentsChanged += ServerRefreshBar;
            ServerRefreshBar();
        }

        public override void OnNetworkDespawn()
        {
            _elixir.OnValueChanged -= OnElixirChanged;
            _bag.ServerContentsChanged -= ServerRefreshBar;
            _elixirAbility = null;
            base.OnNetworkDespawn();
        }

        void OnElixirChanged(ElixirState before, ElixirState after)
        {
            ApplyElixir(after);
            ElixirChanged?.Invoke();
        }

        void ApplyElixir(ElixirState state)
        {
            _elixirAbility = null;
            AbilityBook book = _caster.Book;
            if (state.AbilityId != AbilityIds.None && book != null)
                book.TryGet(state.AbilityId, out _elixirAbility);
            _elixirEndsAt = Time.time + state.SecondsLeft;
        }

        void FixedUpdate()
        {
            if (IsServer && IsSpawned && _elixirAbility != null && Time.time >= _elixirEndsAt)
                _elixir.Value = default;
        }

        /// <summary>Every PC: how many of the item <paramref name="ability"/> drinks are in the bag.</summary>
        public int CountOf(AbilityDefinition ability)
        {
            ItemRegistry items = _bag.Items;
            return items != null && ability != null && items.TryGetDrinkItem(ability.Id, out ItemDefinition item) ? _bag.CountOf(item.Id) : 0;
        }

        /// <summary>Host only: whether the hero can drink <paramref name="ability"/> now, before the caster's own checks.</summary>
        public CastCheck ServerCheck(AbilityDefinition ability) =>
            ConsumableRules.Check(ability, CountOf(ability), _health != null ? _health.Health : 0f, _health != null ? _health.MaxHealth : 0f,
                _caster.Mana, _caster.MaxMana);

        /// <summary>Host only: <paramref name="ability"/> goes off: one item leaves the bag and its effect lands.</summary>
        public void ServerDrink(AbilityDefinition ability)
        {
            ItemRegistry items = _bag.Items;
            if (!IsServer || items == null || ability == null || !items.TryGetDrinkItem(ability.Id, out ItemDefinition item) ||
                !_bag.ServerTryTake(item.Id, 1))
                return;
            if (ability.HealAmount > 0f && _health != null)
                _health.ServerHeal(ability.HealAmount, _player.PlayerId, ability.Id);
            if (ability.ManaAmount > 0f)
                _caster.ServerAddMana(ability.ManaAmount);
            if (ConsumableRules.KindOf(ability) == DrinkKind.Elixir)
                _elixir.Value = new ElixirState { AbilityId = ability.Id, SecondsLeft = ability.Duration };
        }

        /// <summary>Host only: the elixir saved in the profile. Before the class is applied, so max health includes it.</summary>
        public void ServerRestoreElixir(ushort abilityId, float secondsLeft)
        {
            if (IsServer && abilityId != AbilityIds.None && secondsLeft > 0f)
                _elixir.Value = new ElixirState { AbilityId = abilityId, SecondsLeft = secondsLeft };
        }

        /// <summary>Owner only: Use on bag slot <paramref name="index"/>. The host checks what's really in it.</summary>
        public void RequestDrink(int index)
        {
            if (index >= 0 && index < _bag.SlotCount)
                DrinkRpc((byte)index);
        }

        [Rpc(SendTo.Server)]
        void DrinkRpc(byte index, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || index >= _bag.SlotCount || _player.IsKnockedOut)
                return;
            ItemRegistry items = _bag.Items;
            if (items != null && items.TryGetDrink(_bag.GetSlot(index).ItemId, out AbilityDefinition ability))
                _caster.ServerQueueDrink(ability);
        }

        void ServerRefreshBar()
        {
            ItemRegistry items = _bag.Items;
            if (_bar == null || items == null)
                return;
            _stacks.Clear();
            for (int i = 0; i < _bag.SlotCount; i++)
                _stacks.Add(_bag.GetSlot(i));
            AbilityDefinition health = ConsumableRules.BestPotion(items.Drinks, _stacks, DrinkKind.HealthPotion);
            AbilityDefinition mana = ConsumableRules.BestPotion(items.Drinks, _stacks, DrinkKind.ManaPotion);
            _bar.ServerSetPotions(health != null ? health.Id : AbilityIds.None, mana != null ? mana.Id : AbilityIds.None);
        }
    }
}
