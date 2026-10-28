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
    /// One crafting recipe (Roadmap 3.5): inputs from the bag become an output. Most need a workbench nearby; a few
    /// (the workbench itself) can be made anywhere. 5.6 adds other stations and skill levels. Create one with
    /// Assets &gt; Create &gt; Voxelforge &gt; Recipe, then add it to the <see cref="RecipeBook"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Recipe", fileName = "NewRecipe")]
    public sealed class RecipeDefinition : ScriptableObject
    {
        [SerializeField] List<ItemAmount> inputs = new List<ItemAmount>();

        [SerializeField] ItemAmount output;

        [Tooltip("Only made within reach of a Workbench block. Off = can be made anywhere.")]
        [SerializeField] bool needsWorkbench = true;

        readonly List<ItemStack> _inputStacks = new List<ItemStack>();

        public IReadOnlyList<ItemAmount> Inputs => inputs;
        public ItemAmount Output => output;
        public bool NeedsWorkbench => needsWorkbench;

        /// <summary>The inputs as stacks, for <see cref="ItemStacks.TryExchange"/>. Reused list: don't keep it.</summary>
        public IReadOnlyList<ItemStack> InputStacks()
        {
            _inputStacks.Clear();
            foreach (ItemAmount input in inputs)
                _inputStacks.Add(input.ToStack());
            return _inputStacks;
        }

        /// <summary>Builds a recipe in code, for tests and tools.</summary>
        internal static RecipeDefinition Create(ItemAmount output, bool needsWorkbench, params ItemAmount[] inputs)
        {
            var recipe = CreateInstance<RecipeDefinition>();
            recipe.output = output;
            recipe.needsWorkbench = needsWorkbench;
            recipe.inputs = new List<ItemAmount>(inputs);
            return recipe;
        }
    }
}
