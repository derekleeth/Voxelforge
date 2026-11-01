using System;
using System.Collections.Generic;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// One thing a profession gathers (Roadmap 5.7): an ore block, a herb or a mob's body, the skill it needs, what it
    /// gives and how long it takes.
    /// </summary>
    [Serializable]
    public struct GatherNode
    {
        public ProfessionId Profession;
        public GatherNodeKind Kind;

        [Tooltip("Ore: the block ID. Herb: the herb's item ID. Mob: the MobId.")]
        public ushort Id;

        [Tooltip("The skill needed to gather it at all.")]
        [Range(1, 255)] public int RequiredSkill;

        [Tooltip("What gathering it gives (rolled per gather). Ore drops its block's drop instead, so it can be empty.")]
        public LootTable Yield;

        [Tooltip("How long the gather cast takes, in seconds. Ore is mined as usual, so 0.")]
        [Min(0f)] public float GatherSeconds;

        public GatherNode(ProfessionId profession, GatherNodeKind kind, ushort id, int requiredSkill, LootTable yield = null,
            float gatherSeconds = 0f)
        {
            Profession = profession;
            Kind = kind;
            Id = id;
            RequiredSkill = requiredSkill;
            Yield = yield;
            GatherSeconds = gatherSeconds;
        }
    }

    /// <summary>
    /// Every number about the gathering professions (Roadmap 5.7, D-038): the skill cap, how likely a gather is to raise
    /// the skill, the bonus yield of a skilled gatherer, the skill that unlocks the World Map's heat map, and every
    /// node. <c>ProfessionBuilder</c> (Voxelforge &gt; Build Professions) writes it; don't edit it by hand.
    /// <see cref="ProfessionRules"/> does the sums.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Profession Book", fileName = "ProfessionBook")]
    public sealed class ProfessionBook : ScriptableObject
    {
        [Tooltip("The highest skill in each profession.")]
        [SerializeField, Range(2, 255)] int maxSkill = 100;

        [Tooltip("Up to this many points above a node's required skill, every gather raises the skill (orange).")]
        [SerializeField, Min(1)] int orangeRange = 10;

        [Tooltip("Up to this many points above, a gather usually raises the skill (yellow).")]
        [SerializeField, Min(1)] int yellowRange = 25;

        [Tooltip("Up to this many points above, a gather sometimes raises the skill (green). Past it: grey, never.")]
        [SerializeField, Min(1)] int greenRange = 40;

        [SerializeField, Range(0f, 1f)] float yellowChance = 0.6f;

        [SerializeField, Range(0f, 1f)] float greenChance = 0.25f;

        [Tooltip("The chance of one extra item per gather for a gatherer this many points above the node...")]
        [SerializeField, Min(1)] int bonusYieldRange = 50;

        [Tooltip("...rising to this chance (from 0 at the node's required skill).")]
        [SerializeField, Range(0f, 1f)] float maxBonusYieldChance = 0.5f;

        [Tooltip("The skill that unlocks the profession's heat map on the World Map.")]
        [SerializeField, Min(1)] int heatMapSkill = 10;

        [SerializeField] List<GatherNode> nodes = new List<GatherNode>();

        public int MaxSkill => maxSkill;
        public int OrangeRange => orangeRange;
        public int YellowRange => yellowRange;
        public int GreenRange => greenRange;
        public float YellowChance => yellowChance;
        public float GreenChance => greenChance;
        public int BonusYieldRange => bonusYieldRange;
        public float MaxBonusYieldChance => maxBonusYieldChance;
        public int HeatMapSkill => heatMapSkill;
        public IReadOnlyList<GatherNode> Nodes => nodes;

        /// <summary>The node for the ore block, herb or mob <paramref name="id"/>, if it's gatherable.</summary>
        public bool TryGetNode(GatherNodeKind kind, ushort id, out GatherNode node)
        {
            foreach (GatherNode entry in nodes)
            {
                if (entry.Kind == kind && entry.Id == id)
                {
                    node = entry;
                    return true;
                }
            }
            node = default;
            return false;
        }

        /// <summary>Returns every problem with the book. An empty list means it's valid.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            if (!(orangeRange < yellowRange && yellowRange < greenRange))
                errors.Add($"The skill-up ranges must grow: orange {orangeRange} < yellow {yellowRange} < green {greenRange}.");
            var seen = new HashSet<(GatherNodeKind, ushort)>();
            foreach (GatherNode node in nodes)
            {
                string name = $"{node.Kind} {node.Id}";
                if (!seen.Add((node.Kind, node.Id)))
                    errors.Add($"{name} has more than one node.");
                if (node.RequiredSkill < 1 || node.RequiredSkill > maxSkill)
                    errors.Add($"{name} needs skill {node.RequiredSkill}; it must be 1 to {maxSkill}.");
                if (node.Kind != GatherNodeKind.Ore && node.Yield == null)
                    errors.Add($"{name} gives nothing: it needs a yield table.");
                if (node.Kind != GatherNodeKind.Ore && node.GatherSeconds <= 0f)
                    errors.Add($"{name} needs a gather time.");
                ProfessionId expected = node.Kind switch
                {
                    GatherNodeKind.Ore => ProfessionId.Mining,
                    GatherNodeKind.Herb => ProfessionId.Herbalism,
                    _ => ProfessionId.Skinning,
                };
                if (node.Profession != expected)
                    errors.Add($"{name} is gathered by {node.Profession}, but {node.Kind} nodes belong to {expected}.");
            }
            return errors;
        }

        /// <summary>Builds a book in code, for tests and tools.</summary>
        internal static ProfessionBook Create(int maxSkill = 100, IEnumerable<GatherNode> nodes = null, int orangeRange = 10,
            int yellowRange = 25, int greenRange = 40, float yellowChance = 0.6f, float greenChance = 0.25f,
            int bonusYieldRange = 50, float maxBonusYieldChance = 0.5f, int heatMapSkill = 10)
        {
            var book = CreateInstance<ProfessionBook>();
            book.maxSkill = maxSkill;
            book.orangeRange = orangeRange;
            book.yellowRange = yellowRange;
            book.greenRange = greenRange;
            book.yellowChance = yellowChance;
            book.greenChance = greenChance;
            book.bonusYieldRange = bonusYieldRange;
            book.maxBonusYieldChance = maxBonusYieldChance;
            book.heatMapSkill = heatMapSkill;
            book.nodes = new List<GatherNode>(nodes ?? Array.Empty<GatherNode>());
            return book;
        }
    }
}
