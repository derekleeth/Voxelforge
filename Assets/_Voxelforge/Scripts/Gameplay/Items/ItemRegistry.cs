using System.Collections.Generic;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Every <see cref="ItemDefinition"/> in the game (Roadmap 3.5). Blocks are items too and need no entry here,
    /// unless they want a model when held or dropped (prop blocks such as torches).
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Item Registry", fileName = "ItemRegistry")]
    public sealed class ItemRegistry : ScriptableObject, IItemRules
    {
        [SerializeField] List<ItemDefinition> items = new List<ItemDefinition>();

        Dictionary<ushort, ItemDefinition> _byId;
        Dictionary<ushort, ItemDefinition> _byDrink;
        List<ItemDefinition> _drinks;

        public IReadOnlyList<ItemDefinition> Items => items;

        public bool TryGet(ushort id, out ItemDefinition item)
        {
            if (_byId == null)
            {
                _byId = new Dictionary<ushort, ItemDefinition>();
                foreach (ItemDefinition definition in items)
                {
                    if (definition != null)
                        _byId.TryAdd(definition.Id, definition);
                }
            }
            return _byId.TryGetValue(id, out item);
        }

        /// <summary>What the item multiplies mining power by while held. Bare hands for blocks and unknown IDs.</summary>
        public ToolPower ToolPowerOf(ushort id) => TryGet(id, out ItemDefinition item) ? item.ToolPower : ToolPower.Hands;

        /// <summary>What Mine casts while the item is held, if it's ranged (the M.G.R.S.'s Mana Bolt, Roadmap 4.1).</summary>
        public bool TryGetAbility(ushort id, out AbilityDefinition ability)
        {
            ability = TryGet(id, out ItemDefinition item) ? item.Ability : null;
            return ability != null;
        }

        /// <summary>What drinking the item casts, if it's a potion or elixir (Roadmap 5.8).</summary>
        public bool TryGetDrink(ushort id, out AbilityDefinition ability)
        {
            ability = TryGet(id, out ItemDefinition item) ? item.Drink : null;
            return ability != null;
        }

        /// <summary>Which item the Drink ability <paramref name="abilityId"/> uses up.</summary>
        public bool TryGetDrinkItem(ushort abilityId, out ItemDefinition item)
        {
            if (_byDrink == null)
                BuildDrinks();
            return _byDrink.TryGetValue(abilityId, out item);
        }

        /// <summary>Every potion and elixir.</summary>
        public IReadOnlyList<ItemDefinition> Drinks
        {
            get
            {
                if (_drinks == null)
                    BuildDrinks();
                return _drinks;
            }
        }

        void BuildDrinks()
        {
            _byDrink = new Dictionary<ushort, ItemDefinition>();
            _drinks = new List<ItemDefinition>();
            foreach (ItemDefinition definition in items)
            {
                if (definition == null || definition.Drink == null)
                    continue;
                _drinks.Add(definition);
                _byDrink.TryAdd(definition.Drink.Id, definition);
            }
        }

        public ItemCategory CategoryOf(ushort id) => TryGet(id, out ItemDefinition item) ? item.Category : ItemRules.DefaultCategory(id);

        public int MaxStackOf(ushort id) =>
            TryGet(id, out ItemDefinition item) ? item.MaxStack : ItemRules.DefaultMaxStack(ItemRules.DefaultCategory(id));

        public int BagSlotsOf(ushort id) => TryGet(id, out ItemDefinition item) ? item.BagSlots : 0;

        public ItemRarity RarityOf(ushort id) => TryGet(id, out ItemDefinition item) ? item.Rarity : ItemRarity.Common;

        public EquipSlot EquipSlotOf(ushort id) => TryGet(id, out ItemDefinition item) ? item.EquipSlot : EquipSlot.None;

        public GearStats StatsOf(ushort id) => TryGet(id, out ItemDefinition item) ? item.Stats : default;

        /// <summary>The item's name: its definition's, else its block's, else "Item 1234".</summary>
        public string DisplayName(ushort id, BlockRegistry blocks)
        {
            if (TryGet(id, out ItemDefinition item))
                return item.DisplayName;
            if (blocks != null && ItemIds.IsBlock(id) && blocks.TryGet(id, out BlockDefinition block))
                return block.DisplayName;
            return $"Item {id}";
        }

        /// <summary>Returns every problem with the registry. An empty list means it's valid.</summary>
        public List<string> Validate(BlockRegistry blocks)
        {
            var errors = new List<string>();
            var seen = new HashSet<ushort>();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDefinition item = items[i];
                if (item == null)
                {
                    errors.Add($"Entry {i} is empty.");
                    continue;
                }
                if (!seen.Add(item.Id))
                    errors.Add($"'{item.DisplayName}' uses ID {item.Id}, which another item already uses.");
                if (ItemIds.IsBlock(item.Id) && blocks != null && !blocks.TryGet(item.Id, out _))
                    errors.Add($"'{item.DisplayName}' uses ID {item.Id}, below {ItemIds.FirstNonBlock}, but no block has it.");
                if (!ItemIds.IsBlock(item.Id) && (item.Mesh == null || item.Material == null))
                    errors.Add($"'{item.DisplayName}' needs a mesh and a material.");
                if (item.IsBag && (ItemIds.IsBlock(item.Id) || item.MaxStack != 1))
                    errors.Add($"'{item.DisplayName}' is a bag, so it must be a one-per-slot item, not a block.");
                if (item.IsWornGear)
                {
                    if (ItemIds.IsBlock(item.Id) || item.MaxStack != 1 || item.Category != ItemCategory.Gear)
                        errors.Add($"'{item.DisplayName}' is worn gear, so it must be a one-per-slot Gear item, not a block.");
                    if (item.IsBag || item.IsTool)
                        errors.Add($"'{item.DisplayName}' is worn gear, so it can't also be a bag or a tool.");
                    if (item.IsRanged && item.EquipSlot != EquipSlot.MainHand)
                        errors.Add($"'{item.DisplayName}' shoots, so it must be a Main Hand weapon.");
                    if (item.EquipSlot == EquipSlot.MainHand && !item.IsRanged && !item.Weapon.IsMelee)
                        errors.Add($"'{item.DisplayName}' is a Main Hand weapon that neither swings (no damage) nor shoots.");
                    if (item.Stats.IsZero)
                        errors.Add($"'{item.DisplayName}' is worn gear with no stats.");
                }
                if (item.Drink != null)
                {
                    if (item.Category != ItemCategory.Consumables)
                        errors.Add($"'{item.DisplayName}' is drunk, so it must be in the Consumables category.");
                    if (item.Drink.Delivery != AbilityDelivery.Drink)
                        errors.Add($"'{item.DisplayName}' is drunk, but its ability '{item.Drink.DisplayName}' isn't a Drink.");
                    foreach (string problem in AbilityBook.Check(item.Drink))
                        errors.Add($"'{item.DisplayName}' is drunk: {problem}");
                }
                if (item.Ability != null)
                {
                    foreach (string problem in AbilityBook.Check(item.Ability))
                        errors.Add($"'{item.DisplayName}' shoots: {problem}");
                }
            }
            // Blocks that drop an item (ore, Roadmap 5.7) need it to exist.
            if (blocks != null)
            {
                foreach (BlockDefinition block in blocks.Blocks)
                {
                    if (block != null && block.DropCount > 0 && !ItemIds.IsBlock(block.DropId) && !seen.Contains(block.DropId))
                        errors.Add($"'{block.DisplayName}' drops item {block.DropId}, which isn't in the item registry.");
                }
            }
            return errors;
        }

        void OnValidate()
        {
            _byId = null;
            _byDrink = null;
            _drinks = null;
        }

        /// <summary>Builds a registry in code, for tests and tools.</summary>
        internal static ItemRegistry Create(IEnumerable<ItemDefinition> definitions)
        {
            var registry = CreateInstance<ItemRegistry>();
            registry.items = new List<ItemDefinition>(definitions);
            return registry;
        }
    }
}
