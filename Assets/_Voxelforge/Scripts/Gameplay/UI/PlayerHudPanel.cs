using System;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Everything the 4.4 HUD draws for one local player: their action bar and mana, their cast bar, and their floating
    /// combat text. <see cref="CombatHud"/> fits it to that player's own part of the screen every frame, so it never
    /// lands on someone else's view (CLAUDE.md Law #5). Each part sits on its own nested canvas, so a cooldown ticking
    /// or a number floating up doesn't rebuild the frames around it.
    /// </summary>
    sealed class PlayerHudPanel : IDisposable
    {
        const int TextCapacity = 24;

        readonly RectTransform _barArea;
        readonly RectTransform _barGroup;
        readonly RectTransform _textArea;
        readonly ActionBarView _actionBar;
        readonly CastBarView _castBar;
        readonly CombatTextView _textView;
        readonly CombatTextFeed _feed = new CombatTextFeed(TextCapacity);

        PlayerController _player;
        PlayerCaster _caster;
        PlayerActionBar _bar;
        PlayerAbilityCues _cues;
        PlayerCombatText _combatText;

        public PlayerHudPanel(RectTransform canvas, HudSkin skin, byte slot, Func<ushort, string> itemName)
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
            _textView = new CombatTextView(_textArea, skin, TextCapacity, itemName);
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
            if (_player == player)
                return;
            if (_cues != null)
                _cues.Received -= OnCue;
            if (_combatText != null)
                _combatText.Raised -= OnCombatText;
            _feed.Clear();

            _player = player;
            _caster = player != null ? player.GetComponent<PlayerCaster>() : null;
            _bar = player != null ? player.GetComponent<PlayerActionBar>() : null;
            _cues = player != null ? player.GetComponent<PlayerAbilityCues>() : null;
            _combatText = player != null ? player.GetComponent<PlayerCombatText>() : null;
            if (_cues != null)
                _cues.Received += OnCue;
            if (_combatText != null)
                _combatText.Raised += OnCombatText;
        }

        public void SetVisible(bool visible)
        {
            HudWidgets.SetActive(_barArea, visible);
            HudWidgets.SetActive(_textArea, visible);
        }

        /// <param name="ownArea">The player's own part of the screen (GUI pixels), for the bars.</param>
        /// <param name="textArea">Where their combat text may go (GUI pixels).</param>
        /// <param name="scale">GUI pixels per 1080p pixel.</param>
        public void Tick(PlayerView view, Rect ownArea, Rect textArea, float scale, float now)
        {
            Fit(_barArea, ownArea, scale);
            Fit(_textArea, textArea, scale);

            // Shrink the bar to fit a narrow view (a quarter of the screen, or a Voronoi half).
            float room = ownArea.width / scale - 24f;
            float shrink = Mathf.Min(1f, room / _actionBar.Width);
            if (!Mathf.Approximately(_barGroup.localScale.x, shrink))
                _barGroup.localScale = new Vector3(shrink, shrink, 1f);

            PlayerButtons buttons = _player != null ? _player.OwnerInput.Buttons : PlayerButtons.None;
            _actionBar.Tick(_bar, _caster, buttons, now);
            _castBar.Tick(_caster, now);
            _feed.Expire(now);
            _textView.Tick(_feed, view, textArea, scale, now);
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
        }
    }
}
