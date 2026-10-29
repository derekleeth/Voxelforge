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
    /// when it isn't. Holding the M.G.R.S. (3.6), the reticle reaches as far as its bolts and shows the recharge. A mana
    /// bar sits under the hero and a cast bar low in their own view (4.1). Real action bars and inventory windows replace
    /// it in 4.4.
    /// </summary>
    public sealed class PlayerHud : MonoBehaviour
    {
        [SerializeField] LocalPlayerManager localPlayers;

        [SerializeField] SplitScreenDirector cameras;

        [Tooltip("For block names.")]
        [SerializeField] VoxelWorldManager world;

        [Tooltip("Data/Items/ItemRegistry.asset: for tool names and pictures.")]
        [SerializeField] ItemRegistry items;

        [Tooltip("Materials/VoxelBlocks.mat: for block pictures in the crafting window.")]
        [SerializeField] Material blockMaterial;

        [SerializeField] bool visible = true;

        [Tooltip("Reticle arm length and thickness, in pixels at 1080p.")]
        [SerializeField] Vector2 reticleSize = new Vector2(9f, 2f);

        [SerializeField] Color manaColor = new Color(0.25f, 0.55f, 1f);

        [SerializeField] Color castColor = new Color(1f, 0.8f, 0.3f);

        // Must match SplitScreenDirector's slot count.
        const int MaxSlots = 4;

        readonly StringBuilder _text = new StringBuilder();
        GUIStyle _bagStyle;
        CraftingWindow _craftingWindow;
        HearthPrompt _hearthPrompt;
        ItemLooks _looks;
        ItemIcons _icons;

        ItemIcons Icons => _icons ??= new ItemIcons(_looks ??= new ItemLooks(items, blockMaterial));

        void LateUpdate()
        {
            VoxelWorld voxels = world != null ? world.World : null;
            _icons?.DrawQueued(voxels != null ? voxels.Blocks : default);
        }

        void OnDestroy()
        {
            _icons?.Dispose();
            _looks?.Dispose();
        }

        void OnGUI()
        {
            if (!visible || localPlayers == null || cameras == null || Event.current.type != EventType.Repaint)
                return;

            _bagStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, richText = true, fontStyle = FontStyle.Bold };
            float scale = Screen.height / 1080f;
            _bagStyle.fontSize = Mathf.RoundToInt(18 * scale);

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
            // Holding the M.G.R.S. (3.6), the reticle reaches as far as its bolts fly.
            PlayerHands hands = player.TryGetComponent(out PlayerHands h) ? h : null;
            PlayerCaster caster = player.TryGetComponent(out PlayerCaster c) ? c : null;
            AbilityDefinition ability = null;
            bool ranged = hands != null && hands.TryGetSelectedAbility(out ability);
            float reach = ranged ? ability.Range : player.Aim.Reach;
            bool hasTarget = player.Aim.TryAim(player.World, feet, input, reach, out VoxelHit hit);
            float3 aimPoint = hasTarget ? hit.Point : player.Aim.Eyes(feet) + math.normalize(player.Aim.Direction(input)) * reach;
            if (TryProject(view, aimPoint, out Vector2 reticle))
            {
                DrawReticle(reticle, player.TeamColor, hasTarget ? 1f : 0.35f, scale);
                if (ranged && caster != null)
                    DrawCooldown(reticle, player.TeamColor, caster.CooldownReady(ability), scale);
            }

            if (player.TryGetComponent(out PlayerBlockBag bag) && TryProject(view, player.VisualRoot.position, out Vector2 under))
            {
                ushort selected = hands != null ? hands.OwnerSelected : BlockIds.Air;
                string line = DescribeBag(bag, selected, player.TeamColor);
                // Stacked down from under the hero: a full bag wraps onto more lines, so each row starts below the last.
                float y = under.y + 6f * scale;
                if (line.Length > 0)
                {
                    string plain = DescribeBag(bag, selected, null);
                    float height = _bagStyle.CalcHeight(new GUIContent(plain), 1000f * scale);
                    DrawShadowedLabel(new Rect(under.x - 500f * scale, y, 1000f * scale, height), line, plain, scale);
                    y += height;
                }
                // Mana (4.1), between the bag and the Home reminder.
                if (caster != null && caster.MaxMana > 0f)
                {
                    DrawMeter(new Rect(under.x - 70f * scale, y + 4f * scale, 140f * scale, Mathf.Max(3f, 6f * scale)),
                        caster.Mana / caster.MaxMana, manaColor, scale);
                    y += 14f * scale;
                }
                // A small reminder that Home works (Roadmap 3.8).
                if (player.TryGetComponent(out PlayerHearth home) && home.HasHome)
                    DrawShadowedLabel(new Rect(under.x - 300f * scale, y, 600f * scale, 30f * scale),
                        "<color=#7CFF8A>Home Totem set</color>", "Home Totem set", scale);
            }

            if (caster != null)
                DrawCasting(OwnArea(player, view), caster, scale);

            if (player.TryGetComponent(out PlayerCrafter crafter))
            {
                _craftingWindow ??= new CraftingWindow(ItemName, id => Icons?.Get(id));
                _craftingWindow.Draw(OwnArea(player, view), crafter, bag, player.TeamColor, scale);
            }

            if (player.TryGetComponent(out PlayerHearth hearth))
            {
                _hearthPrompt ??= new HearthPrompt(() => Icons?.Get(BlockIds.HomeTotem));
                _hearthPrompt.Draw(OwnArea(player, view), hearth, scale);
            }
        }

        /// <summary>
        /// The part of the screen (GUI pixels) that is this player's own, for their crafting window: their rectangle in a
        /// grid, their side of the line in a Voronoi split, and in a merged view the half their hero stands in, so two open
        /// windows never sit on top of each other (CLAUDE.md Law #5).
        /// </summary>
        Rect OwnArea(PlayerController player, PlayerView view)
        {
            Rect r = view.ScreenRect;
            var area = new Rect(r.x * Screen.width, (1f - r.y - r.height) * Screen.height, r.width * Screen.width, r.height * Screen.height);
            SplitMode mode = cameras.Layout.Mode;
            float2 towards; // Which way, on screen (y up), this player's half lies.
            if (mode == SplitMode.Voronoi && view.VoronoiSide != 0f)
                towards = view.VoronoiNormal * view.VoronoiSide;
            else if (mode == SplitMode.Merged && TryProject(view, player.VisualRoot.position, out Vector2 hero))
                towards = new float2(hero.x < area.center.x ? -1f : 1f, 0f);
            else
                return area;

            if (math.abs(towards.x) >= math.abs(towards.y))
                return towards.x < 0f
                    ? new Rect(area.x, area.y, area.width * 0.5f, area.height)
                    : new Rect(area.center.x, area.y, area.width * 0.5f, area.height);
            return towards.y > 0f
                ? new Rect(area.x, area.y, area.width, area.height * 0.5f)
                : new Rect(area.x, area.center.y, area.width, area.height * 0.5f);
        }

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

        /// <summary>A thin bar under the reticle that fills back up while the staff recharges; hidden when it's ready.</summary>
        void DrawCooldown(Vector2 centre, Color colour, float ready, float scale)
        {
            if (ready >= 1f)
                return;
            float width = 30f * scale;
            float height = Mathf.Max(2f, 3f * scale);
            float y = centre.y + (reticleSize.x + 10f) * scale;
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            DrawBar(centre.x - width * 0.5f - 1f, y - 1f, width + 2f, height + 2f);
            GUI.color = colour;
            DrawBar(centre.x - width * 0.5f, y, width * Mathf.Clamp01(ready), height);
            GUI.color = previous;
        }

        /// <summary>
        /// The cast bar (name and fill) low in this player's own area while they cast (4.1), and the owner's "Not enough
        /// mana" style message just above it.
        /// </summary>
        void DrawCasting(Rect area, PlayerCaster caster, float scale)
        {
            float x = area.center.x;
            float y = area.y + area.height * 0.74f;
            AbilityDefinition casting = caster.Casting;
            if (casting != null)
            {
                float width = Mathf.Min(260f * scale, area.width * 0.6f);
                DrawShadowedLabel(new Rect(x - width, y - 28f * scale, width * 2f, 28f * scale), casting.DisplayName,
                    casting.DisplayName, scale);
                DrawMeter(new Rect(x - width * 0.5f, y, width, 12f * scale), caster.CastProgress, castColor, scale);
            }
            string message = caster.Message;
            if (message != null)
                DrawShadowedLabel(new Rect(x - 300f * scale, y - 60f * scale, 600f * scale, 30f * scale),
                    $"<color=#FF6A5A>{message}</color>", message, scale);
        }

        /// <summary>A bar with a dark outline and back, filled <paramref name="fill"/> (0-1) from the left.</summary>
        static void DrawMeter(Rect rect, float fill, Color colour, float scale)
        {
            float edge = Mathf.Max(1f, scale);
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            DrawBar(rect.x - edge, rect.y - edge, rect.width + edge * 2f, rect.height + edge * 2f);
            GUI.color = colour;
            DrawBar(rect.x, rect.y, rect.width * Mathf.Clamp01(fill), rect.height);
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
