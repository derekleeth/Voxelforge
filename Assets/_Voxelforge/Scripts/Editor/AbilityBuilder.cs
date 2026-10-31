using UnityEditor;
using UnityEngine;
using Voxelforge.Gameplay;

namespace Voxelforge.Editor
{
    /// <summary>
    /// <i>Voxelforge → Build Abilities</i> (Roadmap 4.1): writes the ability templates in <c>Data/Abilities/</c> and the
    /// <see cref="AbilityBook"/> that lists them. Re-running it updates the same assets in place and overwrites Inspector
    /// tweaks, so keep the numbers here.
    /// <list type="bullet">
    /// <item><b>Mana Bolt</b>: what Kian's M.G.R.S. shoots (D-019's numbers: 45 damage, 0.5 s, 40 m/s, 24 m), now
    /// costing 8 mana.</item>
    /// <item><b>Arcane Orb</b>: a slow, heavy test spell with a 1.5 s cast and a small crater, in action bar slot 1.</item>
    /// <item><b>Pyroblast</b> (Roadmap 4.3): a fire orb with a 2.5 s cast that blasts a deep, round crater, in slot 2.</item>
    /// <item><b>EarthShatter</b> (Roadmap 4.3): an instant slam on the ground you aim at that blasts a wide, shallow
    /// crater, in slot 3.</item>
    /// </list>
    /// The slots are the default <see cref="ActionBarLoadout"/> (Roadmap 4.4). Each look picks up its action bar icon from
    /// <c>Art/Textures/UI/Icons/&lt;Ability&gt;.png</c> when there is one.
    /// It also writes how each looks and sounds (<see cref="AbilityLook"/>, Roadmap 4.2) in <c>Looks/</c>, using the
    /// sounds from <i>Voxelforge → Build Sound Effects</i>, so build those first.
    /// <para>The Runeforged Paladin's spells (Roadmap 4.6): <b>Rune Hammer</b> (a glowing hammer thrown end over end),
    /// <b>Bulwark Rune</b> (a shield on the partner you aim at, or you) and <b>Hallowed Forge</b> (a golden circle at your
    /// feet that heals heroes in it).</para>
    /// <para>The Geomancer Mage's (Roadmap 4.6): <b>Stone Spear</b> (a heavy lance of rock with a short cast) and
    /// <b>Rising Pillar</b> (Earthen Rock rises under you, five blocks, and crumbles after 30 s), with EarthShatter.</para>
    /// <para>The Shaman's (Roadmap 4.6): <b>Earthen Wall</b> (a 5 x 3 wall of Earthen Rock where you aim, crumbling after
    /// 30 s), <b>Wellspring Totem</b> (heals heroes within 8 m and quickens their mana) and <b>Thunderhead Totem</b>
    /// (lightning on the nearest foe within 12 m every 2 s). Totems last 20 s, one of each per Shaman.</para>
    /// <para>And the classes (Roadmap 4.6) in <c>Data/Classes/</c>: each <see cref="ClassDefinition"/>, its action bar
    /// loadout and the <see cref="ClassBook"/>, in the order the class window shows them.</para>
    /// <para>The races (Roadmap 4.7) in <c>Data/Races/</c>, with their racials: <b>Hoofquake</b> (the Thunderhoof's stomp,
    /// a 6 m stun) and <b>Second Wind</b> (the Wayfarer's quick heal of a quarter of their health).</para>
    /// </summary>
    public static class AbilityBuilder
    {
        const string AbilityDir = "Assets/_Voxelforge/Data/Abilities";
        public const string ManaBoltPath = AbilityDir + "/ManaBolt.asset";
        public const string ArcaneOrbPath = AbilityDir + "/ArcaneOrb.asset";
        public const string PyroblastPath = AbilityDir + "/Pyroblast.asset";
        public const string EarthShatterPath = AbilityDir + "/EarthShatter.asset";
        public const string RuneHammerPath = AbilityDir + "/RuneHammer.asset";
        public const string BulwarkRunePath = AbilityDir + "/BulwarkRune.asset";
        public const string HallowedForgePath = AbilityDir + "/HallowedForge.asset";
        public const string StoneSpearPath = AbilityDir + "/StoneSpear.asset";
        public const string RisingPillarPath = AbilityDir + "/RisingPillar.asset";
        const string SpearModelPath = "Assets/_Voxelforge/Art/Models/Props/StoneSpear.fbx";
        public const string EarthenWallPath = AbilityDir + "/EarthenWall.asset";
        public const string WellspringTotemPath = AbilityDir + "/WellspringTotem.asset";
        public const string ThunderheadTotemPath = AbilityDir + "/ThunderheadTotem.asset";
        const string ModelDir = "Assets/_Voxelforge/Art/Models/Props";
        const string HammerModelPath = "Assets/_Voxelforge/Art/Models/Props/RuneHammer.fbx";
        public const string BookPath = AbilityDir + "/AbilityBook.asset";
        public const string LoadoutPath = AbilityDir + "/DefaultLoadout.asset";
        const string IconDir = "Assets/_Voxelforge/Art/Textures/UI/Icons";
        const string LookDir = AbilityDir + "/Looks";
        public const string LookBookPath = LookDir + "/AbilityLookBook.asset";
        const string SfxDir = "Assets/_Voxelforge/Art/Audio/Sfx";
        const string ClassDir = "Assets/_Voxelforge/Data/Classes";
        public const string ClassBookPath = ClassDir + "/ClassBook.asset";
        const string HudSkinPath = "Assets/_Voxelforge/Data/UI/HudSkin.asset";
        public const string HoofquakePath = AbilityDir + "/Hoofquake.asset";
        public const string SecondWindPath = AbilityDir + "/SecondWind.asset";
        const string RaceDir = "Assets/_Voxelforge/Data/Races";
        public const string RaceBookPath = RaceDir + "/RaceBook.asset";

        [MenuItem("Voxelforge/Build Abilities")]
        public static void BuildMenu()
        {
            Build();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Voxelforge] Abilities built in {AbilityDir}.");
        }

        /// <summary>Writes every ability and the book, and returns the Mana Bolt (for the M.G.R.S. item).</summary>
        public static AbilityDefinition Build()
        {
            AbilityDefinition manaBolt = MakeAbility(ManaBoltPath, AbilityIds.ManaBolt, "Mana Bolt",
                "A quick bolt of raw mana from the M.G.R.S. Breaks blocks from afar.",
                AbilityElement.Arcane, manaCost: 8f, cooldown: 0.5f, castTime: 0f, range: 24f, aoeRadius: 0f,
                speed: 40f, blockDamage: 45f, creatureDamage: 20f);
            AbilityDefinition arcaneOrb = MakeAbility(ArcaneOrbPath, AbilityIds.ArcaneOrb, "Arcane Orb",
                "Gather your mana into a slow, heavy orb. Stand still while you cast it.",
                AbilityElement.Arcane, manaCost: 30f, cooldown: 6f, castTime: 1.5f, range: 30f, aoeRadius: 2f,
                speed: 18f, blockDamage: 100f, creatureDamage: 60f, craterFalloff: 0.15f, craterFlatten: 0.8f);
            // Craters (Roadmap 4.3): blockDamage is at the middle, falling to craterFalloff of it at the edge. Made smaller
            // after the 4.6 play-test (Orb 3 -> 2 m, Pyroblast 4 -> 3 m, EarthShatter 4.5 -> 3.5 m): with these numbers
            // Pyroblast clears dirt across the whole crater and stone out to about 2.3 m.
            AbilityDefinition pyroblast = MakeAbility(PyroblastPath, AbilityIds.Pyroblast, "Pyroblast",
                "Hurl a huge ball of fire that blasts a crater out of the ground. Stand still while you cast it.",
                AbilityElement.Fire, manaCost: 45f, cooldown: 10f, castTime: 2.5f, range: 40f, aoeRadius: 3f,
                speed: 22f, blockDamage: 200f, creatureDamage: 120f, craterFalloff: 0.1f, craterFlatten: 1f);
            AbilityDefinition earthShatter = MakeAbility(EarthShatterPath, AbilityIds.EarthShatter, "EarthShatter",
                "Slam the ground where you look and shatter it into a wide, shallow bowl.",
                AbilityElement.Earth, manaCost: 35f, cooldown: 8f, castTime: 0f, range: 20f, aoeRadius: 3.5f,
                speed: 0f, blockDamage: 120f, creatureDamage: 80f, craterFalloff: 0.2f, craterFlatten: 0.45f,
                delivery: AbilityDelivery.GroundSlam);

            // ---- Runeforged Paladin (Roadmap 4.6) ----
            AbilityDefinition runeHammer = MakeAbility(RuneHammerPath, AbilityIds.RuneHammer, "Rune Hammer",
                "Hurl a hammer of rune-light end over end. It hits hard, and its runes burst where it lands.",
                AbilityElement.Holy, manaCost: 15f, cooldown: 4f, castTime: 0f, range: 30f, aoeRadius: 0f,
                speed: 30f, blockDamage: 60f, creatureDamage: 45f);
            AbilityDefinition bulwarkRune = MakeAbility(BulwarkRunePath, AbilityIds.BulwarkRune, "Bulwark Rune",
                "Carve a rune of warding round the partner you aim at, or round yourself. It soaks up the next 40 damage.",
                AbilityElement.Holy, manaCost: 20f, cooldown: 12f, castTime: 0f, range: 20f, aoeRadius: 0f,
                speed: 0f, blockDamage: 0f, creatureDamage: 0f, delivery: AbilityDelivery.Shield,
                shieldAmount: 40f, duration: 15f, aimCone: 12f);
            AbilityDefinition hallowedForge = MakeAbility(HallowedForgePath, AbilityIds.HallowedForge, "Hallowed Forge",
                "Bless the ground at your feet with a ring of forge-fire. Heroes standing in it heal for 6 seconds.",
                AbilityElement.Holy, manaCost: 35f, cooldown: 20f, castTime: 1.5f, range: 0f, aoeRadius: 6f,
                speed: 0f, blockDamage: 0f, creatureDamage: 0f, delivery: AbilityDelivery.Zone,
                healAmount: 5f, duration: 6f, pulseInterval: 1f);

            // ---- Geomancer Mage (Roadmap 4.6) ----
            AbilityDefinition stoneSpear = MakeAbility(StoneSpearPath, AbilityIds.StoneSpear, "Stone Spear",
                "Wrench a lance of rock from the earth and hurl it. Slow to call up, but it hits harder than anything quick.",
                AbilityElement.Earth, manaCost: 20f, cooldown: 3f, castTime: 1f, range: 40f, aoeRadius: 0f,
                speed: 45f, blockDamage: 80f, creatureDamage: 60f);
            AbilityDefinition risingPillar = MakeAbility(RisingPillarPath, AbilityIds.RisingPillar, "Rising Pillar",
                "A column of rock bursts up under your feet and lifts you five blocks high. It crumbles away after 30 seconds.",
                AbilityElement.Earth, manaCost: 25f, cooldown: 12f, castTime: 0f, range: 0f, aoeRadius: 0f,
                speed: 0f, blockDamage: 0f, creatureDamage: 0f, delivery: AbilityDelivery.Pillar,
                duration: 30f, earthHeight: 5, earthStep: 0.08f);

            // ---- Shaman (Roadmap 4.6) ----
            AbilityDefinition earthenWall = MakeAbility(EarthenWallPath, AbilityIds.EarthenWall, "Earthen Wall",
                "Call up a wall of living earth where you look, five blocks wide and three high. It crumbles after 30 seconds.",
                AbilityElement.Earth, manaCost: 30f, cooldown: 15f, castTime: 0f, range: 20f, aoeRadius: 0f,
                speed: 0f, blockDamage: 0f, creatureDamage: 0f, delivery: AbilityDelivery.Wall,
                duration: 30f, earthHeight: 3, earthWidth: 5, earthStep: 0.1f);
            AbilityDefinition wellspring = MakeAbility(WellspringTotemPath, AbilityIds.WellspringTotem, "Wellspring Totem",
                "Plant a totem of healing water. For 20 seconds, heroes near it heal and their mana flows half as fast again.",
                AbilityElement.Nature, manaCost: 30f, cooldown: 25f, castTime: 0f, range: 20f, aoeRadius: 8f,
                speed: 0f, blockDamage: 0f, creatureDamage: 0f, delivery: AbilityDelivery.Totem,
                healAmount: 4f, duration: 20f, pulseInterval: 1f, manaRegenBonus: 0.5f);
            AbilityDefinition thunderhead = MakeAbility(ThunderheadTotemPath, AbilityIds.ThunderheadTotem, "Thunderhead Totem",
                "Plant a storm totem. For 20 seconds it calls lightning down on the nearest foe within 12 metres.",
                AbilityElement.Lightning, manaCost: 25f, cooldown: 20f, castTime: 0f, range: 20f, aoeRadius: 12f,
                speed: 0f, blockDamage: 0f, creatureDamage: 25f, delivery: AbilityDelivery.Totem,
                duration: 20f, pulseInterval: 2f);

            // ---- Racials (Roadmap 4.7): free to cast, slot 9, on long cooldowns ----
            AbilityDefinition hoofquake = MakeAbility(HoofquakePath, AbilityIds.Hoofquake, "Hoofquake",
                "Rear up and stamp the ground. Foes within 6 metres are knocked about and stunned for 2 seconds.",
                AbilityElement.Earth, manaCost: 0f, cooldown: 45f, castTime: 0f, range: 0f, aoeRadius: 6f,
                speed: 0f, blockDamage: 0f, creatureDamage: 20f, craterFalloff: 0.5f, delivery: AbilityDelivery.Quake,
                duration: 2f);
            AbilityDefinition secondWind = MakeAbility(SecondWindPath, AbilityIds.SecondWind, "Second Wind",
                "Catch your breath like an old road-hand and heal a quarter of your health at once.",
                AbilityElement.Nature, manaCost: 0f, cooldown: 120f, castTime: 0f, range: 0f, aoeRadius: 0f,
                speed: 0f, blockDamage: 0f, creatureDamage: 0f, delivery: AbilityDelivery.SelfHeal, healAmount: 0.25f);

            var book = LoadOrCreate<AbilityBook>(BookPath);
            var serialized = new SerializedObject(book);
            SerializedProperty list = serialized.FindProperty("abilities");
            AbilityDefinition[] all =
            {
                manaBolt, arcaneOrb, pyroblast, earthShatter, runeHammer, bulwarkRune, hallowedForge, stoneSpear, risingPillar,
                earthenWall, wellspring, thunderhead, hoofquake, secondWind,
            };
            list.arraySize = all.Length;
            for (int i = 0; i < all.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = all[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(book);

            // What every hero starts with on their action bar (Roadmap 4.4). The Mana Bolt stays on the M.G.R.S.
            var loadout = LoadOrCreate<ActionBarLoadout>(LoadoutPath);
            serialized = new SerializedObject(loadout);
            list = serialized.FindProperty("slots");
            list.arraySize = PlayerActionBar.SlotCount;
            for (int i = 0; i < PlayerActionBar.SlotCount; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = null;
            list.GetArrayElementAtIndex(0).objectReferenceValue = arcaneOrb;
            list.GetArrayElementAtIndex(1).objectReferenceValue = pyroblast;
            list.GetArrayElementAtIndex(2).objectReferenceValue = earthShatter;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(loadout);

            // The Mana Bolt keeps D-019's look and sounds; the orb is bigger, purple, slower to fade and louder.
            AbilityLook manaBoltLook = MakeLook($"{LookDir}/ManaBoltLook.asset", manaBolt, new Color(0.35f, 0.85f, 1f),
                AbilityCoreShape.Gem, coreSize: 0.22f, trailTime: 0.18f, light: 4f, lightRange: 7f, muzzleSparks: 8,
                impactSparks: 28, impactSpeed: 5f, castMotes: 50f, castLight: 2.5f,
                castLoop: null, release: "BoltFire", impact: "BoltImpact", releaseVolume: 0.8f, impactVolume: 0.9f);
            AbilityLook arcaneOrbLook = MakeLook($"{LookDir}/ArcaneOrbLook.asset", arcaneOrb, new Color(0.8f, 0.4f, 1f),
                AbilityCoreShape.Orb, coreSize: 0.45f, trailTime: 0.35f, light: 6f, lightRange: 10f, muzzleSparks: 16,
                impactSparks: 70, impactSpeed: 7f, castMotes: 90f, castLight: 3f,
                castLoop: "CastHum", release: "OrbRelease", impact: "OrbImpact", releaseVolume: 0.9f, impactVolume: 1.1f,
                debrisChunks: 30, debrisSpeed: 6f, debrisTint: new Color(0.6f, 0.3f, 0.9f, 0.2f), shake: 0.3f, blastLight: 8f);
            // Pyroblast: a big orange fireball that roars while cast, and a boom that throws scorched rock.
            AbilityLook pyroblastLook = MakeLook($"{LookDir}/PyroblastLook.asset", pyroblast, new Color(1f, 0.45f, 0.1f),
                AbilityCoreShape.Orb, coreSize: 0.6f, trailTime: 0.5f, light: 9f, lightRange: 14f, muzzleSparks: 24,
                impactSparks: 110, impactSpeed: 10f, castMotes: 140f, castLight: 4.5f,
                castLoop: "FireRoar", release: "PyroRelease", impact: "PyroImpact", releaseVolume: 1f, impactVolume: 1.4f,
                debrisChunks: 60, debrisSpeed: 9f, debrisTint: new Color(0.12f, 0.08f, 0.06f, 0.35f), shake: 0.7f,
                blastLight: 18f, maxDistance: 70f);
            // EarthShatter: nothing flies, so it's all in the slam: heavy rock and a big shake.
            AbilityLook earthShatterLook = MakeLook($"{LookDir}/EarthShatterLook.asset", earthShatter,
                new Color(0.75f, 0.55f, 0.3f), AbilityCoreShape.Gem, coreSize: 0.3f, trailTime: 0.2f, light: 2f, lightRange: 6f,
                muzzleSparks: 0, impactSparks: 40, impactSpeed: 5f, castMotes: 0f, castLight: 0f,
                castLoop: null, release: null, impact: "EarthSlam", releaseVolume: 0.8f, impactVolume: 1.4f,
                debrisChunks: 90, debrisSpeed: 8f, debrisTint: new Color(0.35f, 0.25f, 0.15f, 0.25f), shake: 0.9f,
                blastLight: 3f, maxDistance: 60f);
            // Paladin: warm forge-gold. The hammer flies as a glowing hammer, tumbling.
            var paladinGold = new Color(1f, 0.78f, 0.3f);
            AbilityLook runeHammerLook = MakeLook($"{LookDir}/RuneHammerLook.asset", runeHammer, paladinGold,
                AbilityCoreShape.Gem, coreSize: 0.6f, trailTime: 0.25f, light: 5f, lightRange: 8f, muzzleSparks: 14,
                impactSparks: 45, impactSpeed: 6f, castMotes: 0f, castLight: 0f,
                castLoop: null, release: "HammerThrow", impact: "HammerClang", releaseVolume: 0.8f, impactVolume: 1f,
                coreMesh: LoadModelMesh(HammerModelPath, "RuneHammer"), coreSpin: 900f);
            AbilityLook bulwarkLook = MakeLook($"{LookDir}/BulwarkRuneLook.asset", bulwarkRune, new Color(1f, 0.86f, 0.42f),
                AbilityCoreShape.Gem, coreSize: 0.2f, trailTime: 0.2f, light: 3f, lightRange: 6f, muzzleSparks: 0,
                impactSparks: 30, impactSpeed: 4f, castMotes: 0f, castLight: 0f,
                castLoop: null, release: null, impact: "ShieldChime", releaseVolume: 0.7f, impactVolume: 0.8f);
            AbilityLook hallowedLook = MakeLook($"{LookDir}/HallowedForgeLook.asset", hallowedForge, paladinGold,
                AbilityCoreShape.Gem, coreSize: 0.2f, trailTime: 0.2f, light: 3.5f, lightRange: 12f, muzzleSparks: 0,
                impactSparks: 40, impactSpeed: 3f, castMotes: 70f, castLight: 3f,
                castLoop: "HolyHum", release: null, impact: "ForgeBless", releaseVolume: 0.9f, impactVolume: 0.9f,
                pulse: "HealPulse", pulseVolume: 0.35f);
            // Geomancer: warm earth-amber, like the glowing cracks in Earthen Rock.
            var geomancerAmber = new Color(0.95f, 0.6f, 0.25f);
            AbilityLook stoneSpearLook = MakeLook($"{LookDir}/StoneSpearLook.asset", stoneSpear, geomancerAmber,
                AbilityCoreShape.Gem, coreSize: 0.7f, trailTime: 0.3f, light: 4f, lightRange: 7f, muzzleSparks: 18,
                impactSparks: 50, impactSpeed: 6f, castMotes: 60f, castLight: 2.5f,
                castLoop: "CastHum", release: "SpearLaunch", impact: "SpearImpact", releaseVolume: 0.8f, impactVolume: 0.9f,
                debrisChunks: 25, debrisSpeed: 5f, debrisTint: new Color(0.35f, 0.25f, 0.15f, 0.25f), shake: 0.2f,
                coreMesh: LoadModelMesh(SpearModelPath, "StoneSpear"));
            AbilityLook risingPillarLook = MakeLook($"{LookDir}/RisingPillarLook.asset", risingPillar, geomancerAmber,
                AbilityCoreShape.Gem, coreSize: 0.2f, trailTime: 0.2f, light: 2f, lightRange: 6f, muzzleSparks: 0,
                impactSparks: 30, impactSpeed: 4f, castMotes: 0f, castLight: 0f,
                castLoop: null, release: "EarthRise", impact: null, releaseVolume: 0.9f, impactVolume: 0.8f);
            // Shaman: the wall shares the Geomancer's earth-amber; the totems are healing water and storm-blue.
            AbilityLook earthenWallLook = MakeLook($"{LookDir}/EarthenWallLook.asset", earthenWall, geomancerAmber,
                AbilityCoreShape.Gem, coreSize: 0.2f, trailTime: 0.2f, light: 2f, lightRange: 6f, muzzleSparks: 0,
                impactSparks: 30, impactSpeed: 4f, castMotes: 0f, castLight: 0f,
                castLoop: null, release: "EarthRise", impact: null, releaseVolume: 1f, impactVolume: 0.8f);
            AbilityLook wellspringLook = MakeLook($"{LookDir}/WellspringTotemLook.asset", wellspring, new Color(0.4f, 1f, 0.75f),
                AbilityCoreShape.Gem, coreSize: 0.2f, trailTime: 0.2f, light: 3f, lightRange: 9f, muzzleSparks: 0,
                impactSparks: 25, impactSpeed: 3f, castMotes: 25f, castLight: 0f,
                castLoop: null, release: null, impact: "TotemPlant", releaseVolume: 0.8f, impactVolume: 0.9f,
                zoneModel: LoadModelMesh($"{ModelDir}/TotemWellspring.fbx", "TotemWellspring"), zoneRing: 0.6f,
                pulse: "WaterPulse", pulseVolume: 0.4f);
            AbilityLook thunderheadLook = MakeLook($"{LookDir}/ThunderheadTotemLook.asset", thunderhead, new Color(0.55f, 0.75f, 1f),
                AbilityCoreShape.Gem, coreSize: 0.2f, trailTime: 0.2f, light: 4f, lightRange: 10f, muzzleSparks: 0,
                impactSparks: 30, impactSpeed: 6f, castMotes: 8f, castLight: 0f,
                castLoop: null, release: null, impact: "TotemPlant", releaseVolume: 0.9f, impactVolume: 0.9f,
                zoneModel: LoadModelMesh($"{ModelDir}/TotemThunderhead.fbx", "TotemThunderhead"), zoneRing: 0.25f,
                pulse: "ThunderZap", pulseVolume: 0.9f, maxDistance: 60f);

            // Racials: Hoofquake is all slam (dust, rock and a heavy shake); Second Wind is a soft green breath.
            AbilityLook hoofquakeLook = MakeLook($"{LookDir}/HoofquakeLook.asset", hoofquake, new Color(0.85f, 0.65f, 0.35f),
                AbilityCoreShape.Gem, coreSize: 0.2f, trailTime: 0.2f, light: 1.5f, lightRange: 6f, muzzleSparks: 0,
                impactSparks: 60, impactSpeed: 6f, castMotes: 0f, castLight: 0f,
                castLoop: null, release: null, impact: "Hoofquake", releaseVolume: 0.8f, impactVolume: 1.4f,
                debrisChunks: 70, debrisSpeed: 7f, debrisTint: new Color(0.4f, 0.3f, 0.18f, 0.3f), shake: 0.8f,
                blastLight: 0f, maxDistance: 50f);
            AbilityLook secondWindLook = MakeLook($"{LookDir}/SecondWindLook.asset", secondWind, new Color(0.55f, 1f, 0.6f),
                AbilityCoreShape.Gem, coreSize: 0.2f, trailTime: 0.2f, light: 2f, lightRange: 5f, muzzleSparks: 20,
                impactSparks: 0, impactSpeed: 3f, castMotes: 0f, castLight: 0f,
                castLoop: null, release: "SecondWind", impact: null, releaseVolume: 0.9f, impactVolume: 0.8f);

            var lookBook = LoadOrCreate<AbilityLookBook>(LookBookPath);
            serialized = new SerializedObject(lookBook);
            serialized.FindProperty("defaultLook").objectReferenceValue = manaBoltLook;
            list = serialized.FindProperty("looks");
            AbilityLook[] allLooks =
            {
                manaBoltLook, arcaneOrbLook, pyroblastLook, earthShatterLook, runeHammerLook, bulwarkLook, hallowedLook,
                stoneSpearLook, risingPillarLook, earthenWallLook, wellspringLook, thunderheadLook, hoofquakeLook,
                secondWindLook,
            };
            list.arraySize = allLooks.Length;
            for (int i = 0; i < allLooks.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = allLooks[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(lookBook);

            BuildClasses(loadout, earthShatter, runeHammer, bulwarkRune, hallowedForge, stoneSpear, risingPillar, earthenWall,
                wellspring, thunderhead);
            BuildRaces(hoofquake, secondWind);
            return manaBolt;
        }

        /// <summary>The races (Roadmap 4.7), in the order the race window shows them, and the <see cref="RaceBook"/>.</summary>
        static void BuildRaces(AbilityDefinition hoofquake, AbilityDefinition secondWind)
        {
            if (!AssetDatabase.IsValidFolder(RaceDir))
                AssetDatabase.CreateFolder("Assets/_Voxelforge/Data", "Races");

            RaceDefinition wayfarer = MakeRace("Wayfarer", RaceId.Wayfarer, "Wayfarer",
                "Hardy folk of the long roads, who'll sleep under any sky but always find their way home. Every hearth " +
                "from the Meadows to the Peaks has a Wayfarer story told round it.",
                homeland: "Meadows", bonusHealth: 0f, bonusMana: 10f, bonusManaRegen: 0f, secondWind,
                RacialPassive.Hearthbound, "Hearthbound",
                "The road always leads home: your Home Totem hearth takes half as long.", passiveValue: 0.5f);
            RaceDefinition thunderhoof = MakeRace("Thunderhoof", RaceId.Thunderhoof, "Thunderhoof",
                "Big, gentle bull-folk of the windswept plains, slow to anger and hard to move. They follow the herds " +
                "between the mesas and listen to the land, and their shamans are the oldest in the world.",
                homeland: "Thunderhoof Plains", bonusHealth: 20f, bonusMana: 0f, bonusManaRegen: 0f, hoofquake,
                RacialPassive.HerbSense, "Herb-Sense",
                "You know the plants of the plains by smell: gathering herbs gives half as many again.", passiveValue: 0.5f);

            var book = LoadOrCreate<RaceBook>(RaceBookPath);
            var serialized = new SerializedObject(book);
            SerializedProperty list = serialized.FindProperty("races");
            RaceDefinition[] order = { wayfarer, thunderhoof };
            list.arraySize = order.Length;
            for (int i = 0; i < order.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = order[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(book);
            foreach (string error in book.Validate())
                Debug.LogError($"[Voxelforge] Race book: {error}", book);
        }

        static RaceDefinition MakeRace(string name, RaceId id, string displayName, string description, string homeland,
            float bonusHealth, float bonusMana, float bonusManaRegen, AbilityDefinition racial, RacialPassive passive,
            string passiveName, string passiveDescription, float passiveValue)
        {
            var definition = LoadOrCreate<RaceDefinition>($"{RaceDir}/{name}.asset");
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("id").intValue = (int)id;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("description").stringValue = description;
            serialized.FindProperty("homeland").stringValue = homeland;
            serialized.FindProperty("bonusHealth").floatValue = bonusHealth;
            serialized.FindProperty("bonusMana").floatValue = bonusMana;
            serialized.FindProperty("bonusManaRegen").floatValue = bonusManaRegen;
            serialized.FindProperty("racialAbility").objectReferenceValue = racial;
            serialized.FindProperty("passive").intValue = (int)passive;
            serialized.FindProperty("passiveName").stringValue = passiveName;
            serialized.FindProperty("passiveDescription").stringValue = passiveDescription;
            serialized.FindProperty("passiveValue").floatValue = passiveValue;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        /// <summary>
        /// The classes (Roadmap 4.6). Slots 1-3 are each class's signature spells; the ones not built yet (steps C-E) are
        /// left empty, and the class window says "Coming soon".
        /// </summary>
        static void BuildClasses(ActionBarLoadout adventurerBar, AbilityDefinition earthShatter, AbilityDefinition runeHammer,
            AbilityDefinition bulwarkRune, AbilityDefinition hallowedForge, AbilityDefinition stoneSpear,
            AbilityDefinition risingPillar, AbilityDefinition earthenWall, AbilityDefinition wellspring, AbilityDefinition thunderhead)
        {
            if (!AssetDatabase.IsValidFolder(ClassDir))
                AssetDatabase.CreateFolder("Assets/_Voxelforge/Data", "Classes");

            ClassDefinition adventurer = MakeClass("Adventurer", HeroClassId.Adventurer, "Adventurer",
                "No calling yet: a wanderer with a bag of starter spells. Pick a path when you're ready, and change it whenever you like.",
                maxHealth: 100f, maxMana: 100f, manaRegen: 10f, adventurerBar);
            ClassDefinition paladin = MakeClass("RuneforgedPaladin", HeroClassId.RuneforgedPaladin, "Runeforged Paladin",
                "A holy knight in rune-carved plate, tough as an anvil. Hurls a glowing hammer, shields a partner and blesses " +
                "the ground so friends standing on it heal.",
                maxHealth: 150f, maxMana: 80f, manaRegen: 8f,
                MakeLoadout("RuneforgedPaladinBar", runeHammer, bulwarkRune, hallowedForge));
            ClassDefinition geomancer = MakeClass("GeomancerMage", HeroClassId.GeomancerMage, "Geomancer Mage",
                "A scholar of stone who bends the land itself. Flings stone spears, shatters the ground into craters and " +
                "rides a pillar of rock up out of trouble.",
                maxHealth: 90f, maxMana: 130f, manaRegen: 12f,
                MakeLoadout("GeomancerMageBar", stoneSpear, earthShatter, risingPillar));
            ClassDefinition shaman = MakeClass("Shaman", HeroClassId.Shaman, "Shaman",
                "A wise caller of the elements. Raises walls of living earth and plants totems that heal friends or call " +
                "down lightning on foes.",
                maxHealth: 110f, maxMana: 110f, manaRegen: 10f,
                MakeLoadout("ShamanBar", earthenWall, wellspring, thunderhead));

            var book = LoadOrCreate<ClassBook>(ClassBookPath);
            var serialized = new SerializedObject(book);
            SerializedProperty list = serialized.FindProperty("classes");
            ClassDefinition[] order = { adventurer, paladin, geomancer, shaman };
            list.arraySize = order.Length;
            for (int i = 0; i < order.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = order[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(book);
            foreach (string error in book.Validate())
                Debug.LogError($"[Voxelforge] Class book: {error}", book);

            // Each class's crest for the class window (a look, so it lives in the HUD skin), in HeroClassId order.
            var skin = AssetDatabase.LoadAssetAtPath<HudSkin>(HudSkinPath);
            if (skin != null)
            {
                skin.classCrests = new Sprite[order.Length];
                foreach (ClassDefinition definition in order)
                    skin.classCrests[(int)definition.Id] = IconSprite($"Class{definition.name}");
                EditorUtility.SetDirty(skin);
            }
        }

        static ActionBarLoadout MakeLoadout(string name, params AbilityDefinition[] abilities)
        {
            var loadout = LoadOrCreate<ActionBarLoadout>($"{ClassDir}/{name}.asset");
            var serialized = new SerializedObject(loadout);
            SerializedProperty list = serialized.FindProperty("slots");
            list.arraySize = PlayerActionBar.SlotCount;
            for (int i = 0; i < PlayerActionBar.SlotCount; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = i < abilities.Length ? abilities[i] : null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(loadout);
            return loadout;
        }

        static ClassDefinition MakeClass(string name, HeroClassId id, string displayName, string description, float maxHealth,
            float maxMana, float manaRegen, ActionBarLoadout loadout)
        {
            var definition = LoadOrCreate<ClassDefinition>($"{ClassDir}/{name}.asset");
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("id").intValue = (int)id;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("description").stringValue = description;
            serialized.FindProperty("maxHealth").floatValue = maxHealth;
            serialized.FindProperty("maxMana").floatValue = maxMana;
            serialized.FindProperty("manaRegen").floatValue = manaRegen;
            serialized.FindProperty("loadout").objectReferenceValue = loadout;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        static AbilityLook MakeLook(string path, AbilityDefinition ability, Color glow, AbilityCoreShape shape, float coreSize,
            float trailTime, float light, float lightRange, int muzzleSparks, int impactSparks, float impactSpeed,
            float castMotes, float castLight, string castLoop, string release, string impact, float releaseVolume,
            float impactVolume, int debrisChunks = 40, float debrisSpeed = 7f, Color debrisTint = default, float shake = 0.4f,
            float blastLight = 8f, float maxDistance = 40f, Mesh coreMesh = null, float coreSpin = 0f, Mesh zoneModel = null,
            float zoneRing = 1f, string pulse = null, float pulseVolume = 0.6f)
        {
            var look = LoadOrCreate<AbilityLook>(path);
            var serialized = new SerializedObject(look);
            serialized.FindProperty("ability").objectReferenceValue = ability;
            Sprite icon = IconSprite(ability.name);
            if (icon != null)
                serialized.FindProperty("icon").objectReferenceValue = icon;
            serialized.FindProperty("glow").colorValue = glow;
            serialized.FindProperty("coreShape").enumValueIndex = (int)shape;
            serialized.FindProperty("coreSize").floatValue = coreSize;
            serialized.FindProperty("trailTime").floatValue = trailTime;
            serialized.FindProperty("lightIntensity").floatValue = light;
            serialized.FindProperty("lightRange").floatValue = lightRange;
            serialized.FindProperty("muzzleSparks").intValue = muzzleSparks;
            serialized.FindProperty("impactSparks").intValue = impactSparks;
            serialized.FindProperty("impactSpeed").floatValue = impactSpeed;
            serialized.FindProperty("castMotesPerSecond").floatValue = castMotes;
            serialized.FindProperty("castLight").floatValue = castLight;
            serialized.FindProperty("castLoop").objectReferenceValue = Clip(castLoop);
            serialized.FindProperty("release").objectReferenceValue = Clip(release);
            serialized.FindProperty("releaseVolume").floatValue = releaseVolume;
            serialized.FindProperty("impact").objectReferenceValue = Clip(impact);
            serialized.FindProperty("impactVolume").floatValue = impactVolume;
            serialized.FindProperty("debrisChunks").intValue = debrisChunks;
            serialized.FindProperty("debrisSpeed").floatValue = debrisSpeed;
            serialized.FindProperty("debrisTint").colorValue = debrisTint;
            serialized.FindProperty("shakeStrength").floatValue = shake;
            serialized.FindProperty("blastLight").floatValue = blastLight;
            serialized.FindProperty("maxDistance").floatValue = maxDistance;
            serialized.FindProperty("coreMesh").objectReferenceValue = coreMesh;
            serialized.FindProperty("coreSpin").floatValue = coreSpin;
            serialized.FindProperty("zoneModel").objectReferenceValue = zoneModel;
            serialized.FindProperty("zoneRing").floatValue = zoneRing;
            serialized.FindProperty("pulse").objectReferenceValue = Clip(pulse);
            serialized.FindProperty("pulseVolume").floatValue = pulseVolume;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(look);
            return look;
        }

        static AudioClip Clip(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{SfxDir}/{name}.wav");
            if (clip == null)
                Debug.LogWarning($"[Voxelforge] No sound {name}.wav yet: run Voxelforge > Build Sound Effects, then Build Abilities again.");
            return clip;
        }

        static AbilityDefinition MakeAbility(string path, ushort id, string displayName, string description,
            AbilityElement element, float manaCost, float cooldown, float castTime, float range, float aoeRadius, float speed,
            float blockDamage, float creatureDamage, float craterFalloff = 0.25f, float craterFlatten = 1f,
            AbilityDelivery delivery = AbilityDelivery.Projectile, float healAmount = 0f, float shieldAmount = 0f,
            float duration = 0f, float pulseInterval = 1f, float aimCone = 12f, int earthHeight = 0, int earthWidth = 0,
            float earthStep = 0.08f, float manaRegenBonus = 0f)
        {
            var ability = LoadOrCreate<AbilityDefinition>(path);
            var serialized = new SerializedObject(ability);
            serialized.FindProperty("id").intValue = id;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("description").stringValue = description;
            serialized.FindProperty("element").enumValueIndex = (int)element;
            serialized.FindProperty("manaCost").floatValue = manaCost;
            serialized.FindProperty("cooldown").floatValue = cooldown;
            serialized.FindProperty("castTime").floatValue = castTime;
            serialized.FindProperty("castWhileMoving").boolValue = false;
            serialized.FindProperty("range").floatValue = range;
            serialized.FindProperty("aoeRadius").floatValue = aoeRadius;
            serialized.FindProperty("craterFalloff").floatValue = craterFalloff;
            serialized.FindProperty("craterFlatten").floatValue = craterFlatten;
            serialized.FindProperty("delivery").enumValueIndex = (int)delivery;
            serialized.FindProperty("projectileSpeed").floatValue = speed;
            serialized.FindProperty("blockDamage").floatValue = blockDamage;
            serialized.FindProperty("creatureDamage").floatValue = creatureDamage;
            serialized.FindProperty("healAmount").floatValue = healAmount;
            serialized.FindProperty("shieldAmount").floatValue = shieldAmount;
            serialized.FindProperty("duration").floatValue = duration;
            serialized.FindProperty("pulseInterval").floatValue = pulseInterval;
            serialized.FindProperty("aimCone").floatValue = aimCone;
            serialized.FindProperty("earthHeight").intValue = earthHeight;
            serialized.FindProperty("earthWidth").intValue = earthWidth;
            serialized.FindProperty("earthStep").floatValue = earthStep;
            serialized.FindProperty("manaRegenBonus").floatValue = manaRegenBonus;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ability);
            return ability;
        }

        /// <summary>
        /// The picture <c>Art/Textures/UI/Icons/&lt;name&gt;.png</c> from <c>build_spell_icons.py</c> as a sprite, setting it to
        /// import as one first if it's new. Null if there's no such picture yet.
        /// </summary>
        static Sprite IconSprite(string name)
        {
            string path = $"{IconDir}/{name}.png";
            if (AssetImporter.GetAtPath(path) is TextureImporter importer && importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>The mesh called <paramref name="name"/> in the model at <paramref name="path"/>, or null (with a warning).</summary>
        static Mesh LoadModelMesh(string path, string name)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Mesh mesh && mesh.name == name)
                    return mesh;
            }
            Debug.LogWarning($"[Voxelforge] No {name} mesh in {path} yet: run build_props.py in Blender, then Build Abilities again.");
            return null;
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;
            if (!AssetDatabase.IsValidFolder(AbilityDir))
                AssetDatabase.CreateFolder("Assets/_Voxelforge/Data", "Abilities");
            if (!AssetDatabase.IsValidFolder(LookDir))
                AssetDatabase.CreateFolder(AbilityDir, "Looks");
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
