using System;
using System.Collections.Generic;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>Some number of one block or item, as set up in a recipe. Set either the block or the item.</summary>
    [Serializable]
    public struct ItemAmount
    {
        public BlockDefinition block;
        public ItemDefinition item;
        [Min(1)] public int count;

        public ItemAmount(BlockDefinition block, int count)
        {
            this.block = block;
            item = null;
            this.count = count;
        }

        public ItemAmount(ItemDefinition item, int count)
        {
            block = null;
            this.item = item;
            this.count = count;
        }

        /// <summary>The item ID: the item's, else the block's, else <see cref="BlockIds.Air"/> (not set).</summary>
        public ushort ItemId => item != null ? item.Id : block != null ? block.Id : BlockIds.Air;

        public ItemStack ToStack() => new ItemStack(ItemId, (ushort)Mathf.Clamp(count, 0, ushort.MaxValue));
    }

    /// <summary>
    /// One crafting recipe (Roadmap 3.5): inputs from the bag become an output. Most need a station nearby; a few (the
    /// workbench itself) can be made anywhere. A profession's station (Roadmap 5.8) also asks for a skill level. Create
    /// one with Assets &gt; Create &gt; Voxelforge &gt; Recipe, then add it to the <see cref="RecipeBook"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Recipe", fileName = "NewRecipe")]
    public sealed class RecipeDefinition : ScriptableObject
    {
        [SerializeField] List<ItemAmount> inputs = new List<ItemAmount>();

        [SerializeField] ItemAmount output;

        [Tooltip("The block that must be within reach. It also picks the profession: Alchemy Table, Forge and Anvil, " +
                 "Tanning Rack. Anywhere and Workbench recipes belong to no profession.")]
        [SerializeField] CraftStation station = CraftStation.Workbench;

        [Tooltip("The skill needed in the station's profession. Crafting raises the skill the way gathering does: often " +
                 "at first, then never once it's far above this (ProfessionRules).")]
        [SerializeField, Range(1, 255)] int requiredSkill = 1;

        readonly List<ItemStack> _inputStacks = new List<ItemStack>();

        public IReadOnlyList<ItemAmount> Inputs => inputs;
        public ItemAmount Output => output;
        public CraftStation Station => station;
        public int RequiredSkill => requiredSkill;

        /// <summary>The inputs as stacks, for <see cref="ItemStacks.TryExchange"/>. Reused list: don't keep it.</summary>
        public IReadOnlyList<ItemStack> InputStacks()
        {
            _inputStacks.Clear();
            foreach (ItemAmount input in inputs)
                _inputStacks.Add(input.ToStack());
            return _inputStacks;
        }

        /// <summary>Builds a recipe in code, for tests and tools.</summary>
        internal static RecipeDefinition Create(ItemAmount output, CraftStation station, params ItemAmount[] inputs) =>
            Create(output, station, 1, inputs);

        /// <summary>Builds a recipe that needs a skill in code, for tests and tools.</summary>
        internal static RecipeDefinition Create(ItemAmount output, CraftStation station, int requiredSkill, params ItemAmount[] inputs)
        {
            var recipe = CreateInstance<RecipeDefinition>();
            recipe.output = output;
            recipe.station = station;
            recipe.requiredSkill = requiredSkill;
            recipe.inputs = new List<ItemAmount>(inputs);
            return recipe;
        }
    }
}
