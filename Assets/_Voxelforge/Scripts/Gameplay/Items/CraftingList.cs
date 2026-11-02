using System.Collections.Generic;
using Unity.Mathematics;

namespace Voxelforge.Gameplay
{
    /// <summary>The crafting window's tabs (Roadmap 5.8): recipes with no profession, then one tab for each profession.</summary>
    public enum CraftingTab : byte
    {
        General,
        Alchemy,
        Blacksmithing,
        Leatherworking,
    }

    /// <summary>
    /// What one player's crafting window lists and where its cursor is (Roadmap 5.8), owner-side only. Each tab keeps its
    /// own cursor and lists its recipes easiest first, whatever their place in the book (a place never changes, because
    /// it's sent over the network). Plain C# so tests can drive it.
    /// </summary>
    public sealed class CraftingList
    {
        public const int TabCount = 4;

        readonly IReadOnlyList<RecipeDefinition> _recipes;
        readonly List<int> _rows = new List<int>();
        readonly int[] _cursors = new int[TabCount];
        int _firstRow;

        public CraftingList(IReadOnlyList<RecipeDefinition> recipes)
        {
            _recipes = recipes;
            Fill();
        }

        public CraftingTab Tab { get; private set; }

        /// <summary>The recipe book index shown in each row, in order.</summary>
        public IReadOnlyList<int> Rows => _rows;

        public int CursorRow => _cursors[(int)Tab];

        /// <summary>The recipe book index under the cursor, or -1 when the tab is empty.</summary>
        public int CursorRecipe => _rows.Count > 0 ? _rows[CursorRow] : -1;

        public static CraftingTab TabOf(CraftStation station) => station switch
        {
            CraftStation.AlchemyTable => CraftingTab.Alchemy,
            CraftStation.Forge => CraftingTab.Blacksmithing,
            CraftStation.Anvil => CraftingTab.Blacksmithing,
            CraftStation.TanningRack => CraftingTab.Leatherworking,
            _ => CraftingTab.General,
        };

        /// <summary>The profession a tab's recipes raise. False for General: it has none.</summary>
        public static bool TryGetProfession(CraftingTab tab, out ProfessionId profession)
        {
            switch (tab)
            {
                case CraftingTab.Alchemy:
                    profession = ProfessionId.Alchemy;
                    return true;
                case CraftingTab.Blacksmithing:
                    profession = ProfessionId.Blacksmithing;
                    return true;
                case CraftingTab.Leatherworking:
                    profession = ProfessionId.Leatherworking;
                    return true;
                default:
                    profession = default;
                    return false;
            }
        }

        /// <summary>The window just opened: shows the nearest station's tab. With none nearby, the last tab stays.</summary>
        public void OpenAt(CraftStation nearest)
        {
            if (nearest != CraftStation.Anywhere)
                SetTab(TabOf(nearest));
        }

        public void SetTab(CraftingTab tab)
        {
            if (Tab == tab)
                return;
            Tab = tab;
            Fill();
        }

        public void StepTab(int direction) => SetTab((CraftingTab)(((int)Tab + direction % TabCount + TabCount) % TabCount));

        /// <summary>Moves the cursor <paramref name="rows"/> down the list (up if negative), wrapping round the ends.</summary>
        public void Move(int rows)
        {
            if (_rows.Count == 0)
                return;
            _cursors[(int)Tab] = (CursorRow + rows % _rows.Count + _rows.Count) % _rows.Count;
        }

        /// <summary>
        /// The first row on screen when <paramref name="visible"/> fit: it scrolls just enough to keep the cursor in sight.
        /// Kept here and not in the window, because split-screen players share one window object.
        /// </summary>
        public int FirstRow(int visible)
        {
            if (CursorRow < _firstRow)
                _firstRow = CursorRow;
            else if (CursorRow >= _firstRow + visible)
                _firstRow = CursorRow - visible + 1;
            _firstRow = math.clamp(_firstRow, 0, math.max(0, _rows.Count - visible));
            return _firstRow;
        }

        void Fill()
        {
            _rows.Clear();
            _firstRow = 0;
            for (int i = 0; i < _recipes.Count; i++)
            {
                RecipeDefinition recipe = _recipes[i];
                if (recipe == null || TabOf(recipe.Station) != Tab)
                    continue;
                // Keeps the list sorted by skill; recipes needing the same skill stay in book order.
                int at = _rows.Count;
                while (at > 0 && _recipes[_rows[at - 1]].RequiredSkill > recipe.RequiredSkill)
                    at--;
                _rows.Insert(at, i);
            }
            _cursors[(int)Tab] = math.clamp(CursorRow, 0, math.max(0, _rows.Count - 1));
        }
    }
}
