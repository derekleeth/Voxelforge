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
    /// when it isn't. Holding the M.G.R.S. (3.6), the reticle reaches as far as its bolts and shows the recharge. The
    /// action bar, mana, cast bar and combat text are drawn by <see cref="CombatHud"/> (4.4); the bag becomes a real
    /// inventory window in 4.5.
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
            // The reticle sits where the aim ray from the hero's eyes really lands, out as far as anything they can cast
            // reaches: the M.G.R.S. (3.6) or the spells on their action bar (4.4). Stopping it at mining reach left it
            // floating in mid-air, and from the camera above and behind the hero that point shows below where the
            // spell hits.
            PlayerHands hands = player.TryGetComponent(out PlayerHands h) ? h : null;
            PlayerCaster caster = player.TryGetComponent(out PlayerCaster c) ? c : null;
            AbilityDefinition ability = null;
            bool ranged = hands != null && hands.TryGetSelectedAbility(out ability);
            float reach = Mathf.Max(player.Aim.Reach, ranged ? ability.Range : 0f, SpellReach(player));
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
                // A small reminder that Home works (Roadmap 3.8).
                if (player.TryGetComponent(out PlayerHearth home) && home.HasHome)
                    DrawShadowedLabel(new Rect(under.x - 300f * scale, y, 600f * scale, 30f * scale),
                        "<color=#7CFF8A>Home Totem set</color>", "Home Totem set", scale);
            }

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

        /// <summary>The longest range of the spells on this player's action bar, 0 if it's empty.</summary>
        static float SpellReach(PlayerController player)
        {
            if (!player.TryGetComponent(out PlayerActionBar bar))
                return 0f;
            float range = 0f;
            for (int i = 0; i < PlayerActionBar.SlotCount; i++)
            {
                if (bar.TryGet(i, out AbilityDefinition spell))
                    range = Mathf.Max(range, spell.Range);
            }
            return range;
        }

        Rect OwnArea(PlayerController player, PlayerView view) => PlayerScreenArea.Own(cameras.Layout.Mode, player, view);

        static bool TryProject(PlayerView view, float3 point, out Vector2 gui) => PlayerScreenArea.TryProject(view, point, out gui);

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
