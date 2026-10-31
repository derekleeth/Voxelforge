using Unity.Netcode;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// This hero's nine action bar slots (Roadmap 4.4). <b>The host</b> fills them from the loadout and shares them with
    /// every PC, so the action bar in this player's view shows what the host will really cast. Pressing a slot's button
    /// sends nothing new: the host's <see cref="PlayerCaster"/> reads the press from the input frame and casts the slot
    /// it agrees on (CLAUDE.md Law #2).
    /// </summary>
    [RequireComponent(typeof(PlayerCaster))]
    public sealed class PlayerActionBar : NetworkBehaviour
    {
        public const int SlotCount = 9;

        [Tooltip("Data/Abilities/DefaultLoadout.asset: what every hero starts with. Classes (4.6) will bring their own.")]
        [SerializeField] ActionBarLoadout defaultLoadout;

        readonly NetworkVariable<ActionBarSlots> _slots = new NetworkVariable<ActionBarSlots>();

        PlayerCaster _caster;

        void Awake() => _caster = GetComponent<PlayerCaster>();

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer && defaultLoadout != null)
                _slots.Value = defaultLoadout.ToSlots();
        }

        /// <summary>
        /// Host only: fills the bar from <paramref name="loadout"/> (a class's spells, Roadmap 4.6), with
        /// <paramref name="race"/>'s racial ability in slot 9 (Roadmap 4.7).
        /// </summary>
        public void ServerSetLoadout(ActionBarLoadout loadout, RaceDefinition race = null)
        {
            if (IsServer && loadout != null)
                _slots.Value = RaceRules.Bar(loadout, race);
        }

        /// <summary>Every PC: the ability in slot <paramref name="slot"/> (0-8), as the host shared it.</summary>
        public bool TryGet(int slot, out AbilityDefinition ability)
        {
            ability = null;
            if (slot < 0 || slot >= SlotCount)
                return false;
            ushort id = _slots.Value[slot];
            AbilityBook book = _caster != null ? _caster.Book : null;
            return id != AbilityIds.None && book != null && book.TryGet(id, out ability);
        }
    }
}
