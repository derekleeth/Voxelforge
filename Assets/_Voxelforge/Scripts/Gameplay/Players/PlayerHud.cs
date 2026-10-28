using System.Collections.Generic;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// A tiny HUD for each player on this PC (Roadmap 3.2, D-015): a reticle in their team colour where they aim, and
    /// their bag ("[Dirt 12] · Stone 5", with the item they hold in brackets, 3.3) under their hero, and their crafting
    /// window when it's open (3.5). Everything is placed through <b>that player's own camera
    /// and view</b> (CLAUDE.md Law #5), so it stays on their side of the screen when it's split and follows their hero
    /// when it isn't. Real action bars and inventory windows replace it in 4.4.
    /// </summary>
    public sealed class PlayerHud : MonoBehaviour
    {
        [SerializeField] LocalPlayerManager localPlayers;

        [SerializeField] SplitScreenDirector cameras;

        [Tooltip("For block names.")]
        [SerializeField] VoxelWorldManager world;

        [Tooltip("Data/Items/ItemRegistry.asset: for tool names.")]
        [SerializeField] ItemRegistry items;

        [SerializeField] bool visible = true;

        [Tooltip("Reticle arm length and thickness, in pixels at 1080p.")]
        [SerializeField] Vector2 reticleSize = new Vector2(9f, 2f);

        // Must match SplitScreenDirector's slot count.
        const int MaxSlots = 4;

        // How wide the crafting window is, in pixels at 1080p.
        const float PanelWidth = 560f;

        readonly StringBuilder _text = new StringBuilder();
        GUIStyle _bagStyle;
        GUIStyle _panelStyle;

        void OnGUI()
        {
            if (!visible || localPlayers == null || cameras == null || Event.current.type != EventType.Repaint)
                return;

            _bagStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, richText = true, fontStyle = FontStyle.Bold };
            float scale = Screen.height / 1080f;
            _bagStyle.fontSize = Mathf.RoundToInt(18 * scale);
            _panelStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, richText = true, wordWrap = false };
            _panelStyle.fontSize = Mathf.RoundToInt(17 * scale);

            for (int slot = 0; slot < MaxSlots; slot++)
            {
                if (localPlayers.TryGetLocalPlayer(slot, out PlayerController player, out _) && player.HasLanded &&
                    cameras.TryGetView(player.LocalSlot, out PlayerView view))
                    DrawHud(player, view, scale);
            }
        }

        void DrawHud(PlayerController player, PlayerView view, float scale)
        {
            float3 feet = player.transform.position;
            PlayerInputFrame input = player.OwnerInput;
            bool hasTarget = player.Aim.TryAim(player.World, feet, input, out VoxelHit hit);
            float3 aimPoint = hasTarget ? hit.Point : player.Aim.Eyes(feet) + math.normalize(player.Aim.Direction(input)) * player.Aim.Reach;
            if (TryProject(view, aimPoint, out Vector2 reticle))
                DrawReticle(reticle, player.TeamColor, hasTarget ? 1f : 0.35f, scale);

            if (player.TryGetComponent(out PlayerBlockBag bag) && TryProject(view, player.VisualRoot.position, out Vector2 under))
            {
                ushort selected = player.TryGetComponent(out PlayerHands hands) ? hands.OwnerSelected : BlockIds.Air;
                string line = DescribeBag(bag, selected, player.TeamColor);
                if (line.Length > 0)
                    DrawShadowedLabel(new Rect(under.x - 500f * scale, under.y + 6f * scale, 1000f * scale, 40f * scale), line,
                        DescribeBag(bag, selected, null), scale);
            }

            if (player.TryGetComponent(out PlayerCrafter crafter))
                DrawCrafting(crafter, view, player.TeamColor, scale);
        }

        /// <summary>
        /// The recipe list (Roadmap 3.5) in the top-right of this player's view, clear of the F3 overlay: craftable recipes bright, the rest grey
        /// with the reason, and the highlighted one in brackets and team colour.
        /// </summary>
        void DrawCrafting(PlayerCrafter crafter, PlayerView view, Color teamColor, float scale)
        {
            Rect rect = view.ScreenRect;
            float left = (rect.x + rect.width) * Screen.width - PanelWidth * scale;
            float top = (1f - rect.y - rect.height) * Screen.height + 24f * scale;
            float lineHeight = 24f * scale;

            string message = crafter.Message;
            if (message != null)
                DrawPanelLine(left, top + lineHeight * 0.0f, $"<b>{message}</b>", scale);
            if (!crafter.IsMenuOpen || crafter.Recipes == null)
                return;

            float y = top + (message != null ? lineHeight * 1.5f : 0f);
            bool atBench = crafter.IsNearWorkbench();
            DrawPanelLine(left, y, atBench ? "<b>Crafting</b>  (at a Workbench)" : "<b>Crafting</b>  (find a Workbench for tools)", scale);
            y += lineHeight * 1.3f;

            string team = ColorUtility.ToHtmlStringRGB(teamColor);
            IReadOnlyList<RecipeDefinition> recipes = crafter.Recipes.Recipes;
            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeDefinition recipe = recipes[i];
                if (recipe == null)
                    continue;
                CraftCheck check = crafter.Check(i);
                _text.Clear();
                bool highlighted = i == crafter.Cursor;
                string colour = highlighted ? team : check == CraftCheck.Ok ? "FFFFFF" : "9A9A9A";
                _text.Append("<color=#").Append(colour).Append('>').Append(highlighted ? "[ " : "  ");
                ItemAmount output = recipe.Output;
                _text.Append(output.count > 1 ? $"{output.count} " : "").Append(ItemName(output.ItemId));
                _text.Append("  <size=").Append(Mathf.RoundToInt(14 * scale)).Append(">(");
                for (int input = 0; input < recipe.Inputs.Count; input++)
                {
                    if (input > 0)
                        _text.Append(", ");
                    _text.Append(recipe.Inputs[input].count).Append(' ').Append(ItemName(recipe.Inputs[input].ItemId));
                }
                _text.Append(")</size>");
                if (check == CraftCheck.NeedsWorkbench)
                    _text.Append("  - needs a Workbench");
                else if (check == CraftCheck.BagFull)
                    _text.Append("  - bag full");
                _text.Append(highlighted ? " ]</color>" : "</color>");
                DrawPanelLine(left, y, _text.ToString(), scale);
                y += lineHeight;
            }
            DrawPanelLine(left, y + lineHeight * 0.3f,
                "<size=" + Mathf.RoundToInt(14 * scale) + ">Next / Previous: choose    Place: craft    Interact: close</size>", scale);
        }

        void DrawPanelLine(float x, float y, string text, float scale)
        {
            var rect = new Rect(x, y, PanelWidth * scale, 30f * scale);
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            float offset = Mathf.Max(1f, 2f * scale);
            GUI.Label(new Rect(rect.x + offset, rect.y + offset, rect.width, rect.height), StripColor(text), _panelStyle);
            GUI.color = previous;
            GUI.Label(rect, text, _panelStyle);
        }

        /// <summary>The text without colour tags, for the drop shadow.</summary>
        static string StripColor(string text) => ColorTags.Replace(text, "");

        static readonly System.Text.RegularExpressions.Regex ColorTags =
            new System.Text.RegularExpressions.Regex("</?color[^>]*>", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>Where a world point shows up in this player's view, in GUI pixels (origin top-left).</summary>
        static bool TryProject(PlayerView view, float3 point, out Vector2 gui)
        {
            Vector3 viewport = view.Camera.WorldToViewportPoint(point);
            Rect rect = view.ScreenRect;
            gui = new Vector2((rect.x + viewport.x * rect.width) * Screen.width,
                (1f - (rect.y + viewport.y * rect.height)) * Screen.height);
            return viewport.z > 0f;
        }

        void DrawReticle(Vector2 centre, Color colour, float alpha, float scale)
        {
            float arm = reticleSize.x * scale;
            float thick = Mathf.Max(1f, reticleSize.y * scale);
            float gap = 3f * scale;
            Color previous = GUI.color;
            // A dark outline first, so the reticle shows on bright snow and sand too.
            for (int pass = 0; pass < 2; pass++)
            {
                float grow = pass == 0 ? Mathf.Max(1f, scale) : 0f;
                GUI.color = pass == 0 ? new Color(0f, 0f, 0f, 0.6f * alpha) : new Color(colour.r, colour.g, colour.b, alpha);
                DrawBar(centre.x - gap - arm - grow, centre.y - thick * 0.5f - grow, arm + grow * 2f, thick + grow * 2f);
                DrawBar(centre.x + gap - grow, centre.y - thick * 0.5f - grow, arm + grow * 2f, thick + grow * 2f);
                DrawBar(centre.x - thick * 0.5f - grow, centre.y - gap - arm - grow, thick + grow * 2f, arm + grow * 2f);
                DrawBar(centre.x - thick * 0.5f - grow, centre.y + gap - grow, thick + grow * 2f, arm + grow * 2f);
            }
            GUI.color = previous;
        }

        static void DrawBar(float x, float y, float width, float height) =>
            GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);

        /// <summary>
        /// "[Dirt 12]  ·  Stone 5": the block <paramref name="selected"/> for building is bracketed, in
        /// <paramref name="teamColor"/> if there is one (the drop shadow has none).
        /// </summary>
        string DescribeBag(PlayerBlockBag bag, ushort selected, Color? teamColor)
        {
            _text.Clear();
            for (int i = 0; i < bag.SlotCount; i++)
            {
                ItemStack stack = bag.GetSlot(i);
                if (stack.Count == 0)
                    continue;
                if (_text.Length > 0)
                    _text.Append("  ·  ");
                if (stack.ItemId == selected)
                {
                    if (teamColor.HasValue)
                        _text.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(teamColor.Value)).Append('>');
                    _text.Append('[').Append(ItemName(stack.ItemId)).Append(' ').Append(stack.Count).Append(']');
                    if (teamColor.HasValue)
                        _text.Append("</color>");
                }
                else
                {
                    _text.Append(ItemName(stack.ItemId)).Append(' ').Append(stack.Count);
                }
            }
            return _text.ToString();
        }

        string ItemName(ushort itemId)
        {
            BlockRegistry blocks = world != null ? world.Blocks : null;
            if (items != null)
                return items.DisplayName(itemId, blocks);
            return blocks != null && blocks.TryGet(itemId, out BlockDefinition block) ? block.DisplayName : $"Item {itemId}";
        }

        /// <param name="shadowText">The same words without colour tags, which would tint the shadow.</param>
        void DrawShadowedLabel(Rect rect, string text, string shadowText, float scale)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            float offset = Mathf.Max(1f, 2f * scale);
            GUI.Label(new Rect(rect.x + offset, rect.y + offset, rect.width, rect.height), shadowText, _bagStyle);
            GUI.color = previous;
            GUI.Label(rect, text, _bagStyle);
        }
    }
}
