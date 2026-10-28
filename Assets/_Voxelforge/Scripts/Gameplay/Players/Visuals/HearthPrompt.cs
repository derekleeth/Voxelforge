using System;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// One player's Home Totem HUD (Roadmap 3.8): the "Teleport home?" prompt, the cast bar while hearthing, and short
    /// messages. <see cref="PlayerHud"/> gives it the part of the screen that belongs to this player (CLAUDE.md Law #5).
    /// Plain IMGUI, like the crafting window.
    /// </summary>
    public sealed class HearthPrompt
    {
        // Sizes in pixels at 1080p.
        const float Width = 560f;
        const float Height = 190f;

        static readonly Color Frame = new Color(0.4f, 0.95f, 0.5f);
        static readonly Color Back = new Color(0.05f, 0.11f, 0.06f, 0.94f);
        static readonly Color Slot = new Color(0.03f, 0.07f, 0.04f, 0.9f);
        static readonly Color Title = new Color(0.75f, 1f, 0.75f);
        static readonly Color Text = new Color(0.93f, 0.97f, 0.92f);
        static readonly Color Dim = new Color(0.6f, 0.72f, 0.6f);

        readonly Func<Texture> _icon;
        GUIStyle _style;

        /// <param name="icon">The Home Totem's picture, or null.</param>
        public HearthPrompt(Func<Texture> icon) => _icon = icon;

        /// <summary>Draws whatever this player's hearth needs right now, in <paramref name="area"/> (GUI pixels).</summary>
        public void Draw(Rect area, PlayerHearth hearth, float screenScale)
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { richText = false, wordWrap = true, clipping = TextClipping.Clip };
                _style.normal.textColor = Color.white;
            }
            float s = screenScale * Mathf.Min(1f, area.width * 0.9f / (Width * screenScale));

            if (hearth.IsPromptOpen)
                DrawPrompt(area, s);
            if (hearth.IsCasting)
                DrawCastBar(area, hearth.CastProgress, s);
            string message = hearth.Message;
            if (message != null)
            {
                var rect = new Rect(area.center.x - 270f * s, area.y + 100f * s, 540f * s, 50f * s);
                CraftingWindow.Box(rect, Back, Frame, 2.5f * s, 12f * s);
                Label(rect, message, Text, 18, s, FontStyle.Bold, TextAnchor.MiddleCenter);
            }
        }

        void DrawPrompt(Rect area, float s)
        {
            var window = new Rect(Mathf.Round(area.center.x - Width * s * 0.5f), Mathf.Round(area.center.y - Height * s * 0.5f),
                Width * s, Height * s);
            CraftingWindow.Box(window, Back, Frame, 3f * s, 16f * s);

            var icon = new Rect(window.x + 18f * s, window.y + 18f * s, 96f * s, 96f * s);
            CraftingWindow.Box(icon, Slot, Frame * 0.5f, 1.5f * s, 10f * s);
            Texture texture = _icon();
            if (texture != null)
                GUI.DrawTexture(new Rect(icon.x + 3f * s, icon.y + 3f * s, icon.width - 6f * s, icon.height - 6f * s), texture,
                    ScaleMode.ScaleToFit);

            float textX = icon.xMax + 18f * s;
            Label(new Rect(textX, window.y + 16f * s, window.xMax - textX - 16f * s, 36f * s), "TELEPORT HOME?", Title, 26, s,
                FontStyle.Bold);
            Label(new Rect(textX, window.y + 54f * s, window.xMax - textX - 16f * s, 60f * s),
                "Return to your Home Totem. Stand still while it charges.", Text, 16, s);

            float y = window.yMax - 52f * s;
            float x = Hint(window.x + 18f * s, y, "Place", "Yes, go home", s);
            Hint(x + 28f * s, y, "Home", "No", s);
        }

        void DrawCastBar(Rect area, float progress, float s)
        {
            var bar = new Rect(area.center.x - 180f * s, area.center.y + 70f * s, 360f * s, 26f * s);
            CraftingWindow.Box(bar, Slot, Frame, 2f * s, 13f * s);
            if (progress > 0.01f)
                CraftingWindow.Box(new Rect(bar.x + 3f * s, bar.y + 3f * s, (bar.width - 6f * s) * progress, bar.height - 6f * s),
                    new Color(0.3f, 0.85f, 0.4f, 0.95f), Color.clear, 0f, 10f * s);
            Label(bar, "Going home...", Text, 15, s, FontStyle.Bold, TextAnchor.MiddleCenter);
        }

        float Hint(float x, float y, string button, string action, float s)
        {
            _style.fontSize = Mathf.RoundToInt(16 * s);
            _style.fontStyle = FontStyle.Bold;
            float buttonWidth = _style.CalcSize(new GUIContent(button)).x + 18f * s;
            var key = new Rect(x, y, buttonWidth, 30f * s);
            CraftingWindow.Box(key, Slot, Frame * 0.8f, 1.5f * s, 7f * s);
            Label(key, button, Title, 16, s, FontStyle.Bold, TextAnchor.MiddleCenter);
            _style.fontStyle = FontStyle.Normal;
            float actionWidth = _style.CalcSize(new GUIContent(action)).x + 6f * s;
            Label(new Rect(key.xMax + 8f * s, y, actionWidth, key.height), action, Dim, 16, s, FontStyle.Normal, TextAnchor.MiddleLeft);
            return key.xMax + 8f * s + actionWidth;
        }

        void Label(Rect rect, string text, Color colour, int size, float s, FontStyle style = FontStyle.Normal,
            TextAnchor anchor = TextAnchor.UpperLeft)
        {
            _style.fontSize = Mathf.Max(8, Mathf.RoundToInt(size * s));
            _style.fontStyle = style;
            _style.alignment = anchor;
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            float offset = Mathf.Max(1f, 1.5f * s);
            GUI.Label(new Rect(rect.x + offset, rect.y + offset, rect.width, rect.height), text, _style);
            GUI.color = colour;
            GUI.Label(rect, text, _style);
            GUI.color = previous;
        }
    }
}
