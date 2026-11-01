using UnityEditor;
using UnityEngine;
using Voxelforge.Gameplay;

namespace Voxelforge.Editor
{
    /// <summary>
    /// <i>Voxelforge → Build Levels</i> (Roadmap 5.6, D-037): writes the <see cref="LevelBook"/> from the numbers below,
    /// the one place the level cap, the XP curve, what a level adds and what each mob is worth live. Re-running it
    /// updates the same asset in place.
    /// </summary>
    public static class LevelBuilder
    {
        internal const string BookPath = "Assets/_Voxelforge/Data/Levels/LevelBook.asset";

        const int MaxLevel = 20;

        // Level 1 → 2 takes this much XP (about 5 boars); each level after needs Growth times as much, rounded to 10s.
        // Level 5 is roughly one play session; 20 is a long-term goal.
        const int FirstLevelXp = 100;
        const float Growth = 1.35f;

        const float HealthPerLevel = 10f;
        const float ManaPerLevel = 5f;

        // Every hero this close to a defeated mob gets its full XP.
        const float ShareRadius = 40f;

        static readonly MobXp[] Mobs =
        {
            new MobXp(MobId.BristlebackBoar, 20),
            new MobXp(MobId.FrostfangWolf, 15),   // Packs, so a little less each.
            new MobXp(MobId.ThunderRhino, 40),    // The toughest overland mob.
            new MobXp(MobId.CrystalSlime, 25),    // Then its pieces: 2 medium and 4 small.
            new MobXp(MobId.CrystalSlimeMedium, 10),
            new MobXp(MobId.CrystalSlimeSmall, 5),
        };

        [MenuItem("Voxelforge/Build Levels")]
        public static void Build()
        {
            int[] curve = new int[MaxLevel - 1];
            for (int i = 0; i < curve.Length; i++)
                curve[i] = Mathf.RoundToInt(FirstLevelXp * Mathf.Pow(Growth, i) / 10f) * 10;

            var book = AssetDatabase.LoadAssetAtPath<LevelBook>(BookPath);
            if (book == null)
            {
                string folder = BookPath.Substring(0, BookPath.LastIndexOf('/'));
                if (!AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.CreateFolder("Assets/_Voxelforge/Data", "Levels");
                book = ScriptableObject.CreateInstance<LevelBook>();
                AssetDatabase.CreateAsset(book, BookPath);
            }
            var serialized = new SerializedObject(book);
            serialized.FindProperty("maxLevel").intValue = MaxLevel;
            SerializedProperty xp = serialized.FindProperty("xpToNext");
            xp.arraySize = curve.Length;
            for (int i = 0; i < curve.Length; i++)
                xp.GetArrayElementAtIndex(i).intValue = curve[i];
            serialized.FindProperty("healthPerLevel").floatValue = HealthPerLevel;
            serialized.FindProperty("manaPerLevel").floatValue = ManaPerLevel;
            serialized.FindProperty("shareRadius").floatValue = ShareRadius;
            SerializedProperty mobs = serialized.FindProperty("mobXp");
            mobs.arraySize = Mobs.Length;
            for (int i = 0; i < Mobs.Length; i++)
            {
                SerializedProperty entry = mobs.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("Mob").intValue = (int)Mobs[i].Mob;
                entry.FindPropertyRelative("Xp").intValue = Mobs[i].Xp;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(book);
            AssetDatabase.SaveAssets();

            var problems = book.Validate();
            if (problems.Count > 0)
                Debug.LogError("[Voxelforge] Build Levels made a book with problems:\n" + string.Join("\n", problems));
            else
                Debug.Log($"[Voxelforge] Built the Level Book: cap {MaxLevel}, level 2 at {curve[0]} XP, level {MaxLevel} at " +
                          $"{curve[curve.Length - 1]} XP for the last step.");
        }
    }
}
