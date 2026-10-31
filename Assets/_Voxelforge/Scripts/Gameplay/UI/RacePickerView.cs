using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// One player's race window (Roadmap 4.7), in the class window's comic-book style: "Choose Your People" over a big
    /// card per race, each with its name on a band in its colour, a Blender-rendered portrait, its homeland and bonuses,
    /// a little lore, its racial passive and its racial ability. The cursor card glows in the player's team colour.
    /// <see cref="PlayerHudPanel"/> keeps it inside that player's own view (CLAUDE.md Law #5). It only shows what
    /// <see cref="PlayerRaceMenu"/> and the synced <see cref="PlayerRace"/> say; the mouse player can also point and click.
    /// </summary>
    sealed class RacePickerView
    {
        // Sizes in pixels at 1080p, roomy like the class window.
        const float Pad = 22f;
        const float TitleHeight = 44f;
        const float SubtitleHeight = 26f;
        const float SectionGap = 12f;
        const float CardWidth = 380f;
        const float CardHeight = 600f;
        const float CardGap = 18f;
        const float BandHeight = 54f;
        const float PortraitHeight = 190f;
        const float RacialIcon = 48f;
        const float HintHeight = 30f;
        const int MaxCards = 2;

        static readonly Color InkBrown = new Color(0.25f, 0.13f, 0.06f);
        static readonly Color Clear = new Color(1f, 1f, 1f, 0f);
        static readonly Color PortraitBack = new Color(0.78f, 0.7f, 0.56f);

        sealed class Card
        {
            public RectTransform Root;
            public Image Glow;
            public Image Band;
            public Image Crest;
            public TextMeshProUGUI Name;
            public Image Portrait;
            public TextMeshProUGUI Stats;
            public TextMeshProUGUI About;
            public TextMeshProUGUI Passive;
            public Image RacialIcon;
            public TextMeshProUGUI Racial;
        }

        readonly HudSkin _skin;
        readonly Card[] _cards = new Card[MaxCards];
        readonly TextMeshProUGUI _hints;
        RaceBook _bookShown;
        string _scheme;
        bool _waitingShown;

        public RacePickerView(RectTransform parent, HudSkin skin)
        {
            _skin = skin;
            Width = Pad * 2f + MaxCards * CardWidth + (MaxCards - 1) * CardGap;
            Height = Pad + TitleHeight + SubtitleHeight + SectionGap + CardHeight + SectionGap + HintHeight + Pad;
            Root = HudWidgets.Place(HudWidgets.Rect("Race Window", parent), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(Width, Height));
            HudWidgets.Stretch(HudWidgets.Image("Parchment", Root, skin.panel, Color.white, Image.Type.Sliced).rectTransform);

            float y = -Pad;
            TextMeshProUGUI title = HudWidgets.Label("Title", Root, skin.titleFont, 40f, InkBrown, TextAlignmentOptions.Center);
            Row(title.rectTransform, Pad, y, Width - Pad * 2f, TitleHeight);
            title.text = "Choose Your People";
            y -= TitleHeight;
            TextMeshProUGUI subtitle = HudWidgets.Label("Subtitle", Root, skin.titleFont, 20f, InkBrown, TextAlignmentOptions.Center);
            Row(subtitle.rectTransform, Pad, y, Width - Pad * 2f, SubtitleHeight);
            subtitle.text = "<i>You choose once: this is who your hero is.</i>";
            y -= SubtitleHeight + SectionGap;

            for (int i = 0; i < MaxCards; i++)
                _cards[i] = MakeCard(i, Pad + i * (CardWidth + CardGap), y);
            y -= CardHeight + SectionGap;

            _hints = HudWidgets.Label("Hints", Root, skin.titleFont, 22f, InkBrown, TextAlignmentOptions.Center);
            Row(_hints.rectTransform, Pad, y, Width - Pad * 2f, HintHeight);
        }

        public RectTransform Root { get; }

        /// <summary>Size in 1080p pixels.</summary>
        public float Width { get; }
        public float Height { get; }

        static RectTransform Row(RectTransform rect, float x, float y, float width, float height) =>
            HudWidgets.Place(rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(width, height));

        Card MakeCard(int index, float x, float y)
        {
            var c = new Card { Root = Row(HudWidgets.Rect($"Card {index + 1}", Root), x, y, CardWidth, CardHeight) };
            c.Glow = HudWidgets.Image("Glow", c.Root, _skin.slotFlash, Clear);
            HudWidgets.Stretch(c.Glow.rectTransform, -18f);
            HudWidgets.Stretch(HudWidgets.Image("Back", c.Root, _skin.panel, new Color(0.93f, 0.88f, 0.78f), Image.Type.Sliced).rectTransform);

            float inner = CardWidth - 24f;
            c.Band = HudWidgets.Image("Band", c.Root, _skin.barFill, Color.white);
            Row(c.Band.rectTransform, 6f, -6f, CardWidth - 12f, BandHeight);
            RectTransform crest = HudWidgets.Place(HudWidgets.Rect("Crest", c.Band.transform), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(3f, 0f), new Vector2(BandHeight - 6f, BandHeight - 6f));
            c.Crest = HudWidgets.Image("Picture", crest, null, Color.white);
            HudWidgets.Stretch(c.Crest.rectTransform, 3f);
            HudWidgets.Stretch(HudWidgets.Image("Frame", crest, _skin.slotFrame, Color.white).rectTransform);
            c.Name = HudWidgets.SmallLabel("Name", c.Band.transform, _skin, 30f, Color.white, TextAlignmentOptions.Center);
            FitInside(c.Name, 16f, 30f);
            HudWidgets.Place(c.Name.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(BandHeight, 0f),
                new Vector2(CardWidth - 12f - BandHeight - 6f, BandHeight - 6f));

            float cy = -6f - BandHeight - 8f;
            // The portrait, framed like a comic panel.
            RectTransform portrait = Row(HudWidgets.Rect("Portrait", c.Root), 12f, cy, inner, PortraitHeight);
            HudWidgets.Stretch(HudWidgets.Image("Back", portrait, null, PortraitBack).rectTransform);
            c.Portrait = HudWidgets.Image("Picture", portrait, null, Color.white);
            c.Portrait.preserveAspect = true;
            HudWidgets.Stretch(c.Portrait.rectTransform, 2f);
            HudWidgets.Stretch(HudWidgets.Image("Frame", portrait, _skin.barFrame, Color.white, Image.Type.Sliced).rectTransform, -2f);
            cy -= PortraitHeight + 8f;

            c.Stats = HudWidgets.Label("Stats", c.Root, _skin.titleFont, 19f, InkBrown, TextAlignmentOptions.Center);
            Wrap(c.Stats, 13f, 19f);
            Row(c.Stats.rectTransform, 12f, cy, inner, 46f);
            cy -= 50f;
            c.About = HudWidgets.Label("About", c.Root, _skin.titleFont, 18f, InkBrown, TextAlignmentOptions.TopLeft);
            Wrap(c.About, 12f, 18f);
            Row(c.About.rectTransform, 14f, cy, inner - 4f, 112f);
            cy -= 116f;
            c.Passive = HudWidgets.Label("Passive", c.Root, _skin.titleFont, 18f, InkBrown, TextAlignmentOptions.TopLeft);
            Wrap(c.Passive, 12f, 18f);
            Row(c.Passive.rectTransform, 14f, cy, inner - 4f, 60f);
            cy -= 66f;

            RectTransform racial = Row(HudWidgets.Rect("Racial", c.Root), 12f, cy, inner, RacialIcon + 8f);
            RectTransform slot = HudWidgets.Place(HudWidgets.Rect("Slot", racial), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(RacialIcon, RacialIcon));
            HudWidgets.Stretch(HudWidgets.Image("Back", slot, _skin.slotBack, Color.white).rectTransform, 3f);
            c.RacialIcon = HudWidgets.Image("Icon", slot, null, Color.white);
            HudWidgets.Stretch(c.RacialIcon.rectTransform, 4f);
            HudWidgets.Stretch(HudWidgets.Image("Frame", slot, _skin.slotFrame, Color.white).rectTransform);
            c.Racial = HudWidgets.Label("Name", racial, _skin.titleFont, 18f, InkBrown, TextAlignmentOptions.Left);
            Wrap(c.Racial, 12f, 18f);
            HudWidgets.Place(c.Racial.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(RacialIcon + 8f, 0f), new Vector2(inner - RacialIcon - 8f, RacialIcon + 8f));

            HudWidgets.Stretch(HudWidgets.Image("Frame", c.Root, _skin.barFrame, Color.white, Image.Type.Sliced).rectTransform, -2f);
            return c;
        }

        static void FitInside(TextMeshProUGUI label, float min, float max)
        {
            label.enableAutoSizing = true;
            label.fontSizeMin = min;
            label.fontSizeMax = max;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        /// <summary>Lets <paramref name="label"/> run onto more lines, shrinking to fit them.</summary>
        static void Wrap(TextMeshProUGUI label, float min, float max)
        {
            FitInside(label, min, max);
            label.textWrappingMode = TextWrappingModes.Normal;
        }

        public void SetScheme(string scheme)
        {
            if (_scheme == scheme)
                return;
            _scheme = scheme;
            WriteHints();
        }

        void WriteHints()
        {
            if (_waitingShown)
            {
                _hints.text = "<i>Asking the elders...</i>";
                return;
            }
            _hints.text = _scheme switch
            {
                "Gamepad" => Hints(("◄ ►", "Pick"), ("A", "Choose")),
                "KeyboardRight" => Hints(("Arrows", "Pick"), ("RCtrl", "Choose")),
                "KeyboardLeft" => Hints(("A D  1-2", "Pick"), ("Space", "Choose")),
                _ => Hints(("Click", "Choose"), ("A D  1-2", "Pick"), ("Space", "Choose")),
            };
        }

        static string Hints(params (string key, string action)[] pairs)
        {
            var text = new System.Text.StringBuilder();
            foreach ((string key, string action) in pairs)
            {
                if (text.Length > 0)
                    text.Append("      ");
                text.Append("<color=#8A2A10>").Append(key).Append("</color>  ").Append(action);
            }
            return text.ToString();
        }

        public void Tick(PlayerRaceMenu menu, Color teamColor, float now)
        {
            PlayerRace race = menu.Race;
            RaceBook book = race != null ? race.Book : null;
            if (book != _bookShown)
            {
                _bookShown = book;
                Fill(book);
            }
            if (menu.IsWaiting != _waitingShown)
            {
                _waitingShown = menu.IsWaiting;
                WriteHints();
            }
            if (menu.Scheme == "KeyboardMouse")
                HandleMouse(menu);

            int count = book != null ? Mathf.Min(book.Races.Count, MaxCards) : 0;
            for (int i = 0; i < count; i++)
            {
                Card c = _cards[i];
                bool cursor = menu.Model.Cursor == i;
                float pulse = 0.75f + 0.25f * Mathf.Sin(now * 5f);
                HudWidgets.SetColor(c.Glow, cursor ? new Color(teamColor.r, teamColor.g, teamColor.b, pulse) : Clear);
                float size = cursor ? 1.03f : 1f;
                if (!Mathf.Approximately(c.Root.localScale.x, size))
                    c.Root.localScale = new Vector3(size, size, 1f);
            }
        }

        /// <summary>Writes each race's card. Only when the book changes, so building strings here is fine.</summary>
        void Fill(RaceBook book)
        {
            int count = book != null ? book.Races.Count : 0;
            for (int i = 0; i < MaxCards; i++)
            {
                Card c = _cards[i];
                RaceDefinition definition = i < count ? book.Races[i] : null;
                HudWidgets.SetActive(c.Root, definition != null);
                if (definition == null)
                    continue;
                c.Band.color = _skin.RaceColor(definition.Id);
                Sprite crest = _skin.CrestFor(definition.Id);
                c.Crest.sprite = crest;
                c.Crest.enabled = crest != null;
                c.Name.text = definition.DisplayName;
                Sprite portrait = _skin.PortraitFor(definition.Id);
                c.Portrait.sprite = portrait;
                c.Portrait.enabled = portrait != null;
                c.Stats.text = $"<b>Homeland:</b> {definition.Homeland}\n{Bonuses(definition)}";
                c.About.text = definition.Description;
                c.Passive.text = $"<b><color=#8A2A10>{definition.PassiveName}</color></b>  {definition.PassiveDescription}";
                AbilityDefinition racial = definition.RacialAbility;
                c.RacialIcon.sprite = racial != null ? _skin.IconFor(racial) : _skin.fallbackIcon;
                c.Racial.text = racial != null
                    ? $"<b><color=#8A2A10>{racial.DisplayName}</color></b> <size=80%>(slot 9)</size>\n<size=85%>{racial.Description}</size>"
                    : "<i>No racial yet</i>";
            }
        }

        static string Bonuses(RaceDefinition definition)
        {
            var text = new System.Text.StringBuilder();
            if (definition.BonusHealth != 0f)
                text.Append($"<color=#A3221B>{definition.BonusHealth:+0;-0} Health</color>   ");
            if (definition.BonusMana != 0f)
                text.Append($"<color=#1F4FB0>{definition.BonusMana:+0;-0} Mana</color>   ");
            if (definition.BonusManaRegen != 0f)
                text.Append($"<color=#1F4FB0>{definition.BonusManaRegen:+0.#;-0.#} Mana/s</color>");
            return text.Length > 0 ? text.ToString().TrimEnd() : "No bonuses";
        }

        /// <summary>The keyboard + mouse player points at a card and clicks to choose it.</summary>
        void HandleMouse(PlayerRaceMenu menu)
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;
            Vector2 point = mouse.position.ReadValue();
            for (int i = 0; i < MaxCards; i++)
            {
                if (!_cards[i].Root.gameObject.activeSelf || !RectTransformUtility.RectangleContainsScreenPoint(_cards[i].Root, point, null))
                    continue;
                menu.Model.PointAt(i);
                if (mouse.leftButton.wasPressedThisFrame)
                    menu.Choose();
                return;
            }
        }
    }
}
