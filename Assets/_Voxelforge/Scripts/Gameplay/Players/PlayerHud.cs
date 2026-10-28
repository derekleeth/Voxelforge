using System.Text;
using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// A tiny HUD for each player on this PC (Roadmap 3.2, D-015): a reticle in their team colour where they aim, and
    /// their block bag ("Dirt 12 · Stone 5") under their hero. Everything is placed through <b>that player's own camera
    /// and view</b> (CLAUDE.md Law #5), so it stays on their side of the screen when it's split and follows their hero
    /// when it isn't. Real action bars and inventory windows replace it in 4.4.
    /// </summary>
    public sealed class PlayerHud : MonoBehaviour
    {
        [SerializeField] LocalPlayerManager localPlayers;

        [SerializeField] SplitScreenDirector cameras;

        [Tooltip("For block names.")]
        [SerializeField] VoxelWorldManager world;

        [SerializeField] bool visible = true;

        [Tooltip("Reticle arm length and thickness, in pixels at 1080p.")]
        [SerializeField] Vector2 reticleSize = new Vector2(9f, 2f);

        // Must match SplitScreenDirector's slot count.
        const int MaxSlots = 4;

        readonly StringBuilder _text = new StringBuilder();
        GUIStyle _bagStyle;

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
            bool hasTarget = player.Aim.TryAim(player.World, feet, input, out VoxelHit hit);
            float3 aimPoint = hasTarget ? hit.Point : player.Aim.Eyes(feet) + math.normalize(player.Aim.Direction(input)) * player.Aim.Reach;
            if (TryProject(view, aimPoint, out Vector2 reticle))
                DrawReticle(reticle, player.TeamColor, hasTarget ? 1f : 0.35f, scale);

            if (player.TryGetComponent(out PlayerBlockBag bag) && TryProject(view, player.VisualRoot.position, out Vector2 under))
            {
                string line = DescribeBag(bag);
                if (line.Length > 0)
                    DrawShadowedLabel(new Rect(under.x - 300f * scale, under.y + 6f * scale, 600f * scale, 40f * scale), line, scale);
            }
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

        static void DrawBar(float x, float y, float width, float height) =>
            GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);

        string DescribeBag(PlayerBlockBag bag)
        {
            _text.Clear();
            for (int i = 0; i < bag.SlotCount; i++)
            {
                BlockStack stack = bag.GetSlot(i);
                if (stack.Count == 0)
                    continue;
                if (_text.Length > 0)
                    _text.Append("  ·  ");
                _text.Append(BlockName(stack.BlockId)).Append(' ').Append(stack.Count);
            }
            return _text.ToString();
        }

        string BlockName(ushort blockId) =>
            world != null && world.Blocks != null && world.Blocks.TryGet(blockId, out BlockDefinition block)
                ? block.DisplayName
                : $"Block {blockId}";

        void DrawShadowedLabel(Rect rect, string text, float scale)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            float offset = Mathf.Max(1f, 2f * scale);
            GUI.Label(new Rect(rect.x + offset, rect.y + offset, rect.width, rect.height), text, _bagStyle);
            GUI.color = previous;
            GUI.Label(rect, text, _bagStyle);
        }
    }
}
