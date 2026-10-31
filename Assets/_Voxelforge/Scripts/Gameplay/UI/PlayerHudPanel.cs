using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Voxelforge.Voxels;
using Object = UnityEngine.Object;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Everything the 4.4 HUD draws for one local player: their action bar and mana, their cast bar, their floating
    /// combat text, their inventory (4.5), class (4.6) and race (4.7) windows, and their minimap (4.8). <see cref="CombatHud"/> fits it to that player's own part of the screen every frame, so it never
    /// lands on someone else's view (CLAUDE.md Law #5). Each part sits on its own nested canvas, so a cooldown ticking
    /// or a number floating up doesn't rebuild the frames around it.
    /// </summary>
    sealed class PlayerHudPanel : IDisposable
    {
        const int TextCapacity = 24;

        readonly RectTransform _barArea;
        readonly RectTransform _barGroup;
        readonly RectTransform _textArea;
        readonly RectTransform _inventoryArea;
        readonly InventoryView _inventoryView;
        readonly RectTransform _classArea;
        readonly ClassPickerView _classView;
        readonly RectTransform _raceArea;
        readonly RacePickerView _raceView;
        readonly ActionBarView _actionBar;
        readonly CastBarView _castBar;
        readonly CombatTextView _textView;
        readonly TargetBarsView _targetBars;
        readonly RectTransform _mapArea;
        readonly MinimapView _minimap;
        readonly CombatTextFeed _feed = new CombatTextFeed(TextCapacity);

        PlayerController _player;
        PlayerCaster _caster;
        PlayerHealth _health;
        PlayerActionBar _bar;
        PlayerAbilityCues _cues;
        PlayerCombatText _combatText;
        PlayerInventory _inventory;
        PlayerClassMenu _classMenu;
        PlayerRaceMenu _raceMenu;
        PlayerExplorer _explorer;

        public PlayerHudPanel(RectTransform canvas, HudSkin skin, byte slot, Func<ushort, string> itemName,
            Func<ushort, Texture> itemIcon)
        {
            _barArea = NestedCanvas($"Player {slot + 1} HUD", canvas);
            _barGroup = HudWidgets.Rect("Bottom", _barArea);
            _actionBar = new ActionBarView(_barGroup, skin);
            HudWidgets.Place(_barGroup, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f),
                new Vector2(_actionBar.Width, _actionBar.Height));
            _castBar = new CastBarView(_actionBar.Root, skin);

            // Combat text gets its own area and canvas: it clips to the player's view, which in a merged view is wider
            // than the half their bar sits in.
            _textArea = NestedCanvas($"Player {slot + 1} Combat Text", canvas);
            _textArea.gameObject.AddComponent<RectMask2D>();
            // Target health bars (Roadmap 4.6) first, so combat text draws over them.
            _targetBars = new TargetBarsView(_textArea, skin);
            _textView = new CombatTextView(_textArea, skin, TextCapacity, itemName);

            // The minimap (Roadmap 4.8), on its own canvas: it moves every frame, and nothing else should rebuild for it.
            _mapArea = NestedCanvas($"Player {slot + 1} Map", canvas);
            _minimap = new MinimapView(_mapArea, skin);

            // The inventory window (Roadmap 4.5) on top, on its own canvas so its slots don't rebuild the bar.
            _inventoryArea = NestedCanvas($"Player {slot + 1} Inventory", canvas);
            _inventoryView = new InventoryView(_inventoryArea, skin, itemIcon, itemName);
            HudWidgets.SetActive(_inventoryArea, false);

            // The class window (Roadmap 4.6), on its own canvas like the inventory.
            _classArea = NestedCanvas($"Player {slot + 1} Class", canvas);
            _classView = new ClassPickerView(_classArea, skin);
            HudWidgets.SetActive(_classArea, false);

            // The race window (Roadmap 4.7), the same way.
            _raceArea = NestedCanvas($"Player {slot + 1} Race", canvas);
            _raceView = new RacePickerView(_raceArea, skin);
            HudWidgets.SetActive(_raceArea, false);
        }

        static RectTransform NestedCanvas(string name, RectTransform parent)
        {
            RectTransform rect = HudWidgets.Rect(name, parent);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.gameObject.AddComponent<Canvas>();
            return rect;
        }

        /// <summary>Follows <paramref name="player"/> (null to let go), listening to their spells and combat text.</summary>
        public void Bind(PlayerController player, string controlScheme)
        {
            _actionBar.SetScheme(controlScheme);
            _inventoryView.SetScheme(controlScheme);
            _classView.SetScheme(controlScheme);
            _raceView.SetScheme(controlScheme);
            if (_inventory != null)
                _inventory.Scheme = controlScheme;
            if (_classMenu != null)
                _classMenu.Scheme = controlScheme;
            if (_raceMenu != null)
                _raceMenu.Scheme = controlScheme;
            if (_player == player)
                return;
            if (_cues != null)
                _cues.Received -= OnCue;
            if (_combatText != null)
                _combatText.Raised -= OnCombatText;
            _feed.Clear();

            _player = player;
            _caster = player != null ? player.GetComponent<PlayerCaster>() : null;
            _health = player != null ? player.GetComponent<PlayerHealth>() : null;
            _bar = player != null ? player.GetComponent<PlayerActionBar>() : null;
            _cues = player != null ? player.GetComponent<PlayerAbilityCues>() : null;
            _combatText = player != null ? player.GetComponent<PlayerCombatText>() : null;
            _inventory = player != null ? player.GetComponent<PlayerInventory>() : null;
            if (_inventory != null)
                _inventory.Scheme = controlScheme;
            _classMenu = player != null ? player.GetComponent<PlayerClassMenu>() : null;
            if (_classMenu != null)
                _classMenu.Scheme = controlScheme;
            _raceMenu = player != null ? player.GetComponent<PlayerRaceMenu>() : null;
            if (_raceMenu != null)
                _raceMenu.Scheme = controlScheme;
            _explorer = player != null ? player.GetComponent<PlayerExplorer>() : null;
            if (_cues != null)
                _cues.Received += OnCue;
            if (_combatText != null)
                _combatText.Raised += OnCombatText;
        }

        public void SetVisible(bool visible)
        {
            HudWidgets.SetActive(_barArea, visible);
            HudWidgets.SetActive(_textArea, visible);
            HudWidgets.SetActive(_mapArea, visible);
            if (!visible)
            {
                HudWidgets.SetActive(_inventoryArea, false);
                HudWidgets.SetActive(_classArea, false);
                HudWidgets.SetActive(_raceArea, false);
            }
        }

        /// <param name="ownArea">The player's own part of the screen (GUI pixels), for the bars.</param>
        /// <param name="textArea">Where their combat text may go (GUI pixels).</param>
        /// <param name="scale">GUI pixels per 1080p pixel.</param>
        /// <param name="targets">Health of the targets being fought (Roadmap 4.6), shared by every panel.</param>
        /// <param name="mapTiles">The painted map (Roadmap 4.8), shared by every panel. Null before the world starts.</param>
        /// <param name="markers">This frame's map markers: every hero and Home Totem (Roadmap 4.8).</param>
        public void Tick(PlayerView view, Rect ownArea, Rect textArea, float scale, TargetHealthBook targets,
            MapTileCache mapTiles, List<MapMarker> markers, float now)
        {
            Fit(_barArea, ownArea, scale);
            Fit(_textArea, textArea, scale);
            Fit(_mapArea, ownArea, scale);
            if (_player != null)
            {
                // A quarter of the screen gets a smaller map, so it doesn't crowd the view.
                float fit = Mathf.Clamp(Mathf.Min(ownArea.width, ownArea.height) / scale / 900f, 0.6f, 1f);
                _minimap.Tick(mapTiles, _explorer != null ? _explorer.Map : null, _player, markers, fit);
            }

            // Shrink the bar to fit a narrow view (a quarter of the screen, or a Voronoi half).
            float room = ownArea.width / scale - 24f;
            float shrink = Mathf.Min(1f, room / _actionBar.Width);
            if (!Mathf.Approximately(_barGroup.localScale.x, shrink))
                _barGroup.localScale = new Vector3(shrink, shrink, 1f);

            PlayerButtons buttons = _player != null ? _player.OwnerInput.Buttons : PlayerButtons.None;
            _actionBar.Tick(_bar, _caster, _health, buttons, now);
            _castBar.Tick(_caster, now);
            if (_player != null)
                _targetBars.Tick(targets, view, textArea, scale, _player.transform.position, now);
            _feed.Expire(now);
            _textView.Tick(_feed, view, textArea, scale, now);

            TickClassWindow(ownArea, scale, shrink, now);
            TickRaceWindow(ownArea, scale, shrink, now);

            bool open = _inventory != null && _inventory.IsOpen;
            HudWidgets.SetActive(_inventoryArea, open);
            if (open)
            {
                Fit(_inventoryArea, ownArea, scale);
                // Centred in the player's view, above their action bar, shrunk to fit a small view.
                float barRoom = (_actionBar.Height + 24f) * shrink;
                // An open chest's pane (4.5) sits above the window: fit both, and centre them together.
                float extra = _inventoryView.ExtraHeight;
                float fit = Mathf.Min(1f, (ownArea.width / scale - 24f) / _inventoryView.Width,
                    (ownArea.height / scale - barRoom - 24f) / (_inventoryView.Height + extra));
                RectTransform window = _inventoryView.Root;
                if (!Mathf.Approximately(window.localScale.x, fit))
                    window.localScale = new Vector3(fit, fit, 1f);
                var offset = new Vector2(0f, (barRoom - extra * fit) * 0.5f);
                if (window.anchoredPosition != offset)
                    window.anchoredPosition = offset;
                _inventoryView.Tick(_inventory, _player.TeamColor, now);
            }
        }

        /// <summary>The class window (Roadmap 4.6): centred in the player's view above their action bar, shrunk to fit.</summary>
        void TickClassWindow(Rect ownArea, float scale, float shrink, float now)
        {
            bool open = _classMenu != null && _classMenu.IsOpen;
            HudWidgets.SetActive(_classArea, open);
            if (!open)
                return;
            Fit(_classArea, ownArea, scale);
            float barRoom = (_actionBar.Height + 24f) * shrink;
            float fit = Mathf.Min(1f, (ownArea.width / scale - 24f) / _classView.Width,
                (ownArea.height / scale - barRoom - 24f) / _classView.Height);
            RectTransform window = _classView.Root;
            if (!Mathf.Approximately(window.localScale.x, fit))
                window.localScale = new Vector3(fit, fit, 1f);
            var offset = new Vector2(0f, barRoom * 0.5f);
            if (window.anchoredPosition != offset)
                window.anchoredPosition = offset;
            _classView.Tick(_classMenu, _player.TeamColor, now);
        }

        /// <summary>The race window (Roadmap 4.7): centred in the player's view above their action bar, shrunk to fit.</summary>
        void TickRaceWindow(Rect ownArea, float scale, float shrink, float now)
        {
            bool open = _raceMenu != null && _raceMenu.IsOpen;
            HudWidgets.SetActive(_raceArea, open);
            if (!open)
                return;
            Fit(_raceArea, ownArea, scale);
            float barRoom = (_actionBar.Height + 24f) * shrink;
            float fit = Mathf.Min(1f, (ownArea.width / scale - 24f) / _raceView.Width,
                (ownArea.height / scale - barRoom - 24f) / _raceView.Height);
            RectTransform window = _raceView.Root;
            if (!Mathf.Approximately(window.localScale.x, fit))
                window.localScale = new Vector3(fit, fit, 1f);
            var offset = new Vector2(0f, barRoom * 0.5f);
            if (window.anchoredPosition != offset)
                window.anchoredPosition = offset;
            _raceView.Tick(_raceMenu, _player.TeamColor, now);
        }

        /// <summary>Lays <paramref name="rect"/> over <paramref name="gui"/> (GUI pixels, y down) on the overlay canvas (y up).</summary>
        static void Fit(RectTransform rect, Rect gui, float scale)
        {
            var position = new Vector2(gui.x, Screen.height - gui.yMax);
            var size = new Vector2(gui.width / scale, gui.height / scale);
            if (rect.anchoredPosition != position)
                rect.anchoredPosition = position;
            if (rect.sizeDelta != size)
                rect.sizeDelta = size;
            if (!Mathf.Approximately(rect.localScale.x, scale))
                rect.localScale = new Vector3(scale, scale, 1f);
        }

        void OnCue(AbilityCue cue, AbilityDefinition ability)
        {
            if (cue.Kind != AbilityCueKind.Released)
                return;
            _actionBar.Flash(ability, Time.time);
            _castBar.Released(ability, Time.time);
        }

        void OnCombatText(CombatTextEvent e) => _feed.Push(e, Time.time);

        public void Dispose()
        {
            Bind(null, null);
            if (_barArea != null)
                Object.Destroy(_barArea.gameObject);
            if (_textArea != null)
                Object.Destroy(_textArea.gameObject);
            if (_inventoryArea != null)
                Object.Destroy(_inventoryArea.gameObject);
            if (_classArea != null)
                Object.Destroy(_classArea.gameObject);
            if (_raceArea != null)
                Object.Destroy(_raceArea.gameObject);
            _minimap.Dispose();
            if (_mapArea != null)
                Object.Destroy(_mapArea.gameObject);
        }
    }
}
