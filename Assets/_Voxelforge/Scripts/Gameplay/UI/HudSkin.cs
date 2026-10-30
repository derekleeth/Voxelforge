using TMPro;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// How the 4.4 HUD looks: comic-book fantasy frames, fonts and colours. Looks only, never rules. Sizes are in
    /// pixels at 1080p; <see cref="CombatHud"/> scales them to the screen. <c>HudArtBuilder</c> paints the sprites and
    /// writes <c>Data/UI/HudSkin.asset</c> (Voxelforge &gt; Build HUD Art).
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/HUD Skin", fileName = "HudSkin")]
    public sealed class HudSkin : ScriptableObject
    {
        [Header("Fonts")]
        [Tooltip("Punchy comic lettering: numbers, hotkeys, pop-ups (Bangers).")]
        public TMP_FontAsset comicFont;

        [Tooltip("For small comic lettering (mana, hotkeys, timers): a thin ink outline and no drop shadow, which smear " +
                 "small text. A material preset of the comic font.")]
        public Material comicSmall;

        [Tooltip("Spell names on the cast bar (MedievalSharp).")]
        public TMP_FontAsset titleFont;

        [Header("Sprites")]
        [Tooltip("Parchment panel with an ink border, 9-sliced.")]
        public Sprite panel;

        [Tooltip("Wooden, ink-outlined frame drawn over a slot's icon.")]
        public Sprite slotFrame;

        [Tooltip("Dark halftone behind a slot's icon.")]
        public Sprite slotBack;

        [Tooltip("White glow that flashes over a slot when its spell goes off.")]
        public Sprite slotFlash;

        [Tooltip("Plain white square: cooldown sweeps and tinted fills.")]
        public Sprite white;

        [Tooltip("Wood and ink frame around the mana and cast bars, 9-sliced.")]
        public Sprite barFrame;

        [Tooltip("A bar's fill (white, tinted), with halftone shading.")]
        public Sprite barFill;

        [Tooltip("Dark halftone inside an empty bar.")]
        public Sprite barBack;

        [Tooltip("Comic explosion behind craters and refusals.")]
        public Sprite starburst;

        [Tooltip("For a spell whose look has no icon.")]
        public Sprite fallbackIcon;

        [Header("Looks")]
        [Tooltip("Data/Abilities/Looks/AbilityLookBook.asset: spell icons.")]
        public AbilityLookBook looks;

        [Header("Colours")]
        public Color ink = new Color(0.08f, 0.05f, 0.05f);
        public Color mana = new Color(0.22f, 0.5f, 1f);
        public Color cast = new Color(1f, 0.78f, 0.22f);
        public Color refusal = new Color(1f, 0.32f, 0.22f);
        public Color pickup = new Color(1f, 0.92f, 0.55f);
        [Tooltip("The health bar and damage a hero takes (Roadmap 4.6).")]
        public Color health = new Color(0.86f, 0.16f, 0.14f);
        [Tooltip("Healing numbers (Roadmap 4.6).")]
        public Color heal = new Color(0.38f, 1f, 0.42f);
        [Tooltip("The shield over the health bar and shield numbers (Roadmap 4.6).")]
        public Color shield = new Color(1f, 0.86f, 0.38f);

        [Tooltip("Spell hit numbers by element, in AbilityElement order: Physical, Arcane, Fire, Frost, Earth, Nature, " +
                 "Lightning, Holy.")]
        public Color[] elements =
        {
            new Color(1f, 1f, 1f), new Color(0.85f, 0.5f, 1f), new Color(1f, 0.55f, 0.15f), new Color(0.55f, 0.9f, 1f),
            new Color(0.85f, 0.65f, 0.35f), new Color(0.45f, 1f, 0.4f), new Color(0.6f, 0.75f, 1f), new Color(1f, 0.95f, 0.55f),
        };

        [Tooltip("Each class's colour in the class window (Roadmap 4.6), in HeroClassId order: Adventurer, Runeforged " +
                 "Paladin, Geomancer Mage, Shaman.")]
        public Color[] classes =
        {
            new Color(0.62f, 0.5f, 0.36f), new Color(1f, 0.78f, 0.25f), new Color(0.88f, 0.48f, 0.2f), new Color(0.2f, 0.7f, 0.85f),
        };

        [Header("Sizes (pixels at 1080p)")]
        [Range(32f, 120f)] public float slotSize = 66f;
        [Range(0f, 20f)] public float slotGap = 6f;
        [Range(4f, 40f)] public float manaBarHeight = 22f;
        [Range(160f, 600f)] public float castBarWidth = 340f;
        [Range(10f, 60f)] public float castBarHeight = 26f;
        [Range(10f, 120f)] public float combatTextSize = 44f;

        public Color ElementColor(AbilityElement element)
        {
            int i = (int)element;
            return elements != null && i < elements.Length ? elements[i] : Color.white;
        }

        public Color ClassColor(HeroClassId id)
        {
            int i = (int)id;
            return classes != null && i < classes.Length ? classes[i] : Color.white;
        }

        public Sprite IconFor(AbilityDefinition ability)
        {
            // Not the look book's default look: that's another spell's, and its picture would lie.
            if (looks != null && ability != null && looks.TryGet(ability.Id, out AbilityLook look) && look.Icon != null)
                return look.Icon;
            return fallbackIcon;
        }
    }
}
