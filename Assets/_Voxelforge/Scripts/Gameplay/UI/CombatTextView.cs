using System;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Draws one player's floating combat text (Roadmap 4.4) from their <see cref="CombatTextFeed"/>: comic lettering
    /// with an ink outline that pops in where it happened (projected through <b>that player's</b> camera), floats up and
    /// fades. Craters get a starburst behind them. The labels are pooled, so nothing is made while playing.
    /// </summary>
    sealed class CombatTextView
    {
        const float Rise = 80f;
        const float PopSeconds = 0.16f;

        sealed class Label
        {
            public RectTransform Root;
            public TextMeshProUGUI Text;
            public Image Burst;
            public int Version = -1;
            public bool InUse;
            public float Jitter;
        }

        readonly HudSkin _skin;
        readonly Label[] _labels;
        readonly Func<ushort, string> _itemName;

        public CombatTextView(RectTransform parent, HudSkin skin, int capacity, Func<ushort, string> itemName)
        {
            _skin = skin;
            _itemName = itemName;
            _labels = new Label[capacity];
            for (int i = 0; i < capacity; i++)
            {
                var label = new Label { Jitter = ((i * 37) % 41 - 20) * 1.5f };
                label.Root = HudWidgets.Place(HudWidgets.Rect($"Text {i}", parent), Vector2.zero, new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(400f, 120f));
                label.Burst = HudWidgets.Image("Burst", label.Root, skin.starburst, Color.white);
                HudWidgets.Place(label.Burst.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(220f, 160f));
                label.Text = HudWidgets.Label("Words", label.Root, skin.comicFont, skin.combatTextSize, Color.white,
                    TextAlignmentOptions.Center);
                HudWidgets.Stretch(label.Text.rectTransform);
                label.Root.gameObject.SetActive(false);
                _labels[i] = label;
            }
        }

        /// <param name="area">The rect this view's parent covers, in GUI pixels.</param>
        /// <param name="scale">GUI pixels per 1080p pixel.</param>
        public void Tick(CombatTextFeed feed, PlayerView view, Rect area, float scale, float now)
        {
            foreach (Label label in _labels)
                label.InUse = false;

            foreach (CombatTextFeed.Entry entry in feed.Entries)
            {
                Label label = _labels[entry.Label];
                if (!PlayerScreenArea.TryProject(view, entry.Event.Position, out Vector2 gui))
                    continue;
                label.InUse = true;
                if (label.Version != entry.Version)
                {
                    label.Version = entry.Version;
                    Dress(label, entry.Event);
                }

                float age = now - entry.Born;
                float life = math.saturate(age / CombatTextFeed.Lifetime(entry.Event.Kind));
                // Pop in past full size and settle, then drift up, easing out.
                float pop = age < PopSeconds ? math.lerp(0.3f, 1.35f, age / PopSeconds) : math.lerp(1.35f, 1f, math.saturate((age - PopSeconds) / PopSeconds));
                float rise = Rise * (1f - (1f - life) * (1f - life));
                label.Root.anchoredPosition = new Vector2((gui.x - area.x) / scale + label.Jitter, (area.yMax - gui.y) / scale + rise);
                label.Root.localScale = new Vector3(pop, pop, 1f);
                float alpha = life < 0.65f ? 1f : 1f - (life - 0.65f) / 0.35f;
                label.Text.alpha = alpha;
                if (label.Burst.enabled)
                    label.Burst.color = new Color(label.Burst.color.r, label.Burst.color.g, label.Burst.color.b, alpha * 0.95f);
            }

            foreach (Label label in _labels)
                HudWidgets.SetActive(label.Root, label.InUse);
        }

        /// <summary>The words, colour and size for one line. Only when it changes, so building strings here is fine.</summary>
        void Dress(Label label, in CombatTextEvent e)
        {
            float size = _skin.combatTextSize;
            Color colour = Color.white;
            bool burst = false;
            string words;
            switch (e.Kind)
            {
                case CombatTextKind.SpellHit:
                    words = e.Amount.ToString();
                    colour = _skin.ElementColor(e.Element);
                    break;
                case CombatTextKind.CraterHit:
                    words = $"{Exclaim(e.Element)}\n<size=70%>×{e.Amount} blocks</size>";
                    colour = _skin.ElementColor(e.Element);
                    size *= 1.25f;
                    burst = true;
                    break;
                case CombatTextKind.ManaSpent:
                    words = $"-{e.Amount} mana";
                    colour = _skin.mana;
                    size *= 0.7f;
                    break;
                case CombatTextKind.Refused:
                    words = Explain(e.Reason);
                    colour = _skin.refusal;
                    size *= 0.85f;
                    break;
                case CombatTextKind.BagRefused:
                    words = e.BagReason switch
                    {
                        BagRefusal.BagFull => "Bag full!",
                        BagRefusal.NoPartner => "Nobody to send to!",
                        _ => "No room to take that bag off!",
                    };
                    colour = _skin.refusal;
                    size *= 0.85f;
                    break;
                case CombatTextKind.Sent:
                    words = $"Sent {e.Amount} {_itemName?.Invoke(e.ItemId) ?? "?"}!";
                    colour = _skin.pickup;
                    size *= 0.7f;
                    break;
                case CombatTextKind.Pickup:
                    words = $"+{e.Amount} {_itemName?.Invoke(e.ItemId) ?? "?"}";
                    colour = _skin.pickup;
                    size *= 0.7f;
                    break;
                default:
                    words = string.Empty;
                    break;
            }
            label.Text.text = words;
            label.Text.fontSize = size;
            label.Text.color = colour;
            label.Burst.enabled = burst && _skin.starburst != null;
            // A pale balloon, just a hint of the element, so the inked words in its colour stand out on it.
            if (burst)
                label.Burst.color = Color.Lerp(new Color(1f, 0.96f, 0.82f), colour, 0.18f);
        }

        /// <summary>A comic sound effect for a crater, by element.</summary>
        static string Exclaim(AbilityElement element)
        {
            switch (element)
            {
                case AbilityElement.Fire:
                    return "KA-BOOM!";
                case AbilityElement.Earth:
                    return "KRAKA-THOOM!";
                case AbilityElement.Arcane:
                    return "ZA-POW!";
                case AbilityElement.Frost:
                    return "KRSSH!";
                case AbilityElement.Lightning:
                    return "ZZRAKK!";
                default:
                    return "WHAM!";
            }
        }

        static string Explain(CastCheck reason)
        {
            switch (reason)
            {
                case CastCheck.NotEnoughMana:
                    return "NO MANA!";
                case CastCheck.OnCooldown:
                    return "NOT READY!";
                case CastCheck.Busy:
                    return "ALREADY CASTING!";
                case CastCheck.Interrupted:
                    return "INTERRUPTED!";
                case CastCheck.NoTarget:
                    return "NOTHING IN RANGE!";
                default:
                    return "CAN'T CAST THAT!";
            }
        }
    }
}
