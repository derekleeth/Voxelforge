using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Which ability sits in each of the nine action bar slots (Roadmap 4.4). Every hero starts with the default loadout;
    /// classes and talents (4.6) will hand out their own. Create one with Assets &gt; Create &gt; Voxelforge &gt;
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
