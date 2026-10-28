using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>Why a recipe can or can't be crafted right now (Roadmap 3.5).</summary>
    public enum CraftCheck : byte
    {
        Ok,
        /// <summary>There's no recipe with that number.</summary>
        UnknownRecipe,
        /// <summary>The bag is short of an input.</summary>
        MissingInputs,
        /// <summary>The recipe needs a workbench and none is in reach.</summary>
        NeedsWorkbench,
        /// <summary>The bag already holds as many of the output as it can.</summary>
        BagFull,
    }

    /// <summary>
    /// Every crafting recipe (Roadmap 3.5). A recipe's index in this list is the number sent in
    /// <c>PlayerCrafter.RequestCraftRpc</c>, so add new recipes at the end. Also holds the crafting rules shared by the
    /// owning PC (to grey out recipes) and the host (which decides).
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Recipe Book", fileName = "RecipeBook")]
    public sealed class RecipeBook : ScriptableObject
    {
        [SerializeField] List<RecipeDefinition> recipes = new List<RecipeDefinition>();

        public IReadOnlyList<RecipeDefinition> Recipes => recipes;

        public bool TryGet(int index, out RecipeDefinition recipe)
        {
            recipe = index >= 0 && index < recipes.Count ? recipes[index] : null;
            return recipe != null;
        }

        /// <summary>Can <paramref name="bag"/> craft recipe <paramref name="index"/>? Doesn't change anything.</summary>
        public CraftCheck Check(int index, IReadOnlyList<ItemStack> bag, bool nearWorkbench)
        {
            if (!TryGet(index, out RecipeDefinition recipe))
                return CraftCheck.UnknownRecipe;
            if (recipe.NeedsWorkbench && !nearWorkbench)
                return CraftCheck.NeedsWorkbench;

            IReadOnlyList<ItemStack> inputs = recipe.InputStacks();
            for (int i = 0; i < inputs.Count; i++)
            {
                if (CountOf(bag, inputs[i].ItemId) < TotalOf(inputs, inputs[i].ItemId))
                    return CraftCheck.MissingInputs;
            }
            ItemStack output = recipe.Output.ToStack();
            if (CountOf(bag, output.ItemId) - TotalOf(inputs, output.ItemId) + output.Count > ItemStacks.MaxPerItem)
                return CraftCheck.BagFull;
            return CraftCheck.Ok;
        }

        /// <summary>
        /// True if a <paramref name="blockId"/> block is within <paramref name="radius"/> blocks of
        /// <paramref name="center"/> (a box, measured block to block), e.g. a workbench to craft at.
        /// </summary>
        public static bool IsNearBlock(VoxelWorld world, float3 center, ushort blockId, int radius)
        {
            if (world == null)
                return false;
            var middle = (int3)math.floor(center);
            for (int y = -radius; y <= radius; y++)
            for (int z = -radius; z <= radius; z++)
            for (int x = -radius; x <= radius; x++)
            {
                int3 cell = middle + new int3(x, y, z);
                if (world.TryGetChunk(VoxelMath.WorldToChunk(cell), out _) && world.GetVoxel(cell).BlockId == blockId)
                    return true;
            }
            return false;
        }

        /// <summary>Returns every problem with the book. An empty list means it's valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeDefinition recipe = recipes[i];
                if (recipe == null)
                {
                    errors.Add($"Recipe {i} is empty.");
                    continue;
                }
                if (recipe.Output.ItemId == BlockIds.Air || recipe.Output.count <= 0)
                    errors.Add($"'{recipe.name}' has no output.");
                if (recipe.Inputs.Count == 0)
                    errors.Add($"'{recipe.name}' has no inputs.");
                foreach (ItemAmount input in recipe.Inputs)
                {
                    if (input.ItemId == BlockIds.Air || input.count <= 0)
                        errors.Add($"'{recipe.name}' has an empty input.");
                }
            }
            return errors;
        }

        static int CountOf(IReadOnlyList<ItemStack> stacks, ushort itemId)
        {
            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].ItemId == itemId)
                    return stacks[i].Count;
            }
            return 0;
        }

        static int TotalOf(IReadOnlyList<ItemStack> stacks, ushort itemId)
        {
            int total = 0;
            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].ItemId == itemId)
                    total += stacks[i].Count;
            }
            return total;
        }

        /// <summary>Builds a book in code, for tests and tools.</summary>
        internal static RecipeBook Create(IEnumerable<RecipeDefinition> definitions)
        {
            var book = CreateInstance<RecipeBook>();
            book.recipes = new List<RecipeDefinition>(definitions);
            return book;
        }
    }
}
