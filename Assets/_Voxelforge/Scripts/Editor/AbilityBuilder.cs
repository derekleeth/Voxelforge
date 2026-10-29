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
    /// <item><b>Arcane Orb</b>: a slow, heavy test spell with a 1.5 s cast, on the temporary Ability 1 button.</item>
    /// </list>
    /// </summary>
    public static class AbilityBuilder
    {
        const string AbilityDir = "Assets/_Voxelforge/Data/Abilities";
        public const string ManaBoltPath = AbilityDir + "/ManaBolt.asset";
        public const string ArcaneOrbPath = AbilityDir + "/ArcaneOrb.asset";
        public const string BookPath = AbilityDir + "/AbilityBook.asset";

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
                speed: 40f, blockDamage: 45f, creatureDamage: 20f, glow: new Color(0.35f, 0.85f, 1f));
            AbilityDefinition arcaneOrb = MakeAbility(ArcaneOrbPath, AbilityIds.ArcaneOrb, "Arcane Orb",
                "Gather your mana into a slow, heavy orb. Stand still while you cast it.",
                AbilityElement.Arcane, manaCost: 30f, cooldown: 6f, castTime: 1.5f, range: 30f, aoeRadius: 3f,
                speed: 18f, blockDamage: 120f, creatureDamage: 60f, glow: new Color(0.8f, 0.4f, 1f));

            var book = LoadOrCreate<AbilityBook>(BookPath);
            var serialized = new SerializedObject(book);
            SerializedProperty list = serialized.FindProperty("abilities");
            list.arraySize = 2;
            list.GetArrayElementAtIndex(0).objectReferenceValue = manaBolt;
            list.GetArrayElementAtIndex(1).objectReferenceValue = arcaneOrb;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(book);
            return manaBolt;
        }

        static AbilityDefinition MakeAbility(string path, ushort id, string displayName, string description,
            AbilityElement element, float manaCost, float cooldown, float castTime, float range, float aoeRadius, float speed,
            float blockDamage, float creatureDamage, Color glow)
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
            serialized.FindProperty("delivery").enumValueIndex = (int)AbilityDelivery.Projectile;
            serialized.FindProperty("projectileSpeed").floatValue = speed;
            serialized.FindProperty("blockDamage").floatValue = blockDamage;
            serialized.FindProperty("creatureDamage").floatValue = creatureDamage;
            serialized.FindProperty("glow").colorValue = glow;
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
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
