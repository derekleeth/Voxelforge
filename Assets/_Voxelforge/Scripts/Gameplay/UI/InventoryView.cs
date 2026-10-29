using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// One player's inventory window (Roadmap 4.5, D-028): a parchment comic-book frame with a title and capacity, five
    /// tabs, an 8-wide grid of slots that scrolls, the four bag sockets, the name of the item under the cursor and the
    /// buttons for this player's controls. <see cref="PlayerHudPanel"/> keeps it inside that player's own view (CLAUDE.md
    /// Law #5). It only shows what <see cref="PlayerInventory"/> and the synced bag say; the mouse player can also point
    /// and click, which goes through the same <see cref="PlayerInventory"/> requests.
    /// </summary>
    sealed class InventoryView
    {
        // Sizes in pixels at 1080p. Roomy on purpose: small comic lettering with an ink outline smears (play-test, 4.5).
        const float Pad = 22f;
        const float Slot = 62f;
        const float Gap = 8f;
        const float TitleHeight = 44f;
        const float TabHeight = 40f;
        const float SocketSize = 52f;
        const float InfoHeight = 34f;
        const float HintHeight = 50f;
        const float SectionGap = 12f;

        static readonly string[] TabNames = { "All", "Blocks", "Gear", "Consumables", "Materials" };
        static readonly Color Clear = new Color(1f, 1f, 1f, 0f);
        static readonly Color InkBrown = new Color(0.25f, 0.13f, 0.06f);
        static readonly Color TabIdle = new Color(0.55f, 0.42f, 0.3f);

        sealed class Cell
        {
            public RectTransform Root;
            public RawImage Icon;
            public TextMeshProUGUI Count;
            public Image Glow;
            public ItemStack Shown = new ItemStack(ushort.MaxValue, 0);
            public bool HasIcon;
        }

        readonly HudSkin _skin;
        readonly Func<ushort, Texture> _icon;
        readonly Func<ushort, string> _itemName;
        readonly Cell[] _cells = new Cell[InventoryModel.Columns * InventoryModel.VisibleRows];
        readonly Cell[] _sockets = new Cell[EquippedBags.SocketCount];
        readonly Image[] _tabBacks = new Image[InventoryModel.TabCount];
        readonly TextMeshProUGUI[] _tabLabels = new TextMeshProUGUI[InventoryModel.TabCount];
        readonly TextMeshProUGUI _capacity;
        readonly TextMeshProUGUI _info;
        readonly TextMeshProUGUI _hints;
        readonly TextMeshProUGUI _scrollUp;
        readonly TextMeshProUGUI _scrollDown;
        readonly RectTransform _grid;
        string _scheme;
        int _usedShown = -1;
        int _capacityShown = -1;
        int _infoSlot = -2;
        ItemStack _infoStack;
        string _infoMessage;

        public InventoryView(RectTransform parent, HudSkin skin, Func<ushort, Texture> icon, Func<ushort, string> itemName)
        {
            _skin = skin;
            _icon = icon;
            _itemName = itemName;

            float gridWidth = InventoryModel.Columns * Slot + (InventoryModel.Columns - 1) * Gap;
            float gridHeight = InventoryModel.VisibleRows * Slot + (InventoryModel.VisibleRows - 1) * Gap;
            Width = gridWidth + Pad * 2f;
            Height = Pad + TitleHeight + SectionGap * 0.5f + TabHeight + SectionGap + gridHeight + SectionGap + SocketSize +
                     SectionGap + InfoHeight + SectionGap * 0.5f + HintHeight + Pad;

            Root = HudWidgets.Place(HudWidgets.Rect("Inventory", parent), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(Width, Height));
            HudWidgets.Stretch(HudWidgets.Image("Parchment", Root, skin.panel, Color.white, Image.Type.Sliced).rectTransform);

            float y = -Pad;
            TextMeshProUGUI title = HudWidgets.Label("Title", Root, skin.titleFont, 36f, InkBrown, TextAlignmentOptions.Left);
            Row(title.rectTransform, Pad, y, gridWidth, TitleHeight);
            title.text = "Bag";
            // Dark ink lettering on the parchment: sharp at any size, unlike the white outlined comic style.
            _capacity = HudWidgets.Label("Capacity", Root, skin.titleFont, 28f, InkBrown, TextAlignmentOptions.Right);
            Row(_capacity.rectTransform, Pad, y, gridWidth, TitleHeight);
            y -= TitleHeight + SectionGap * 0.5f;

            float tabWidth = (gridWidth - Gap * (InventoryModel.TabCount - 1)) / InventoryModel.TabCount;
            for (int i = 0; i < InventoryModel.TabCount; i++)
            {
                RectTransform tab = Row(HudWidgets.Rect(TabNames[i], Root), Pad + i * (tabWidth + Gap), y, tabWidth, TabHeight);
                _tabBacks[i] = HudWidgets.Image("Back", tab, skin.barFill, TabIdle);
                HudWidgets.Stretch(_tabBacks[i].rectTransform, 2f);
                HudWidgets.Stretch(HudWidgets.Image("Frame", tab, skin.barFrame, Color.white, Image.Type.Sliced).rectTransform, -2f);
                _tabLabels[i] = HudWidgets.SmallLabel("Label", tab, skin, 22f, Color.white, TextAlignmentOptions.Center);
                FitInside(_tabLabels[i], 14f, 22f);
                HudWidgets.Stretch(_tabLabels[i].rectTransform, 6f);
                _tabLabels[i].text = TabNames[i];
            }
            y -= TabHeight + SectionGap;

            _grid = Row(HudWidgets.Rect("Grid", Root), Pad, y, gridWidth, gridHeight);
            for (int i = 0; i < _cells.Length; i++)
            {
                int col = i % InventoryModel.Columns;
                int row = i / InventoryModel.Columns;
                _cells[i] = MakeCell($"Cell {i}", _grid, col * (Slot + Gap), -row * (Slot + Gap), Slot);
            }
            // Scroll arrows sit just outside the grid's right edge, in the frame's margin.
            _scrollUp = HudWidgets.SmallLabel("More Above", Root, skin, 20f, InkBrown, TextAlignmentOptions.Center);
            Row(_scrollUp.rectTransform, Pad + gridWidth + 1f, y, Pad - 2f, 24f);
            _scrollUp.text = "▲";
            y -= gridHeight + SectionGap;
            _scrollDown = HudWidgets.SmallLabel("More Below", Root, skin, 20f, InkBrown, TextAlignmentOptions.Center);
            Row(_scrollDown.rectTransform, Pad + gridWidth + 1f, y + SectionGap + 24f, Pad - 2f, 24f);
            _scrollDown.text = "▼";

            TextMeshProUGUI bagsLabel = HudWidgets.Label("Bags", Root, skin.titleFont, 26f, InkBrown, TextAlignmentOptions.Left);
            Row(bagsLabel.rectTransform, Pad, y, 150f, SocketSize);
            bagsLabel.text = "Worn bags";
            for (int i = 0; i < _sockets.Length; i++)
                _sockets[i] = MakeCell($"Socket {i + 1}", Root, Pad + 160f + i * (SocketSize + Gap), y, SocketSize);
            y -= SocketSize + SectionGap;

            _info = HudWidgets.Label("Info", Root, skin.titleFont, 26f, InkBrown, TextAlignmentOptions.Left);
            FitInside(_info, 16f, 26f);
            Row(_info.rectTransform, Pad, y, gridWidth, InfoHeight);
            y -= InfoHeight + SectionGap * 0.5f;
            _hints = HudWidgets.Label("Hints", Root, skin.titleFont, 20f, InkBrown, TextAlignmentOptions.TopLeft);
            FitInside(_hints, 13f, 20f);
            _hints.textWrappingMode = TextWrappingModes.Normal;
            Row(_hints.rectTransform, Pad, y, gridWidth, HintHeight);
        }

        /// <summary>Shrinks the text to stay inside its box rather than spill over the frame.</summary>
        static void FitInside(TextMeshProUGUI label, float min, float max)
        {
            label.enableAutoSizing = true;
            label.fontSizeMin = min;
            label.fontSizeMax = max;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        public RectTransform Root { get; }

        /// <summary>Size in 1080p pixels.</summary>
        public float Width { get; }
        public float Height { get; }

        static RectTransform Row(RectTransform rect, float x, float y, float width, float height) =>
            HudWidgets.Place(rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(width, height));

        Cell MakeCell(string name, RectTransform parent, float x, float y, float size)
        {
            var c = new Cell { Root = Row(HudWidgets.Rect(name, parent), x, y, size, size) };
            HudWidgets.Stretch(HudWidgets.Image("Back", c.Root, _skin.slotBack, Color.white).rectTransform, 3f);
            c.Icon = HudWidgets.Rect("Icon", c.Root).gameObject.AddComponent<RawImage>();
            c.Icon.raycastTarget = false;
            c.Icon.color = Clear;
            HudWidgets.Stretch(c.Icon.rectTransform, 5f);
            HudWidgets.Stretch(HudWidgets.Image("Frame", c.Root, _skin.slotFrame, Color.white).rectTransform);
            c.Glow = HudWidgets.Image("Glow", c.Root, _skin.slotFlash, Clear);
            HudWidgets.Stretch(c.Glow.rectTransform, -size * 0.18f);
            c.Count = HudWidgets.SmallLabel("Count", c.Root, _skin, size * 0.4f, Color.white, TextAlignmentOptions.BottomRight);
            HudWidgets.Stretch(c.Count.rectTransform, 4f);
            return c;
        }

        public void SetScheme(string scheme)
        {
            if (_scheme == scheme)
                return;
            _scheme = scheme;
            _hints.text = scheme switch
            {
                "Gamepad" => Hints(("A", "Pick up / Put"), ("X", "Use / Wear"), ("View", "Sort"), null,
                    ("Stick", "Move"), ("LB RB", "Tabs"), ("B", "Close")),
                "KeyboardRight" => Hints(("RCtrl", "Pick up / Put"), (";", "Use / Wear"), ("[", "Sort"), null,
                    ("Arrows", "Move"), (". /", "Tabs"), ("Backspace", "Close")),
                "KeyboardLeft" => Hints(("Space", "Pick up / Put"), ("F", "Use / Wear"), ("H", "Sort"), null,
                    ("WASD", "Move"), ("Z C  1-5", "Tabs"), ("Tab", "Close")),
                _ => Hints(("Click", "Pick up / Put"), ("Right-click", "Use / Wear"), ("H", "Sort"), null,
                    ("1-5", "Tabs"), ("Wheel", "Scroll"), ("Tab", "Close")),
            };
        }

        /// <summary>Two lines of "key action" pairs; null starts the second line. Keys are in a darker red-brown.</summary>
        static string Hints(params (string key, string action)?[] pairs)
        {
            var text = new System.Text.StringBuilder();
            bool lineStart = true;
            foreach ((string key, string action)? pair in pairs)
            {
                if (pair == null)
                {
                    text.Append('\n');
                    lineStart = true;
                    continue;
                }
                if (!lineStart)
                    text.Append("      ");
                text.Append("<color=#8A2A10>").Append(pair.Value.key).Append("</color>  ").Append(pair.Value.action);
                lineStart = false;
            }
            return text.ToString();
        }

        public void Tick(PlayerInventory inventory, Color teamColor, float now)
        {
            InventoryModel model = inventory.Model;
            PlayerBlockBag bag = inventory.Bag;
            var slots = inventory.Slots;
            IItemRules rules = bag.Rules;

            if (inventory.Scheme == "KeyboardMouse")
                HandleMouse(inventory);

            for (int i = 0; i < InventoryModel.TabCount; i++)
            {
                bool active = (int)model.Tab == i;
                HudWidgets.SetColor(_tabBacks[i], active ? teamColor : TabIdle);
            }

            int used = 0;
            foreach (ItemStack stack in slots)
            {
                if (!stack.IsEmpty)
                    used++;
            }
            if (used != _usedShown || slots.Count != _capacityShown)
            {
                _usedShown = used;
                _capacityShown = slots.Count;
                _capacity.SetText("{0} / {1}", (float)used, (float)slots.Count);
            }

            float pulse = 0.55f + 0.35f * Mathf.Sin(now * 6f);
            int first = model.ScrollRow * InventoryModel.Columns;
            for (int i = 0; i < _cells.Length; i++)
            {
                int cell = first + i;
                Cell c = _cells[i];
                bool exists = cell < model.Shown.Count;
                HudWidgets.SetActive(c.Root, exists);
                if (!exists)
                    continue;
                int slot = model.Shown[cell];
                Show(c, slots[slot]);
                Color glow = Clear;
                if (slot == inventory.HeldSlot)
                    glow = new Color(_skin.pickup.r, _skin.pickup.g, _skin.pickup.b, 0.9f);
                if (cell == model.CursorCell)
                    glow = new Color(teamColor.r, teamColor.g, teamColor.b, pulse);
                HudWidgets.SetColor(c.Glow, glow);
            }
            HudWidgets.SetActive(_scrollUp, model.ScrollRow > 0);
            HudWidgets.SetActive(_scrollDown, model.ScrollRow + InventoryModel.VisibleRows < model.Rows);

            EquippedBags bags = bag.Bags;
            for (int i = 0; i < _sockets.Length; i++)
            {
                Show(_sockets[i], bags[i] != 0 ? new ItemStack(bags[i], 1) : ItemStack.Empty);
                HudWidgets.SetColor(_sockets[i].Glow,
                    model.CursorSocket == i ? new Color(teamColor.r, teamColor.g, teamColor.b, pulse) : Clear);
            }

            UpdateInfo(inventory, rules, bags);
        }

        void Show(Cell c, ItemStack stack)
        {
            if (!c.Shown.Equals(stack))
            {
                bool sameItem = c.Shown.ItemId == stack.ItemId;
                c.Shown = stack;
                if (!sameItem)
                    c.HasIcon = false;
                if (stack.Count > 1)
                    c.Count.SetText("{0}", (float)stack.Count);
                else
                    c.Count.SetText(string.Empty);
            }
            if (stack.IsEmpty)
            {
                c.Icon.texture = null;
                HudWidgets.SetColor(c.Icon, Clear);
                return;
            }
            // Pictures are drawn on demand and may be a frame or two late.
            if (!c.HasIcon)
            {
                Texture texture = _icon?.Invoke(stack.ItemId);
                c.Icon.texture = texture;
                c.HasIcon = texture != null;
            }
            HudWidgets.SetColor(c.Icon, c.HasIcon ? Color.white : Clear);
        }

        void UpdateInfo(PlayerInventory inventory, IItemRules rules, EquippedBags bags)
        {
            InventoryModel model = inventory.Model;
            string message = inventory.Message;
            int key = model.CursorSocket >= 0 ? -10 - model.CursorSocket : model.CursorSlot;
            ItemStack stack = model.CursorSocket >= 0
                ? (bags[model.CursorSocket] != 0 ? new ItemStack(bags[model.CursorSocket], 1) : ItemStack.Empty)
                : key >= 0 ? inventory.Slots[key] : ItemStack.Empty;
            if (key == _infoSlot && stack.Equals(_infoStack) && message == _infoMessage)
                return;
            _infoSlot = key;
            _infoStack = stack;
            _infoMessage = message;

            string text;
            if (stack.IsEmpty)
                text = model.CursorSocket >= 0 ? "An empty bag socket: wear a bag here for more slots." : "";
            else
            {
                string name = _itemName?.Invoke(stack.ItemId) ?? $"Item {stack.ItemId}";
                int bagSlots = rules.BagSlotsOf(stack.ItemId);
                string kind = bagSlots > 0 ? $"Bag, +{bagSlots} slots" : rules.CategoryOf(stack.ItemId).ToString();
                text = model.CursorSocket >= 0 ? $"{name}  ({kind}) - Use to take off" : $"{name}  ({kind})";
            }
            if (!string.IsNullOrEmpty(message))
                text = string.IsNullOrEmpty(text) ? message : $"{text}   {message}";
            _info.text = text;
        }

        /// <summary>The keyboard + mouse player points and clicks; the cursor is free while their window is open.</summary>
        void HandleMouse(PlayerInventory inventory)
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;
            Vector2 point = mouse.position.ReadValue();
            InventoryModel model = inventory.Model;
            bool over = false;
            int first = model.ScrollRow * InventoryModel.Columns;
            for (int i = 0; i < _cells.Length && !over; i++)
            {
                if (_cells[i].Root.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(_cells[i].Root, point, null))
                {
                    model.PointAtCell(first + i);
                    over = true;
                }
            }
            for (int i = 0; i < _sockets.Length && !over; i++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(_sockets[i].Root, point, null))
                {
                    model.PointAtSocket(i);
                    over = true;
                }
            }
            for (int i = 0; i < _tabBacks.Length; i++)
            {
                if (mouse.leftButton.wasPressedThisFrame &&
                    RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_tabBacks[i].transform.parent, point, null))
                    model.SetTab((InventoryTab)i);
            }
            if (RectTransformUtility.RectangleContainsScreenPoint(_grid, point, null))
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (wheel != 0f)
                    model.Scroll(wheel > 0f ? -1 : 1);
            }
            if (!over)
                return;
            if (mouse.leftButton.wasPressedThisFrame)
                inventory.PickOrPut();
            else if (mouse.rightButton.wasPressedThisFrame)
                inventory.Use();
        }
    }
}
