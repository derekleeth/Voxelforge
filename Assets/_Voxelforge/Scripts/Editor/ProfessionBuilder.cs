using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Voxelforge.Gameplay;

namespace Voxelforge.Editor
{
    /// <summary>
    /// <i>Voxelforge → Build Professions</i> (Roadmap 5.7, D-038): writes the <see cref="ProfessionBook"/> from the
    /// numbers below, the one place the skill cap, how skills rise, a skilled gatherer's bonus and every gatherable node
    /// live. Re-running it updates the same asset in place.
    /// </summary>
    public static class ProfessionBuilder
    {
        internal const string BookPath = "Assets/_Voxelforge/Data/Professions/ProfessionBook.asset";

        const int MaxSkill = 100;

        // WoW-style: a node raises the skill every time for its first 10 points, usually for the next 15, now and then
        // for 15 more, then never ("grey"), so gatherers move on to the next tier.
        const int OrangeRange = 10;
        const int YellowRange = 25;
        const int GreenRange = 40;
        const float YellowChance = 0.6f;
        const float GreenChance = 0.25f;

        // A gatherer 50 points above a node has a 50% chance of one extra item.
        const int BonusYieldRange = 50;
        const float MaxBonusYieldChance = 0.5f;

        // The skill that shows a profession's heat map on the World Map.
        const int HeatMapSkill = 10;

        // Steps B-D add the ores, herbs and mobs.
        static readonly GatherNode[] Nodes = { };

        [MenuItem("Voxelforge/Build Professions")]
        public static void Build()
        {
            var book = AssetDatabase.LoadAssetAtPath<ProfessionBook>(BookPath);
            if (book == null)
            {
                string folder = BookPath.Substring(0, BookPath.LastIndexOf('/'));
                if (!AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.CreateFolder("Assets/_Voxelforge/Data", "Professions");
                book = ScriptableObject.CreateInstance<ProfessionBook>();
                AssetDatabase.CreateAsset(book, BookPath);
            }
            var serialized = new SerializedObject(book);
            serialized.FindProperty("maxSkill").intValue = MaxSkill;
            serialized.FindProperty("orangeRange").intValue = OrangeRange;
            serialized.FindProperty("yellowRange").intValue = YellowRange;
            serialized.FindProperty("greenRange").intValue = GreenRange;
            serialized.FindProperty("yellowChance").floatValue = YellowChance;
            serialized.FindProperty("greenChance").floatValue = GreenChance;
            serialized.FindProperty("bonusYieldRange").intValue = BonusYieldRange;
            serialized.FindProperty("maxBonusYieldChance").floatValue = MaxBonusYieldChance;
            serialized.FindProperty("heatMapSkill").intValue = HeatMapSkill;
            WriteNodes(serialized.FindProperty("nodes"), Nodes);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(book);
            AssetDatabase.SaveAssets();

            List<string> problems = book.Validate();
            if (problems.Count > 0)
                Debug.LogError("[Voxelforge] Build Professions made a book with problems:\n" + string.Join("\n", problems));
            else
                Debug.Log($"[Voxelforge] Built the Profession Book: skills 1-{MaxSkill}, {Nodes.Length} gatherable nodes, " +
                          $"heat maps at {HeatMapSkill}.");
        }

        static void WriteNodes(SerializedProperty list, IReadOnlyList<GatherNode> nodes)
        {
            list.arraySize = nodes.Count;
            for (int i = 0; i < nodes.Count; i++)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                GatherNode node = nodes[i];
                entry.FindPropertyRelative("Profession").intValue = (int)node.Profession;
                entry.FindPropertyRelative("Kind").intValue = (int)node.Kind;
                entry.FindPropertyRelative("Id").intValue = node.Id;
                entry.FindPropertyRelative("RequiredSkill").intValue = node.RequiredSkill;
                entry.FindPropertyRelative("Yield").objectReferenceValue = node.Yield;
                entry.FindPropertyRelative("GatherSeconds").floatValue = node.GatherSeconds;
            }
        }
    }
}
