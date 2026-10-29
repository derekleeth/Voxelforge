using System.Collections.Generic;
using Unity.Mathematics;

namespace Voxelforge.Gameplay
{
    /// <summary>The inventory window's tabs (Roadmap 4.5): every slot, or only the items of one <see cref="ItemCategory"/>.</summary>
    public enum InventoryTab : byte
    {
        All,
        Blocks,
        Gear,
        Consumables,
        Materials,
    }

    /// <summary>
    /// What one player's inventory window shows and where its cursor is (Roadmap 4.5, D-028), owner-side only. The All
    /// tab is every bag slot in order, empty ones too, so things can be put anywhere; the other tabs list only that
    /// tab's items, in slot order. Below the grid is a row of the four bag sockets. Plain C# so tests can drive it.
    /// </summary>
    public sealed class InventoryModel
    {
        public const int Columns = 8;
        /// <summary>Exactly the starting bag (40 slots), so a new bag has no empty band; worn bags scroll.</summary>
        public const int VisibleRows = 5;
        public const int TabCount = 5;

        readonly List<int> _shown = new List<int>();

        public InventoryTab Tab { get; private set; }

        /// <summary>The bag slot shown in each cell of the grid, in order.</summary>
        public IReadOnlyList<int> Shown => _shown;

        /// <summary>The cell the cursor is on, or -1 when it's on the bag sockets (or the tab is empty).</summary>
        public int CursorCell { get; private set; }

        /// <summary>The bag socket the cursor is on, or -1 when it's in the grid.</summary>
        public int CursorSocket { get; private set; } = -1;

        /// <summary>The first grid row on screen.</summary>
        public int ScrollRow { get; private set; }

        public int Rows => (_shown.Count + Columns - 1) / Columns;

        /// <summary>The bag slot under the cursor, or -1.</summary>
        public int CursorSlot => CursorCell >= 0 && CursorCell < _shown.Count ? _shown[CursorCell] : -1;

        public static ItemCategory CategoryOf(InventoryTab tab) => tab switch
        {
            InventoryTab.Blocks => ItemCategory.Blocks,
            InventoryTab.Gear => ItemCategory.Gear,
            InventoryTab.Consumables => ItemCategory.Consumables,
            InventoryTab.Materials => ItemCategory.Materials,
            _ => ItemCategory.Auto,
        };

        /// <summary>True if the tab lists <paramref name="stack"/>.</summary>
        public static bool Shows(InventoryTab tab, ItemStack stack, IItemRules rules) =>
            tab == InventoryTab.All || (!stack.IsEmpty && rules.CategoryOf(stack.ItemId) == CategoryOf(tab));

        /// <summary>Works out the cells from the bag's slots. Call every frame the window is open; the cursor stays in range.</summary>
        public void Rebuild(IReadOnlyList<ItemStack> slots, IItemRules rules)
        {
            _shown.Clear();
            for (int i = 0; i < slots.Count; i++)
            {
                if (Shows(Tab, slots[i], rules))
                    _shown.Add(i);
            }
            if (CursorSocket < 0)
                CursorCell = _shown.Count == 0 ? -1 : math.clamp(CursorCell, 0, _shown.Count - 1);
            if (CursorSocket < 0 && CursorCell < 0)
                CursorSocket = 0; // Nothing in this tab: the sockets are all there is.
            FollowCursor();
        }

        public void SetTab(InventoryTab tab)
        {
            if (Tab == tab)
                return;
            Tab = tab;
            CursorCell = 0;
            CursorSocket = -1;
            ScrollRow = 0;
        }

        public void StepTab(int direction) => SetTab((InventoryTab)(((int)Tab + direction % TabCount + TabCount) % TabCount));

        /// <summary>Moves the cursor <paramref name="dx"/> columns and <paramref name="dy"/> rows (down is +), stopping at the edges.</summary>
        public void Move(int dx, int dy)
        {
            if (CursorSocket >= 0)
            {
                CursorSocket = math.clamp(CursorSocket + dx, 0, EquippedBags.SocketCount - 1);
                if (dy < 0 && _shown.Count > 0)
                {
                    int column = math.min(CursorSocket * 2, Columns - 1);
                    CursorCell = math.min((Rows - 1) * Columns + column, _shown.Count - 1);
                    CursorSocket = -1;
                }
                FollowCursor();
                return;
            }
            if (CursorCell < 0)
                return;

            int col = CursorCell % Columns;
            int row = CursorCell / Columns;
            if (dx != 0)
                CursorCell = math.min(row * Columns + math.clamp(col + dx, 0, Columns - 1), _shown.Count - 1);
            if (dy != 0)
            {
                int newRow = row + dy;
                if (newRow >= Rows)
                {
                    CursorSocket = math.min(col / 2, EquippedBags.SocketCount - 1);
                    CursorCell = -1;
                }
                else if (newRow >= 0)
                {
                    CursorCell = math.min(newRow * Columns + col, _shown.Count - 1);
                }
            }
            FollowCursor();
        }

        /// <summary>Puts the cursor on cell <paramref name="cell"/> (the mouse is over it).</summary>
        public void PointAtCell(int cell)
        {
            if (cell < 0 || cell >= _shown.Count)
                return;
            CursorCell = cell;
            CursorSocket = -1;
        }

        public void PointAtSocket(int socket)
        {
            if (socket < 0 || socket >= EquippedBags.SocketCount)
                return;
            CursorSocket = socket;
            CursorCell = -1;
        }

        /// <summary>Scrolls by whole rows (the mouse wheel over the grid).</summary>
        public void Scroll(int rows) => ScrollRow = math.clamp(ScrollRow + rows, 0, math.max(0, Rows - VisibleRows));

        void FollowCursor()
        {
            if (CursorCell >= 0)
            {
                int row = CursorCell / Columns;
                if (row < ScrollRow)
                    ScrollRow = row;
                else if (row >= ScrollRow + VisibleRows)
                    ScrollRow = row - VisibleRows + 1;
            }
            ScrollRow = math.clamp(ScrollRow, 0, math.max(0, Rows - VisibleRows));
        }
    }

    /// <summary>
    /// Turns a held stick into cursor steps (Roadmap 4.5): one step on the push, then a pause, then a steady repeat.
    /// Only the stronger axis counts, so a slightly diagonal push doesn't wander.
    /// </summary>
    public struct NavRepeat
    {
        public const float Deadzone = 0.5f;
        public const float FirstDelay = 0.35f;
        public const float RepeatDelay = 0.11f;

        int2 _direction;
        float _next;

        /// <summary>The step to take this frame: x right, y down. Zero most frames.</summary>
        public int2 Update(float2 stick, float now)
        {
            int2 direction = int2.zero;
            if (math.abs(stick.x) >= math.abs(stick.y))
            {
                if (math.abs(stick.x) > Deadzone)
                    direction.x = stick.x > 0f ? 1 : -1;
            }
            else if (math.abs(stick.y) > Deadzone)
            {
                direction.y = stick.y > 0f ? -1 : 1; // Stick up is a row up.
            }

            if (direction.Equals(int2.zero))
            {
                _direction = int2.zero;
                return int2.zero;
            }
            if (!direction.Equals(_direction))
            {
                _direction = direction;
                _next = now + FirstDelay;
                return direction;
            }
            if (now < _next)
                return int2.zero;
            _next = now + RepeatDelay;
            return direction;
        }
    }
}
