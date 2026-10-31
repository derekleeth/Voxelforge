using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// What a knocked-out player sees (Roadmap 5.2), over their own view only (CLAUDE.md Law #5): the view fades dark, a
    /// big comic "KNOCKED OUT!" lands, and a countdown says where they'll wake. It reads what every PC already knows
    /// (<see cref="PlayerController.IsKnockedOut"/>, <see cref="PlayerKnockout.WakeAtLocal"/>), so nothing is sent for it.
    /// </summary>
    sealed class KnockoutView
    {
        const float FadeIn = 0.6f;
        const float FadeOut = 0.35f;
        const float MaxDim = 0.55f;

        readonly CanvasGroup _group;
        readonly Image _dim;
        readonly TextMeshProUGUI _title;
        readonly TextMeshProUGUI _countdown;
        float _shown;
        int _secondsShown = -1;
        bool _homeShown;

        public KnockoutView(RectTransform parent, HudSkin skin)
        {
            Root = HudWidgets.Stretch(HudWidgets.Rect("Knocked Out", parent));
            _group = Root.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;

            _dim = HudWidgets.Image("Dim", Root, skin.white, new Color(0.08f, 0.02f, 0.04f, MaxDim));
            HudWidgets.Stretch(_dim.rectTransform);

            _title = HudWidgets.Label("Title", Root, skin.comicFont, 96f, skin.health, TextAlignmentOptions.Center);
            _title.outlineWidth = 0.25f;
            _title.outlineColor = skin.ink;
            HudWidgets.Place(_title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 60f),
                new Vector2(900f, 130f));
            _title.text = "KNOCKED OUT!";

            _countdown = HudWidgets.SmallLabel("Countdown", Root, skin, 38f, Color.white, TextAlignmentOptions.Center);
            HudWidgets.Place(_countdown.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -30f),
                new Vector2(900f, 60f));
            HudWidgets.SetActive(Root, false);
        }

        public RectTransform Root { get; }

        public void Tick(PlayerController player, PlayerKnockout knockout, float now)
        {
            bool down = player != null && player.IsKnockedOut;
            _shown = Mathf.MoveTowards(_shown, down ? 1f : 0f, Time.deltaTime / (down ? FadeIn : FadeOut));
            bool visible = _shown > 0f;
            HudWidgets.SetActive(Root, visible);
            if (!visible)
            {
                _secondsShown = -1;
                return;
            }
            _group.alpha = _shown;

            // The title lands big and settles, like a comic sound effect.
            float punch = down ? 1f + 0.6f * (1f - Mathf.SmoothStep(0f, 1f, _shown)) : 1f;
            _title.rectTransform.localScale = new Vector3(punch, punch, 1f);

            bool home = knockout != null && knockout.WakesAtHome;
            int seconds = knockout != null ? Mathf.Max(0, Mathf.CeilToInt(knockout.WakeAtLocal - now)) : 0;
            if (seconds == _secondsShown && home == _homeShown)
                return;
            _secondsShown = seconds;
            _homeShown = home;
            string where = home ? "your Home Totem" : "the world's spawn";
            _countdown.text = seconds > 0 ? $"Waking at {where} in {seconds}..." : $"Waking at {where}...";
        }
    }
}
