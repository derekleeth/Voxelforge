using System;
using System.Collections.Generic;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// One player's crafting window (Roadmap 3.5, made easier to read in 3.6): a framed panel with every recipe as a row
    /// with its picture on the left, and the chosen recipe on the right with what it needs, how many of each this player
    /// has, and whether they can craft it now. <see cref="PlayerHud"/> gives it the part of the screen that belongs to
    /// this player (CLAUDE.md Law #5). Plain IMGUI, like the rest of the HUD, until the 4.4 HUD replaces it.
    /// </summary>
    public sealed class CraftingWindow
    {
        // Sizes in pixels at 1080p, scaled to fit the player's part of the screen.
        const float Width = 780f;
        const float Height = 520f;
        const float Pad = 18f;
        const float HeaderHeight = 52f;
        const float FooterHeight = 40f;
        const float ListWidth = 330f;
        const float RowHeight = 54f;
        const float RowGap = 6f;

        static readonly Color Frame = new Color(0.86f, 0.66f, 0.3f);
        static readonly Color Back = new Color(0.1f, 0.075f, 0.06f, 0.94f);
        static readonly Color Panel = new Color(0.17f, 0.13f, 0.1f, 0.95f);
        static readonly Color Slot = new Color(0.06f, 0.05f, 0.045f, 0.9f);
        static readonly Color Title = new Color(1f, 0.85f, 0.5f);
        static readonly Color Text = new Color(0.95f, 0.92f, 0.86f);
        static readonly Color Dim = new Color(0.62f, 0.58f, 0.52f);
        static readonly Color Good = new Color(0.45f, 0.9f, 0.4f);
        static readonly Color Warn = new Color(1f, 0.72f, 0.3f);
        static readonly Color Bad = new Color(1f, 0.45f, 0.4f);

        readonly Func<ushort, string> _itemName;
        readonly Func<ushort, Texture> _icon;
        GUIStyle _style;
        int _firstRow;

        /// <param name="itemName">An item's display name.</param>
        /// <param name="icon">An item's picture, or null to draw a plain slot.</param>
        public CraftingWindow(Func<ushort, string> itemName, Func<ushort, Texture> icon)
        {
            _itemName = itemName;
            _icon = icon;
        }

        /// <summary>
        /// Draws the window centred in <paramref name="area"/> (GUI pixels), or only the "Crafted ...!" note when it's closed.
        /// </summary>
        public void Draw(Rect area, PlayerCrafter crafter, PlayerBlockBag bag, Color team, float screenScale)
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { richText = false, wordWrap = false, clipping = TextClipping.Clip };
                _style.normal.textColor = Color.white; // GUI.color tints it.
            }
            float s = screenScale * Mathf.Min(1f, area.width * 0.94f / (Width * screenScale), area.height * 0.92f / (Height * screenScale));

            string message = crafter.Message;
            if (!crafter.IsMenuOpen || crafter.Recipes == null)
            {
                if (message != null)
                    DrawToast(new Rect(area.center.x - 220f * s, area.y + 40f * s, 440f * s, 46f * s), message, s);
                return;
            }

            var window = new Rect(Mathf.Round(area.center.x - Width * s * 0.5f), Mathf.Round(area.center.y - Height * s * 0.5f),
                Width * s, Height * s);
            Box(window, Back, Frame, 3f * s, 16f * s);

            // Header: title, and whether a workbench is in reach.
            bool atBench = crafter.IsNearWorkbench();
            Label(new Rect(window.x + Pad * s, window.y + 10f * s, 300f * s, 36f * s), "CRAFTING", Title, 28, s, FontStyle.Bold);
            Chip(new Rect(window.xMax - (Pad + 250f) * s, window.y + 13f * s, 250f * s, 28f * s),
                atBench ? "Workbench nearby" : "No Workbench nearby", atBench ? Good : Warn, s);
            Bar(new Rect(window.x + Pad * s, window.y + HeaderHeight * s, window.width - Pad * 2f * s, 2f * s), Frame * 0.6f);

            IReadOnlyList<RecipeDefinition> recipes = crafter.Recipes.Recipes;
            float top = window.y + (HeaderHeight + 12f) * s;
            float bodyHeight = window.yMax - FooterHeight * s - top - 8f * s;
            DrawList(new Rect(window.x + Pad * s, top, ListWidth * s, bodyHeight), crafter, recipes, team, s);

            float detailX = window.x + (Pad * 2f + ListWidth) * s;
            var detail = new Rect(detailX, top, window.xMax - Pad * s - detailX, bodyHeight);
            if (crafter.Cursor >= 0 && crafter.Cursor < recipes.Count && recipes[crafter.Cursor] != null)
                DrawDetail(detail, recipes[crafter.Cursor], crafter.Check(crafter.Cursor), bag, s);

            DrawFooter(new Rect(window.x + Pad * s, window.yMax - FooterHeight * s, window.width - Pad * 2f * s, FooterHeight * s - 8f * s), s);

            if (message != null)
                DrawToast(new Rect(window.center.x - 220f * s, window.y - 54f * s, 440f * s, 46f * s), message, s);
        }

        void DrawList(Rect area, PlayerCrafter crafter, IReadOnlyList<RecipeDefinition> recipes, Color team, float s)
        {
            int visible = Mathf.Max(1, Mathf.FloorToInt((area.height + RowGap * s) / ((RowHeight + RowGap) * s)));
            // Scroll just enough to keep the chosen recipe in sight.
            int cursor = crafter.Cursor;
            if (cursor < _firstRow)
                _firstRow = cursor;
            else if (cursor >= _firstRow + visible)
                _firstRow = cursor - visible + 1;
            _firstRow = Mathf.Clamp(_firstRow, 0, Mathf.Max(0, recipes.Count - visible));

            float y = area.y;
            for (int i = _firstRow; i < recipes.Count && i < _firstRow + visible; i++)
            {
                RecipeDefinition recipe = recipes[i];
                if (recipe == null)
                    continue;
                CraftCheck check = crafter.Check(i);
                bool chosen = i == cursor;
                var row = new Rect(area.x, y, area.width, RowHeight * s);
                if (chosen)
                    Box(row, new Color(team.r * 0.45f, team.g * 0.45f, team.b * 0.45f, 0.95f), team, 2.5f * s, 10f * s);
                else
                    Box(row, Panel, new Color(0f, 0f, 0f, 0f), 0f, 10f * s);

                var icon = new Rect(row.x + 6f * s, row.y + 6f * s, (RowHeight - 12f) * s, (RowHeight - 12f) * s);
                Icon(icon, recipe.Output.ItemId, check == CraftCheck.Ok ? 1f : 0.55f, s);
                if (recipe.Output.count > 1)
                    Label(new Rect(icon.x, icon.yMax - 18f * s, icon.width - 2f * s, 18f * s), $"x{recipe.Output.count}", Text, 13, s,
                        FontStyle.Bold, TextAnchor.LowerRight);

                float textX = icon.xMax + 10f * s;
                Label(new Rect(textX, row.y + 5f * s, row.xMax - textX - 6f * s, 26f * s), _itemName(recipe.Output.ItemId),
                    check == CraftCheck.Ok || chosen ? Text : Dim, 18, s, FontStyle.Bold);
                Label(new Rect(textX, row.y + 28f * s, row.xMax - textX - 6f * s, 20f * s), StatusShort(check), StatusColour(check), 13, s);
                y += (RowHeight + RowGap) * s;
            }

            // A little scroll bar when the list is longer than the window.
            if (recipes.Count > visible)
            {
                var track = new Rect(area.xMax + 4f * s, area.y, 4f * s, area.height);
                Box(track, Slot, new Color(0f, 0f, 0f, 0f), 0f, 2f * s);
                float thumb = track.height * visible / recipes.Count;
                float at = (track.height - thumb) * _firstRow / Mathf.Max(1, recipes.Count - visible);
                Box(new Rect(track.x, track.y + at, track.width, thumb), Frame, new Color(0f, 0f, 0f, 0f), 0f, 2f * s);
            }
        }

        void DrawDetail(Rect area, RecipeDefinition recipe, CraftCheck check, PlayerBlockBag bag, float s)
        {
            Box(area, Panel, Frame * 0.5f, 1.5f * s, 12f * s);
            var big = new Rect(area.x + 16f * s, area.y + 16f * s, 104f * s, 104f * s);
            Icon(big, recipe.Output.ItemId, 1f, s);

            float textX = big.xMax + 16f * s;
            ItemAmount output = recipe.Output;
            Label(new Rect(textX, big.y + 8f * s, area.xMax - textX - 12f * s, 36f * s), _itemName(output.ItemId), Title, 26, s, FontStyle.Bold);
            Label(new Rect(textX, big.y + 46f * s, area.xMax - textX - 12f * s, 24f * s),
                output.count > 1 ? $"Makes {output.count}" : "Makes 1", Text, 16, s);
            Label(new Rect(textX, big.y + 72f * s, area.xMax - textX - 12f * s, 24f * s),
                recipe.NeedsWorkbench ? "Made at a Workbench" : "Made anywhere", Dim, 15, s);

            float y = big.yMax + 16f * s;
            Label(new Rect(area.x + 16f * s, y, 200f * s, 22f * s), "NEEDS", Frame, 14, s, FontStyle.Bold);
            y += 26f * s;
            foreach (ItemAmount input in recipe.Inputs)
            {
                ushort id = input.ItemId;
                int have = bag != null ? bag.CountOf(id) : 0;
                bool enough = have >= input.count;
                var line = new Rect(area.x + 12f * s, y, area.width - 24f * s, 40f * s);
                Icon(new Rect(line.x + 4f * s, line.y + 2f * s, 36f * s, 36f * s), id, 1f, s);
                Label(new Rect(line.x + 50f * s, line.y + 8f * s, line.width - 170f * s, 26f * s), _itemName(id), Text, 17, s);
                Label(new Rect(line.xMax - 116f * s, line.y + 8f * s, 112f * s, 26f * s), $"{have} / {input.count}",
                    enough ? Good : Bad, 18, s, FontStyle.Bold, TextAnchor.MiddleRight);
                y += 44f * s;
            }

            // What happens if you press Place.
            var status = new Rect(area.x + 12f * s, area.yMax - 50f * s, area.width - 24f * s, 38f * s);
            Color colour = StatusColour(check);
            Box(status, new Color(colour.r * 0.25f, colour.g * 0.25f, colour.b * 0.25f, 0.95f), colour, 2f * s, 8f * s);
            Label(status, StatusLong(check), colour, 17, s, FontStyle.Bold, TextAnchor.MiddleCenter);
        }

        void DrawFooter(Rect area, float s)
        {
            float x = area.x;
            x = Hint(x, area, "Next / Previous", "choose", s);
            x = Hint(x + 18f * s, area, "Place", "craft", s);
            Hint(x + 18f * s, area, "Interact", "close", s);
        }

        float Hint(float x, Rect area, string button, string action, float s)
        {
            _style.fontSize = Mathf.RoundToInt(14 * s);
            _style.fontStyle = FontStyle.Bold;
            float buttonWidth = _style.CalcSize(new GUIContent(button)).x + 16f * s;
            var key = new Rect(x, area.y + (area.height - 24f * s) * 0.5f, buttonWidth, 24f * s);
            Box(key, Slot, Frame * 0.8f, 1.5f * s, 6f * s);
            Label(key, button, Title, 14, s, FontStyle.Bold, TextAnchor.MiddleCenter);
            _style.fontSize = Mathf.RoundToInt(14 * s);
            _style.fontStyle = FontStyle.Normal;
            float actionWidth = _style.CalcSize(new GUIContent(action)).x + 4f * s;
            Label(new Rect(key.xMax + 6f * s, key.y, actionWidth, key.height), action, Dim, 14, s, FontStyle.Normal, TextAnchor.MiddleLeft);
            return key.xMax + 6f * s + actionWidth;
        }

        void DrawToast(Rect rect, string message, float s)
        {
            Box(rect, new Color(0.08f, 0.2f, 0.08f, 0.95f), Good, 2.5f * s, 12f * s);
            Label(rect, message, Text, 20, s, FontStyle.Bold, TextAnchor.MiddleCenter);
        }

        void Icon(Rect rect, ushort itemId, float alpha, float s)
        {
            Box(rect, Slot, Frame * 0.45f, 1.5f * s, 8f * s);
            Texture texture = _icon(itemId);
            if (texture == null)
                return;
            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(rect.x + 2f * s, rect.y + 2f * s, rect.width - 4f * s, rect.height - 4f * s), texture, ScaleMode.ScaleToFit);
            GUI.color = previous;
        }

        void Label(Rect rect, string text, Color colour, int size, float s, FontStyle style = FontStyle.Normal,
            TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            _style.fontSize = Mathf.Max(8, Mathf.RoundToInt(size * s));
            _style.fontStyle = style;
            _style.alignment = anchor;
            Color previous = GUI.color;
            // A soft drop shadow keeps text readable over the world when the window is small.
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            float offset = Mathf.Max(1f, 1.5f * s);
            GUI.Label(new Rect(rect.x + offset, rect.y + offset, rect.width, rect.height), text, _style);
            GUI.color = colour;
            GUI.Label(rect, text, _style);
            GUI.color = previous;
        }

        void Chip(Rect rect, string text, Color colour, float s)
        {
            Box(rect, new Color(colour.r * 0.22f, colour.g * 0.22f, colour.b * 0.22f, 0.95f), colour, 1.5f * s, rect.height * 0.5f);
            Label(rect, text, colour, 15, s, FontStyle.Bold, TextAnchor.MiddleCenter);
        }

        /// <summary>A rounded box with a border, drawn by the GPU: no textures needed.</summary>
        internal static void Box(Rect rect, Color fill, Color border, float borderWidth, float radius)
        {
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, fill, 0f, radius);
            if (borderWidth > 0f && border.a > 0f)
                GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, border, borderWidth, radius);
        }

        static void Bar(Rect rect, Color colour) =>
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, colour, 0f, 0f);

        static string StatusShort(CraftCheck check)
        {
            switch (check)
            {
                case CraftCheck.Ok:
                    return "Ready to craft";
                case CraftCheck.NeedsWorkbench:
                    return "Needs a Workbench";
                case CraftCheck.BagFull:
                    return "Bag full";
                default:
                    return "Missing items";
            }
        }

        static string StatusLong(CraftCheck check)
        {
            switch (check)
            {
                case CraftCheck.Ok:
                    return "Press Place to craft!";
                case CraftCheck.NeedsWorkbench:
                    return "Stand near a Workbench";
                case CraftCheck.BagFull:
                    return "Your bag is full";
                default:
                    return "Gather the missing items";
            }
        }

        static Color StatusColour(CraftCheck check)
        {
            switch (check)
            {
                case CraftCheck.Ok:
                    return Good;
                case CraftCheck.NeedsWorkbench:
                case CraftCheck.BagFull:
                    return Warn;
                default:
                    return Bad;
            }
        }
    }
}
