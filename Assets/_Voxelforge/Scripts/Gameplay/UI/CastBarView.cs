using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// One player's cast bar (Roadmap 4.4): a wood-and-ink frame above their action bar, the spell's icon and name in the
    /// medieval lettering, and a fill as the cast goes on. When the spell goes off the bar fills, flashes white and fades;
    /// when it's stopped it turns red and fades. Timed on this PC from the host's cues (<see cref="PlayerCaster"/>).
    /// </summary>
    sealed class CastBarView
    {
        const float FadeSeconds = 0.35f;

        readonly HudSkin _skin;
        readonly CanvasGroup _group;
        readonly Image _fill;
        readonly Image _icon;
        readonly TextMeshProUGUI _name;
        readonly TextMeshProUGUI _time;
        AbilityDefinition _shown;
        int _tenthsShown = -1;
        float _fadeStart = float.NegativeInfinity;
        bool _fading;

        /// <param name="parent">The action bar strip; the cast bar floats above it and its mana bar.</param>
        public CastBarView(RectTransform parent, HudSkin skin)
        {
            _skin = skin;
            float height = skin.castBarHeight;
            Root = HudWidgets.Place(HudWidgets.Rect("Cast Bar", parent), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f),
                new Vector2(height * 0.5f, skin.manaBarHeight + 30f), new Vector2(skin.castBarWidth, height));
            _group = Root.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;

            HudWidgets.Stretch(HudWidgets.Image("Back", Root, skin.barBack, Color.white).rectTransform, 2f);
            _fill = HudWidgets.Image("Fill", Root, skin.barFill, skin.cast);
            _fill.type = Image.Type.Filled;
            _fill.fillMethod = Image.FillMethod.Horizontal;
            HudWidgets.Stretch(_fill.rectTransform, 2f);
            HudWidgets.Stretch(HudWidgets.Image("Frame", Root, skin.barFrame, Color.white, Image.Type.Sliced).rectTransform, -4f);

            // The icon hangs off the left end in its own slot frame.
            float iconSize = height * 1.7f;
            RectTransform iconRoot = HudWidgets.Place(HudWidgets.Rect("Icon", Root), new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(2f, 0f), new Vector2(iconSize, iconSize));
            HudWidgets.Stretch(HudWidgets.Image("Back", iconRoot, skin.slotBack, Color.white).rectTransform, 3f);
            _icon = HudWidgets.Image("Picture", iconRoot, null, Color.white);
            HudWidgets.Stretch(_icon.rectTransform, 4f);
            HudWidgets.Stretch(HudWidgets.Image("Frame", iconRoot, skin.slotFrame, Color.white).rectTransform);

            _name = HudWidgets.Label("Name", Root, skin.titleFont, height * 1.15f, new Color(1f, 0.93f, 0.75f),
                TextAlignmentOptions.Bottom);
            HudWidgets.Place(_name.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 2f),
                new Vector2(skin.castBarWidth, height * 1.4f));
            _time = HudWidgets.SmallLabel("Time", Root, skin, height * 0.9f, Color.white, TextAlignmentOptions.Right);
            HudWidgets.Stretch(_time.rectTransform, 6f);
        }

        public RectTransform Root { get; }

        /// <summary>The spell went off: show the bar full and flash it.</summary>
        public void Released(AbilityDefinition ability, float now)
        {
            if (_shown != ability)
                return;
            _fill.fillAmount = 1f;
            _fill.color = Color.white;
            StartFade(now);
        }

        public void Tick(PlayerCaster caster, float now)
        {
            AbilityDefinition casting = caster != null ? caster.Casting : null;
            if (casting != null)
            {
                if (_shown != casting || _fading)
                {
                    _shown = casting;
                    _fading = false;
                    _name.text = casting.DisplayName;
                    _icon.sprite = _skin.IconFor(casting);
                    _fill.color = _skin.cast;
                    _tenthsShown = -1;
                }
                _group.alpha = 1f;
                float progress = caster.CastProgress;
                if (!Mathf.Approximately(_fill.fillAmount, progress))
                    _fill.fillAmount = progress;
                int tenths = Mathf.CeilToInt((1f - progress) * casting.CastTime * 10f);
                if (tenths != _tenthsShown)
                {
                    _tenthsShown = tenths;
                    _time.SetText("{0:1}", tenths / 10f);
                }
                return;
            }

            // The cast ended without going off: stopped.
            if (_shown != null && !_fading)
            {
                _fill.color = _skin.refusal;
                StartFade(now);
            }
            if (_fading)
            {
                float t = Mathf.Clamp01((now - _fadeStart) / FadeSeconds);
                _group.alpha = 1f - t * t;
                if (t >= 1f)
                {
                    _fading = false;
                    _shown = null;
                }
            }
        }

        void StartFade(float now)
        {
            _fading = true;
            _fadeStart = now;
            _time.SetText(string.Empty);
        }
    }
}
