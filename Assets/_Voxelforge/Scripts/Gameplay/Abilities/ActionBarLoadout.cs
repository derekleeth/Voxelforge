using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Which ability sits in each of the nine action bar slots (Roadmap 4.4). Each class (4.6, <see cref="ClassDefinition"/>)
    /// has its own; talents will hand out more. Create one with Assets &gt; Create &gt; Voxelforge &gt;
    /// Action Bar Loadout.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Action Bar Loadout", fileName = "NewLoadout")]
    public sealed class ActionBarLoadout : ScriptableObject
    {
        [Tooltip("Slots 1-9 in order. Leave a slot empty for nothing.")]
        [SerializeField] AbilityDefinition[] slots = new AbilityDefinition[PlayerActionBar.SlotCount];

        /// <summary>The abilities as IDs, for <see cref="PlayerActionBar"/> to share.</summary>
        public ActionBarSlots ToSlots()
        {
            var result = new ActionBarSlots();
            for (int i = 0; i < PlayerActionBar.SlotCount && i < slots.Length; i++)
                result[i] = slots[i] != null ? slots[i].Id : AbilityIds.None;
            return result;
        }

        /// <summary>The ability in slot <paramref name="slot"/> (0-8), or null.</summary>
        public AbilityDefinition Get(int slot) => slot >= 0 && slot < slots.Length ? slots[slot] : null;

        void OnValidate()
        {
            if (slots.Length != PlayerActionBar.SlotCount)
                System.Array.Resize(ref slots, PlayerActionBar.SlotCount);
        }

        /// <summary>Builds a loadout in code, for tests and tools.</summary>
        internal static ActionBarLoadout Create(params AbilityDefinition[] abilities)
        {
            var loadout = CreateInstance<ActionBarLoadout>();
            loadout.slots = new AbilityDefinition[PlayerActionBar.SlotCount];
            for (int i = 0; i < abilities.Length && i < loadout.slots.Length; i++)
                loadout.slots[i] = abilities[i];
            return loadout;
        }
    }
}
