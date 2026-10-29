using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The comic-book RPG HUD for every player on this PC (Roadmap 4.4): an action bar with mana, a cast bar and floating
    /// combat text each. One overlay canvas holds a <see cref="PlayerHudPanel"/> per local player, and every frame each
    /// panel is fitted to <b>that player's own</b> part of the screen (<see cref="PlayerScreenArea"/>, CLAUDE.md Law #5),
    /// so it follows them through Dynamic and Classic splits and merges. Each panel also holds that player's inventory
    /// window (4.5). The reticle, held item and crafting window stay in <see cref="PlayerHud"/>.
    /// </summary>
    public sealed class CombatHud : MonoBehaviour
    {
        [SerializeField] LocalPlayerManager localPlayers;

        [SerializeField] SplitScreenDirector cameras;

        [Tooltip("Data/UI/HudSkin.asset: frames, fonts and colours.")]
        [SerializeField] HudSkin skin;

        [Tooltip("Data/Items/ItemRegistry.asset: names for \"+12 Dirt\".")]
        [SerializeField] ItemRegistry items;

        [Tooltip("For block names.")]
        [SerializeField] VoxelWorldManager world;

        [Tooltip("Materials/VoxelBlocks.mat: for block pictures in the inventory window.")]
        [SerializeField] Material blockMaterial;

        [SerializeField] bool visible = true;

        [Tooltip("Above the world, under the IMGUI windows (which always draw last).")]
        [SerializeField] int sortingOrder = 10;

        // Must match SplitScreenDirector's slot count.
        const int MaxSlots = 4;

        readonly PlayerHudPanel[] _panels = new PlayerHudPanel[MaxSlots];
        RectTransform _canvas;
        ItemLooks _looks;
        ItemIcons _icons;

        ItemIcons Icons => _icons ??= new ItemIcons(_looks ??= new ItemLooks(items, blockMaterial));

        void Awake()
        {
            if (skin == null)
            {
                Debug.LogError("CombatHud needs a HudSkin (Voxelforge > Build HUD Art).", this);
                enabled = false;
                return;
            }
            var go = new GameObject("Combat HUD Canvas", typeof(RectTransform));
            go.layer = 5; // UI
            go.transform.SetParent(transform, false);
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            canvas.pixelPerfect = false;
            _canvas = (RectTransform)go.transform;
        }

        void OnDestroy()
        {
            for (int i = 0; i < _panels.Length; i++)
            {
                _panels[i]?.Dispose();
                _panels[i] = null;
            }
            _icons?.Dispose();
            _looks?.Dispose();
        }

        void LateUpdate()
        {
            if (_canvas == null || localPlayers == null || cameras == null)
                return;
            float scale = Screen.height / 1080f;
            float now = Time.time;
            SplitMode mode = cameras.Layout.Mode;

            for (int slot = 0; slot < MaxSlots; slot++)
            {
                PlayerHudPanel panel = _panels[slot];
                if (!visible || !localPlayers.TryGetLocalPlayer(slot, out PlayerController player, out LocalPlayerInput input) ||
                    !player.HasLanded || !cameras.TryGetView(player.LocalSlot, out PlayerView view))
                {
                    panel?.Bind(null, null);
                    panel?.SetVisible(false);
                    continue;
                }

                panel = _panels[slot] ??= new PlayerHudPanel(_canvas, skin, (byte)slot, ItemName, id => Icons.Get(id));
                panel.Bind(player, input?.ControlScheme);
                panel.SetVisible(true);
                Rect own = PlayerScreenArea.Own(mode, player, view);
                // Merged, both players share one picture: their text may float anywhere on it.
                Rect r = view.ScreenRect;
                Rect text = mode == SplitMode.Merged
                    ? new Rect(r.x * Screen.width, (1f - r.y - r.height) * Screen.height, r.width * Screen.width, r.height * Screen.height)
                    : own;
                panel.Tick(view, own, text, scale, now);
            }

            VoxelWorld voxels = world != null ? world.World : null;
            _icons?.DrawQueued(voxels != null ? voxels.Blocks : default);
        }

        string ItemName(ushort itemId)
        {
            BlockRegistry blocks = world != null ? world.Blocks : null;
            if (items != null)
                return items.DisplayName(itemId, blocks);
            return blocks != null && blocks.TryGet(itemId, out BlockDefinition block) ? block.DisplayName : $"Item {itemId}";
        }
    }
}
