using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// One player's class window (Roadmap 4.6): a parchment comic-book frame with a card per class, each with the class's
    /// name on a band in its colour, its health and mana, a few words about it and its three signature spells. The cursor
    /// card glows in the player's team colour; the hero's own class is stamped CHOSEN. <see cref="PlayerHudPanel"/> keeps
    /// it inside that player's own view (CLAUDE.md Law #5). It only shows what <see cref="PlayerClassMenu"/> and the
    /// synced <see cref="PlayerClass"/> say; the mouse player can also point and click.
    /// </summary>
    sealed class ClassPickerView
    {
        // Sizes in pixels at 1080p, roomy like the inventory (small outlined lettering smears).
        const float Pad = 22f;
        const float TitleHeight = 44f;
        const float SectionGap = 12f;
        const float CardWidth = 236f;
        const float CardHeight = 410f;
        const float CardGap = 14f;
        const float BandHeight = 52f;
        const float SpellIcon = 44f;
        const float HintHeight = 30f;
        const int MaxCards = 4;
        const int SignatureSpells = 3;

        static readonly Color InkBrown = new Color(0.25f, 0.13f, 0.06f);
        static readonly Color StampRed = new Color(0.62f, 0.12f, 0.06f);
        static readonly Color Clear = new Color(1f, 1f, 1f, 0f);
        static readonly Color Faded = new Color(1f, 1f, 1f, 0.3f);

        sealed class Card
        {
            public RectTransform Root;
            public Image Glow;
            public Image Band;
            public Image Crest;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Stamp;
            public TextMeshProUGUI Stats;
            public TextMeshProUGUI About;
            public readonly Image[] Icons = new Image[SignatureSpells];
            public readonly TextMeshProUGUI[] Spells = new TextMeshProUGUI[SignatureSpells];
        }

        readonly HudSkin _skin;
        readonly Card[] _cards = new Card[MaxCards];
        readonly TextMeshProUGUI _hints;
        ClassBook _bookShown;
        string _scheme;

        public ClassPickerView(RectTransform parent, HudSkin skin)
        {
            _skin = skin;
            Width = Pad * 2f + MaxCards * CardWidth + (MaxCards - 1) * CardGap;
            Height = Pad + TitleHeight + SectionGap + CardHeight + SectionGap + HintHeight + Pad;
            Root = HudWidgets.Place(HudWidgets.Rect("Class Window", parent), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(Width, Height));
            HudWidgets.Stretch(HudWidgets.Image("Parchment", Root, skin.panel, Color.white, Image.Type.Sliced).rectTransform);

            float y = -Pad;
            TextMeshProUGUI title = HudWidgets.Label("Title", Root, skin.titleFont, 38f, InkBrown, TextAlignmentOptions.Center);
            Row(title.rectTransform, Pad, y, Width - Pad * 2f, TitleHeight);
            title.text = "Choose Your Calling";
            y -= TitleHeight + SectionGap;

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

            float inner = CardWidth - 20f;
            c.Band = HudWidgets.Image("Band", c.Root, _skin.barFill, Color.white);
            Row(c.Band.rectTransform, 6f, -6f, CardWidth - 12f, BandHeight);
            // The class's crest at the left of its band, framed like a spell slot (Roadmap 4.6 step F).
            RectTransform crest = HudWidgets.Place(HudWidgets.Rect("Crest", c.Band.transform), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(3f, 0f), new Vector2(BandHeight - 6f, BandHeight - 6f));
            c.Crest = HudWidgets.Image("Picture", crest, null, Color.white);
            HudWidgets.Stretch(c.Crest.rectTransform, 3f);
            HudWidgets.Stretch(HudWidgets.Image("Frame", crest, _skin.slotFrame, Color.white).rectTransform);
            c.Name = HudWidgets.SmallLabel("Name", c.Band.transform, _skin, 26f, Color.white, TextAlignmentOptions.Center);
            FitInside(c.Name, 15f, 26f);
            HudWidgets.Place(c.Name.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(BandHeight, 0f),
                new Vector2(CardWidth - 12f - BandHeight - 6f, BandHeight - 6f));

            float cy = -6f - BandHeight - 6f;
            c.Stats = HudWidgets.Label("Stats", c.Root, _skin.titleFont, 20f, InkBrown, TextAlignmentOptions.Center);
            Row(c.Stats.rectTransform, 10f, cy, inner, 26f);
            cy -= 30f;
            c.About = HudWidgets.Label("About", c.Root, _skin.titleFont, 19f, InkBrown, TextAlignmentOptions.TopLeft);
            FitInside(c.About, 13f, 19f);
            Row(c.About.rectTransform, 12f, cy, inner - 4f, 130f);
            cy -= 138f;

            for (int s = 0; s < SignatureSpells; s++)
            {
                RectTransform spell = Row(HudWidgets.Rect($"Spell {s + 1}", c.Root), 10f, cy - s * (SpellIcon + 6f), inner, SpellIcon);
                RectTransform slot = HudWidgets.Place(HudWidgets.Rect("Slot", spell), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    Vector2.zero, new Vector2(SpellIcon, SpellIcon));
                HudWidgets.Stretch(HudWidgets.Image("Back", slot, _skin.slotBack, Color.white).rectTransform, 3f);
                c.Icons[s] = HudWidgets.Image("Icon", slot, null, Clear);
                HudWidgets.Stretch(c.Icons[s].rectTransform, 4f);
                HudWidgets.Stretch(HudWidgets.Image("Frame", slot, _skin.slotFrame, Color.white).rectTransform);
                c.Spells[s] = HudWidgets.Label("Name", spell, _skin.titleFont, 20f, InkBrown, TextAlignmentOptions.Left);
                FitInside(c.Spells[s], 13f, 20f);
                HudWidgets.Place(c.Spells[s].rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(SpellIcon + 8f, 0f), new Vector2(inner - SpellIcon - 8f, SpellIcon));
            }

            HudWidgets.Stretch(HudWidgets.Image("Frame", c.Root, _skin.barFrame, Color.white, Image.Type.Sliced).rectTransform, -2f);
            // A rubber stamp across the corner of the hero's own class.
            c.Stamp = HudWidgets.Label("Chosen", c.Root, _skin.comicFont, 30f, StampRed, TextAlignmentOptions.Center);
            HudWidgets.Place(c.Stamp.rectTransform, new Vector2(1f, 0f), new Vector2(0.5f, 0.5f), new Vector2(-62f, 44f),
                new Vector2(150f, 40f));
            c.Stamp.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 12f);
            c.Stamp.text = "CHOSEN";
            return c;
        }

        static void FitInside(TextMeshProUGUI label, float min, float max)
        {
            label.enableAutoSizing = true;
            label.fontSizeMin = min;
            label.fontSizeMax = max;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        public void SetScheme(string scheme)
        {
            if (_scheme == scheme)
                return;
            _scheme = scheme;
            _hints.text = scheme switch
            {
                "Gamepad" => Hints(("◄ ►", "Pick"), ("A", "Choose"), ("B", "Close")),
                "KeyboardRight" => Hints(("Arrows", "Pick"), ("RCtrl", "Choose"), ("]", "Close")),
                "KeyboardLeft" => Hints(("A D  1-4", "Pick"), ("Space", "Choose"), ("K", "Close")),
                _ => Hints(("Click", "Choose"), ("A D  1-4", "Pick"), ("Space", "Choose"), ("K", "Close")),
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

        public void Tick(PlayerClassMenu menu, Color teamColor, float now)
        {
            PlayerClass heroClass = menu.Class;
            ClassBook book = heroClass != null ? heroClass.Book : null;
            if (book != _bookShown)
            {
                _bookShown = book;
                Fill(book);
            }
            if (menu.Scheme == "KeyboardMouse")
                HandleMouse(menu);

            HeroClassId current = heroClass != null ? heroClass.Class : HeroClassId.Adventurer;
            int count = book != null ? Mathf.Min(book.Classes.Count, MaxCards) : 0;
            for (int i = 0; i < count; i++)
            {
                Card c = _cards[i];
                bool cursor = menu.Model.Cursor == i;
                float pulse = 0.75f + 0.25f * Mathf.Sin(now * 5f);
                HudWidgets.SetColor(c.Glow, cursor ? new Color(teamColor.r, teamColor.g, teamColor.b, pulse) : Clear);
                float size = cursor ? 1.04f : 1f;
                if (!Mathf.Approximately(c.Root.localScale.x, size))
                    c.Root.localScale = new Vector3(size, size, 1f);
                ClassDefinition definition = book.Classes[i];
                HudWidgets.SetActive(c.Stamp, definition != null && definition.Id == current);
            }
        }

        /// <summary>Writes each class's card. Only when the book changes, so building strings here is fine.</summary>
        void Fill(ClassBook book)
        {
            int count = book != null ? book.Classes.Count : 0;
            for (int i = 0; i < MaxCards; i++)
            {
                Card c = _cards[i];
                ClassDefinition definition = i < count ? book.Classes[i] : null;
                HudWidgets.SetActive(c.Root, definition != null);
                if (definition == null)
                    continue;
                c.Band.color = _skin.ClassColor(definition.Id);
                Sprite crest = _skin.CrestFor(definition.Id);
                c.Crest.sprite = crest;
                c.Crest.enabled = crest != null;
                c.Name.text = definition.DisplayName;
                c.Stats.text = $"<color=#A3221B>Health {definition.MaxHealth:0}</color>   " +
                               $"<color=#1F4FB0>Mana {definition.MaxMana:0}</color>";
                c.About.text = definition.Description;
                for (int s = 0; s < SignatureSpells; s++)
                {
                    AbilityDefinition spell = SpellIn(definition, s);
                    c.Icons[s].sprite = spell != null ? _skin.IconFor(spell) : _skin.fallbackIcon;
                    c.Icons[s].color = spell != null ? Color.white : Faded;
                    c.Spells[s].text = spell != null ? spell.DisplayName : "<i>Coming soon</i>";
                }
            }
        }

        static AbilityDefinition SpellIn(ClassDefinition definition, int slot) =>
            definition.Loadout != null ? definition.Loadout.Get(slot) : null;

        /// <summary>The keyboard + mouse player points at a card and clicks to choose it.</summary>
        void HandleMouse(PlayerClassMenu menu)
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
