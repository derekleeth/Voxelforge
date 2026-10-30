using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// One player's action bar (Roadmap 4.4): nine slots on a parchment strip with the mana bar above it, and the health
    /// bar above that with the shield in gold behind it (4.6). Each slot shows
    /// the spell's icon, the key that casts it for this player's controls, a cooldown sweep with the seconds left, a cold
    /// grey when there isn't enough mana, a squash when pressed and a flash when the spell goes off. It only reads what
    /// the host shared (<see cref="PlayerActionBar"/>, <see cref="PlayerCaster"/>).
    /// </summary>
    sealed class ActionBarView
    {
        const float Padding = 10f;
        const float BounceSeconds = 0.18f;
        const float FlashSeconds = 0.35f;
        static readonly Color NoMana = new Color(0.42f, 0.48f, 0.78f);
        static readonly Color Empty = new Color(1f, 1f, 1f, 0f);

        sealed class Slot
        {
            public RectTransform Root;
            public Image Icon;
            public Image Cooldown;
            public Image Flash;
            public TextMeshProUGUI Key;
            public TextMeshProUGUI Timer;
            public AbilityDefinition Ability;
            public int TimerShown = -1;
            public float BounceUntil;
            public float FlashUntil;
        }

        readonly HudSkin _skin;
        readonly Slot[] _slots = new Slot[PlayerActionBar.SlotCount];
        readonly Image _manaFill;
        readonly TextMeshProUGUI _manaText;
        readonly Image _healthFill;
        readonly Image _shieldFill;
        readonly TextMeshProUGUI _healthText;
        int _healthShown = -1;
        int _maxHealthShown = -1;
        int _shieldShown = -1;
        PlayerButtons _previousButtons;
        int _manaShown = -1;
        int _maxManaShown = -1;
        string _scheme;

        public ActionBarView(RectTransform parent, HudSkin skin)
        {
            _skin = skin;
            float slot = skin.slotSize;
            float width = Width;
            Root = HudWidgets.Place(HudWidgets.Rect("Action Bar", parent), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(width, slot + Padding * 2f));
            HudWidgets.Stretch(HudWidgets.Image("Strip", Root, skin.panel, Color.white, Image.Type.Sliced).rectTransform);

            for (int i = 0; i < _slots.Length; i++)
            {
                var s = new Slot();
                s.Root = HudWidgets.Place(HudWidgets.Rect($"Slot {i + 1}", Root), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(Padding + slot * 0.5f + i * (slot + skin.slotGap), 0f), new Vector2(slot, slot));
                HudWidgets.Stretch(HudWidgets.Image("Back", s.Root, skin.slotBack, Color.white).rectTransform, 4f);
                s.Icon = HudWidgets.Image("Icon", s.Root, null, Empty);
                HudWidgets.Stretch(s.Icon.rectTransform, 6f);
                s.Cooldown = HudWidgets.Image("Cooldown", s.Root, skin.white, new Color(0.05f, 0.03f, 0.08f, 0.68f));
                s.Cooldown.type = Image.Type.Filled;
                s.Cooldown.fillMethod = Image.FillMethod.Radial360;
                s.Cooldown.fillOrigin = (int)Image.Origin360.Top;
                s.Cooldown.fillClockwise = false;
                s.Cooldown.fillAmount = 0f;
                HudWidgets.Stretch(s.Cooldown.rectTransform, 6f);
                HudWidgets.Stretch(HudWidgets.Image("Frame", s.Root, skin.slotFrame, Color.white).rectTransform);
                s.Flash = HudWidgets.Image("Flash", s.Root, skin.slotFlash, Empty);
                HudWidgets.Stretch(s.Flash.rectTransform, -slot * 0.25f);
                s.Timer = HudWidgets.Label("Timer", s.Root, skin.comicFont, slot * 0.45f, Color.white, TextAlignmentOptions.Center);
                HudWidgets.Stretch(s.Timer.rectTransform);
                s.Key = HudWidgets.SmallLabel("Key", s.Root, skin, slot * 0.3f, new Color(1f, 0.95f, 0.8f),
                    TextAlignmentOptions.TopLeft);
                HudWidgets.Stretch(s.Key.rectTransform, 5f);
                _slots[i] = s;
            }

            // Mana sits on top of the strip, as wide as it.
            RectTransform mana = HudWidgets.Place(HudWidgets.Rect("Mana", Root), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f),
                new Vector2(0f, 4f), new Vector2(width - Padding * 2f, skin.manaBarHeight));
            HudWidgets.Stretch(HudWidgets.Image("Back", mana, skin.barBack, Color.white).rectTransform, 2f);
            _manaFill = HudWidgets.Image("Fill", mana, skin.barFill, skin.mana);
            _manaFill.type = Image.Type.Filled;
            _manaFill.fillMethod = Image.FillMethod.Horizontal;
            HudWidgets.Stretch(_manaFill.rectTransform, 2f);
            HudWidgets.Stretch(HudWidgets.Image("Frame", mana, skin.barFrame, Color.white, Image.Type.Sliced).rectTransform, -3f);
            _manaText = HudWidgets.SmallLabel("Amount", mana, skin, skin.manaBarHeight * 1.05f, Color.white,
                TextAlignmentOptions.Center);
            HudWidgets.Stretch(_manaText.rectTransform);

            // Health sits on top of the mana (Roadmap 4.6). The shield is a gold strip behind the red, reaching past it.
            RectTransform health = HudWidgets.Place(HudWidgets.Rect("Health", Root), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f),
                new Vector2(0f, 8f + skin.manaBarHeight), new Vector2(width - Padding * 2f, skin.manaBarHeight));
            HudWidgets.Stretch(HudWidgets.Image("Back", health, skin.barBack, Color.white).rectTransform, 2f);
            _shieldFill = Fill("Shield", health, skin.shield);
            _healthFill = Fill("Fill", health, skin.health);
            HudWidgets.Stretch(HudWidgets.Image("Frame", health, skin.barFrame, Color.white, Image.Type.Sliced).rectTransform, -3f);
            _healthText = HudWidgets.SmallLabel("Amount", health, skin, skin.manaBarHeight * 1.05f, Color.white,
                TextAlignmentOptions.Center);
            HudWidgets.Stretch(_healthText.rectTransform);
        }

        Image Fill(string name, RectTransform bar, Color colour)
        {
            Image fill = HudWidgets.Image(name, bar, _skin.barFill, colour);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0f;
            HudWidgets.Stretch(fill.rectTransform, 2f);
            return fill;
        }

        public RectTransform Root { get; }

        /// <summary>Width of the strip in 1080p pixels.</summary>
        public float Width => Padding * 2f + _skin.slotSize * PlayerActionBar.SlotCount + _skin.slotGap * (PlayerActionBar.SlotCount - 1);

        /// <summary>Height of the strip and the mana and health bars on it.</summary>
        public float Height => _skin.slotSize + Padding * 2f + 8f + _skin.manaBarHeight * 2f;

        /// <summary>The key that casts each slot, for this player's controls (Roadmap 4.4).</summary>
        public static string HotkeyLabel(string scheme, int slot)
        {
            switch (scheme)
            {
                case "Gamepad":
                    if (slot == 8)
                        return "B";
                    string arrow = "▲►▼◄".Substring(slot % 4, 1); // Up, right, down, left.
                    return slot >= 4 ? "B+" + arrow : arrow;
                case "KeyboardRight":
                    return "N" + (slot + 1);
                default:
                    return (slot + 1).ToString();
            }
        }

        public void SetScheme(string scheme)
        {
            if (_scheme == scheme)
                return;
            _scheme = scheme;
            for (int i = 0; i < _slots.Length; i++)
                _slots[i].Key.text = HotkeyLabel(scheme, i);
        }

        /// <summary>A spell went off: flash its slot.</summary>
        public void Flash(AbilityDefinition ability, float now)
        {
            foreach (Slot slot in _slots)
            {
                if (slot.Ability == ability)
                    slot.FlashUntil = now + FlashSeconds;
            }
        }

        public void Tick(PlayerActionBar bar, PlayerCaster caster, PlayerHealth health, PlayerButtons buttons, float now)
        {
            PlayerButtons pressed = buttons & ~_previousButtons;
            _previousButtons = buttons;
            float mana = caster != null ? caster.Mana : 0f;

            for (int i = 0; i < _slots.Length; i++)
            {
                Slot s = _slots[i];
                AbilityDefinition ability = null;
                bar?.TryGet(i, out ability);
                if (s.Ability != ability)
                {
                    s.Ability = ability;
                    s.Icon.sprite = ability != null ? _skin.IconFor(ability) : null;
                }
                if ((pressed & ActionSlotButtons.For(i)) != 0)
                    s.BounceUntil = now + BounceSeconds;

                // Squash when pressed, springing back.
                float bounce = Mathf.Clamp01((s.BounceUntil - now) / BounceSeconds);
                float size = 1f - 0.14f * Mathf.Sin(bounce * Mathf.PI);
                if (!Mathf.Approximately(s.Root.localScale.x, size))
                    s.Root.localScale = new Vector3(size, size, 1f);

                if (ability == null)
                {
                    HudWidgets.SetColor(s.Icon, Empty);
                    SetCooldown(s, 1f, 0f);
                    HudWidgets.SetColor(s.Flash, Empty);
                    continue;
                }
                HudWidgets.SetColor(s.Icon, mana + 0.01f < ability.ManaCost ? NoMana : Color.white);
                float ready = caster != null ? caster.CooldownReady(ability) : 1f;
                SetCooldown(s, ready, (1f - ready) * ability.Cooldown);
                float flash = Mathf.Clamp01((s.FlashUntil - now) / FlashSeconds);
                HudWidgets.SetColor(s.Flash, new Color(1f, 1f, 1f, flash * flash));
            }

            float maxMana = caster != null ? caster.MaxMana : 0f;
            float fill = maxMana > 0f ? Mathf.Clamp01(mana / maxMana) : 0f;
            if (!Mathf.Approximately(_manaFill.fillAmount, fill))
                _manaFill.fillAmount = fill;
            int shown = Mathf.FloorToInt(mana);
            int maxShown = Mathf.RoundToInt(maxMana);
            if (shown != _manaShown || maxShown != _maxManaShown)
            {
                _manaShown = shown;
                _maxManaShown = maxShown;
                _manaText.SetText("{0} / {1}", (float)shown, (float)maxShown);
            }
            TickHealth(health);
        }

        void TickHealth(PlayerHealth health)
        {
            float current = health != null ? health.Health : 0f;
            float max = health != null ? health.MaxHealth : 0f;
            float shield = health != null ? health.Shield : 0f;
            float fill = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            float shieldFill = max > 0f && shield > 0f ? Mathf.Clamp01((current + shield) / max) : 0f;
            if (!Mathf.Approximately(_healthFill.fillAmount, fill))
                _healthFill.fillAmount = fill;
            if (!Mathf.Approximately(_shieldFill.fillAmount, shieldFill))
                _shieldFill.fillAmount = shieldFill;

            int shown = Mathf.CeilToInt(current);
            int maxShown = Mathf.RoundToInt(max);
            int shieldShown = Mathf.CeilToInt(shield);
            if (shown == _healthShown && maxShown == _maxHealthShown && shieldShown == _shieldShown)
                return;
            _healthShown = shown;
            _maxHealthShown = maxShown;
            _shieldShown = shieldShown;
            if (shieldShown > 0)
                _healthText.SetText("{0} / {1}  +{2}", (float)shown, (float)maxShown, (float)shieldShown);
            else
                _healthText.SetText("{0} / {1}", (float)shown, (float)maxShown);
        }

        static void SetCooldown(Slot s, float ready, float secondsLeft)
        {
            float fill = ready >= 1f ? 0f : 1f - ready;
            if (!Mathf.Approximately(s.Cooldown.fillAmount, fill))
                s.Cooldown.fillAmount = fill;
            int timer = ready >= 1f ? -1 : Mathf.CeilToInt(secondsLeft);
            if (timer == s.TimerShown)
                return;
            s.TimerShown = timer;
            if (timer > 0)
                s.Timer.SetText("{0}", (float)timer);
            else
                s.Timer.SetText(string.Empty);
        }
    }
}
