using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>The shape of a projectile's glowing core.</summary>
    public enum AbilityCoreShape : byte
    {
        /// <summary>A small faceted gem, like the mana crystals on the M.G.R.S.</summary>
        Gem,
        /// <summary>A round orb.</summary>
        Orb,
    }

    /// <summary>
    /// How one ability looks and sounds on every PC (Roadmap 4.2): its colour, its projectile, the glow on the hands while
    /// it's cast, what flies out of its crater and how hard it shakes the view (4.3), and its sounds. Kept apart from the <see cref="AbilityDefinition"/> so the rules the host runs know
    /// nothing about effects, the way <see cref="SoundBook"/> keeps sounds out of blocks. The views
    /// (<see cref="ManaBoltView"/>, <see cref="CastingView"/>, <see cref="PlayerFoley"/>) play it from
    /// <see cref="AbilityCue"/>s. List it in the <see cref="AbilityLookBook"/>; <c>AbilityBuilder</c> writes ours.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Ability Look", fileName = "NewAbilityLook")]
    public sealed class AbilityLook : ScriptableObject
    {
        [Tooltip("The ability this dresses.")]
        [SerializeField] AbilityDefinition ability;

        [Tooltip("Its picture on the action bar (Roadmap 4.4). Empty = the element's glyph.")]
        [SerializeField] Sprite icon;

        [Tooltip("Colour of the projectile, its light, the cast glow, the flashes and the bursts.")]
        [SerializeField] Color glow = new Color(0.35f, 0.85f, 1f);

        [Header("Projectile")]
        [SerializeField] AbilityCoreShape coreShape;

        [Tooltip("How big the glowing core is, in metres.")]
        [SerializeField, Range(0.05f, 1f)] float coreSize = 0.22f;

        [Tooltip("A model to fly instead of the gem or orb, drawn as glowing light (Roadmap 4.6: the Rune Hammer). Empty = " +
                 "the core shape.")]
        [SerializeField] Mesh coreMesh;

        [Tooltip("How fast the core tumbles end over end, in degrees a second. 0 = it doesn't.")]
        [SerializeField, Range(0f, 2000f)] float coreSpin;

        [Header("Zone (4.6)")]
        [Tooltip("A model standing in the middle of a zone (a Shaman's totem), drawn with the props' lit material. Empty = none.")]
        [SerializeField] Mesh zoneModel;

        [Tooltip("How bright the zone's ring on the ground is (0 = no ring, 1 = full).")]
        [SerializeField, Range(0f, 1f)] float zoneRing = 1f;

        [Tooltip("Seconds the trail takes to fade.")]
        [SerializeField, Range(0.05f, 1f)] float trailTime = 0.18f;

        [Tooltip("The light that travels with the projectile.")]
        [SerializeField, Range(0f, 20f)] float lightIntensity = 4f;

        [SerializeField, Range(1f, 20f)] float lightRange = 7f;

        [Tooltip("Sparks out of the muzzle (or the hand) when it leaves.")]
        [SerializeField, Range(0, 60)] int muzzleSparks = 8;

        [Tooltip("Sparks where it hits a block. A fizzle throws a third of these.")]
        [SerializeField, Range(0, 120)] int impactSparks = 28;

        [Tooltip("How fast the impact sparks fly, in metres per second.")]
        [SerializeField, Range(1f, 15f)] float impactSpeed = 5f;

        [Header("Crater (abilities with an AoE radius, Roadmap 4.3)")]
        [Tooltip("Chunks of rock flung out of a crater, for a 3 m crater. Bigger craters throw more.")]
        [SerializeField, Range(0, 200)] int debrisChunks = 40;

        [Tooltip("How fast the chunks fly, in metres per second.")]
        [SerializeField, Range(1f, 20f)] float debrisSpeed = 7f;

        [Tooltip("Mixed into the colour of the block it hit (alpha = how much), e.g. dark for scorched rock.")]
        [SerializeField] Color debrisTint = new Color(0f, 0f, 0f, 0f);

        [Tooltip("How hard it shakes the view of a hero standing right next to it (0-1). Further away shakes less.")]
        [SerializeField, Range(0f, 1f)] float shakeStrength = 0.4f;

        [Tooltip("How bright the flash where it lands is. It lights about three times the crater radius.")]
        [SerializeField, Range(0f, 30f)] float blastLight = 8f;

        [Header("Casting (abilities with a cast time)")]
        [Tooltip("Motes swirling around the hands per second while casting.")]
        [SerializeField, Range(0f, 200f)] float castMotesPerSecond = 50f;

        [Tooltip("Brightest the light around the hands gets at the end of the cast.")]
        [SerializeField, Range(0f, 10f)] float castLight = 2.5f;

        [Header("Sounds")]
        [Tooltip("Loops while casting.")]
        [SerializeField] AudioClip castLoop;

        [SerializeField, Range(0f, 2f)] float castLoopVolume = 0.5f;

        [Tooltip("When it goes off.")]
        [SerializeField] AudioClip release;

        [SerializeField, Range(0f, 2f)] float releaseVolume = 0.8f;

        [Tooltip("Where a projectile hits a block.")]
        [SerializeField] AudioClip impact;

        [SerializeField, Range(0f, 2f)] float impactVolume = 0.9f;

        [Tooltip("Where a projectile fizzles. Empty = the impact sound, quieter and higher.")]
        [SerializeField] AudioClip fizzle;

        [Tooltip("Silent beyond this many metres from every player.")]
        [SerializeField, Min(1f)] float maxDistance = 40f;

        public AbilityDefinition Ability => ability;
        public Sprite Icon => icon;
        public Color Glow => glow;
        public AbilityCoreShape CoreShape => coreShape;
        public float CoreSize => coreSize;
        public Mesh CoreMesh => coreMesh;
        public float CoreSpin => coreSpin;
        public Mesh ZoneModel => zoneModel;
        public float ZoneRing => zoneRing;
        public float TrailTime => trailTime;
        public float LightIntensity => lightIntensity;
        public float LightRange => lightRange;
        public int MuzzleSparks => muzzleSparks;
        public int ImpactSparks => impactSparks;
        public float ImpactSpeed => impactSpeed;
        public int DebrisChunks => debrisChunks;
        public float DebrisSpeed => debrisSpeed;
        public Color DebrisTint => debrisTint;
        public float ShakeStrength => shakeStrength;
        public float BlastLight => blastLight;
        public float CastMotesPerSecond => castMotesPerSecond;
        public float CastLight => castLight;
        public AudioClip CastLoop => castLoop;
        public float CastLoopVolume => castLoopVolume;
        public AudioClip Release => release;
        public float ReleaseVolume => releaseVolume;
        public AudioClip Impact => impact;
        public float ImpactVolume => impactVolume;
        public AudioClip Fizzle => fizzle;
        public float MaxDistance => maxDistance;

        /// <summary>Builds a look in code, for tests and tools.</summary>
        internal static AbilityLook Create(AbilityDefinition ability, Color glow)
        {
            var look = CreateInstance<AbilityLook>();
            look.name = ability != null ? $"{ability.name} Look" : "Look";
            look.ability = ability;
            look.glow = glow;
            return look;
        }
    }
}
