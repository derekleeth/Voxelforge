using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The chip over the right end of one player's action bar while they're under an elixir (Roadmap 5.8): its icon, what
    /// it gives and the time left, turning red for the last 30 seconds. It sits on that player's own bar, so it never
    /// lands on someone else's view (CLAUDE.md Law #5). It only reads what the host shared (<see cref="PlayerConsumables"/>).
    /// </summary>
    sealed class BuffChipView
    {
        const float Width = 168f;
        const float Height = 44f;
        const float RunningOutSeconds = 30f;
        static readonly Color Plenty = new Color(1f, 0.95f, 0.8f);
        static readonly Color RunningOut = new Color(1f, 0.5f, 0.4f);

        readonly HudSkin _skin;
        readonly RectTransform _root;
        readonly Image _icon;
        readonly TextMeshProUGUI _stats;
        readonly TextMeshProUGUI _time;
        AbilityDefinition _shown;
        int _secondsShown = -1;

        /// <param name="bar">The action bar strip; the chip floats above its right end, clear of the cast bar.</param>
        public BuffChipView(RectTransform bar, HudSkin skin)
        {
            _skin = skin;
            _root = HudWidgets.Place(HudWidgets.Rect("Elixir", bar), new Vector2(1f, 1f), new Vector2(1f, 0f),
                new Vector2(-10f, skin.manaBarHeight * 2f + 14f), new Vector2(Width, Height));
            HudWidgets.Stretch(HudWidgets.Image("Back", _root, skin.barBack, Color.white).rectTransform, 2f);
            HudWidgets.Stretch(HudWidgets.Image("Frame", _root, skin.barFrame, Color.white, Image.Type.Sliced).rectTransform, -3f);

            RectTransform icon = HudWidgets.Place(HudWidgets.Rect("Icon", _root), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(3f, 0f), new Vector2(Height - 6f, Height - 6f));
            _icon = HudWidgets.Image("Picture", icon, null, Color.white);
            HudWidgets.Stretch(_icon.rectTransform);

            _stats = HudWidgets.SmallLabel("Stats", _root, skin, Height * 0.42f, Plenty, TextAlignmentOptions.Left);
            HudWidgets.Place(_stats.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Height, -2f),
                new Vector2(Width - Height - 4f, Height * 0.5f));
            _time = HudWidgets.SmallLabel("Time", _root, skin, Height * 0.42f, Plenty, TextAlignmentOptions.Left);
            HudWidgets.Place(_time.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(Height, 2f),
                new Vector2(Width - Height - 4f, Height * 0.5f));
            HudWidgets.SetActive(_root, false);
        }

        public void Tick(PlayerConsumables consumables)
        {
            AbilityDefinition elixir = consumables != null ? consumables.Elixir : null;
            HudWidgets.SetActive(_root, elixir != null);
            if (elixir == null)
            {
                _shown = null;
                return;
            }
            if (_shown != elixir)
            {
                _shown = elixir;
                _icon.sprite = _skin.IconFor(elixir);
                _stats.text = GearText.Bonus(elixir.Buff);
                _secondsShown = -1;
            }
            int seconds = Mathf.CeilToInt(consumables.ElixirSecondsLeft);
            if (seconds == _secondsShown)
                return;
            _secondsShown = seconds;
            _time.text = $"{seconds / 60}:{seconds % 60:00}";
            _time.color = seconds <= RunningOutSeconds ? RunningOut : Plenty;
        }
    }
}
