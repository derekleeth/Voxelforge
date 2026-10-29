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
    /// <item><b>Arcane Orb</b>: a slow, heavy test spell with a 1.5 s cast and a small crater, on the temporary Ability 1
    /// button.</item>
    /// <item><b>Pyroblast</b> (Roadmap 4.3): a fire orb with a 2.5 s cast that blasts a deep, round crater, on Ability 2.</item>
    /// <item><b>EarthShatter</b> (Roadmap 4.3): an instant slam on the ground you aim at that blasts a wide, shallow
    /// crater, on Ability 3.</item>
    /// </list>
    /// It also writes how each looks and sounds (<see cref="AbilityLook"/>, Roadmap 4.2) in <c>Looks/</c>, using the
    /// sounds from <i>Voxelforge → Build Sound Effects</i>, so build those first.
    /// </summary>
    public static class AbilityBuilder
    {
        const string AbilityDir = "Assets/_Voxelforge/Data/Abilities";
        public const string ManaBoltPath = AbilityDir + "/ManaBolt.asset";
        public const string ArcaneOrbPath = AbilityDir + "/ArcaneOrb.asset";
        public const string PyroblastPath = AbilityDir + "/Pyroblast.asset";
        public const string EarthShatterPath = AbilityDir + "/EarthShatter.asset";
        public const string BookPath = AbilityDir + "/AbilityBook.asset";
        const string LookDir = AbilityDir + "/Looks";
        public const string LookBookPath = LookDir + "/AbilityLookBook.asset";
        const string SfxDir = "Assets/_Voxelforge/Art/Audio/Sfx";

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
                AbilityElement.Arcane, manaCost: 30f, cooldown: 6f, castTime: 1.5f, range: 30f, aoeRadius: 3f,
                speed: 18f, blockDamage: 120f, creatureDamage: 60f, craterFalloff: 0.15f, craterFlatten: 0.8f);
            // Craters (Roadmap 4.3): blockDamage is at the middle, falling to craterFalloff of it at the edge, so with
            // these numbers Pyroblast clears dirt across the whole crater, stone out to about 3.3 m and ore to 1.7 m.
            AbilityDefinition pyroblast = MakeAbility(PyroblastPath, AbilityIds.Pyroblast, "Pyroblast",
                "Hurl a huge ball of fire that blasts a crater out of the ground. Stand still while you cast it.",
                AbilityElement.Fire, manaCost: 45f, cooldown: 10f, castTime: 2.5f, range: 40f, aoeRadius: 4f,
                speed: 22f, blockDamage: 240f, creatureDamage: 120f, craterFalloff: 0.1f, craterFlatten: 1f);
            AbilityDefinition earthShatter = MakeAbility(EarthShatterPath, AbilityIds.EarthShatter, "EarthShatter",
                "Slam the ground where you look and shatter it into a wide, shallow bowl.",
                AbilityElement.Earth, manaCost: 35f, cooldown: 8f, castTime: 0f, range: 20f, aoeRadius: 4.5f,
                speed: 0f, blockDamage: 150f, creatureDamage: 80f, craterFalloff: 0.2f, craterFlatten: 0.45f,
                delivery: AbilityDelivery.GroundSlam);

            var book = LoadOrCreate<AbilityBook>(BookPath);
            var serialized = new SerializedObject(book);
            SerializedProperty list = serialized.FindProperty("abilities");
            list.arraySize = 4;
            list.GetArrayElementAtIndex(0).objectReferenceValue = manaBolt;
            list.GetArrayElementAtIndex(1).objectReferenceValue = arcaneOrb;
            list.GetArrayElementAtIndex(2).objectReferenceValue = pyroblast;
            list.GetArrayElementAtIndex(3).objectReferenceValue = earthShatter;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(book);

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

            var lookBook = LoadOrCreate<AbilityLookBook>(LookBookPath);
            serialized = new SerializedObject(lookBook);
            serialized.FindProperty("defaultLook").objectReferenceValue = manaBoltLook;
            list = serialized.FindProperty("looks");
            list.arraySize = 4;
            list.GetArrayElementAtIndex(0).objectReferenceValue = manaBoltLook;
            list.GetArrayElementAtIndex(1).objectReferenceValue = arcaneOrbLook;
            list.GetArrayElementAtIndex(2).objectReferenceValue = pyroblastLook;
            list.GetArrayElementAtIndex(3).objectReferenceValue = earthShatterLook;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(lookBook);
            return manaBolt;
        }

        static AbilityLook MakeLook(string path, AbilityDefinition ability, Color glow, AbilityCoreShape shape, float coreSize,
            float trailTime, float light, float lightRange, int muzzleSparks, int impactSparks, float impactSpeed,
            float castMotes, float castLight, string castLoop, string release, string impact, float releaseVolume,
            float impactVolume, int debrisChunks = 40, float debrisSpeed = 7f, Color debrisTint = default, float shake = 0.4f,
            float blastLight = 8f, float maxDistance = 40f)
        {
            var look = LoadOrCreate<AbilityLook>(path);
            var serialized = new SerializedObject(look);
            serialized.FindProperty("ability").objectReferenceValue = ability;
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
            AbilityDelivery delivery = AbilityDelivery.Projectile)
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
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ability);
            return ability;
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
