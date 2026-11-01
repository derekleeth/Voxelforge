using System;
using Unity.Netcode;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// A hero's gathering skills (Roadmap 5.7, D-038). <b>The host</b> raises them when the hero gathers (<see
    /// cref="ServerGathered"/>, with <see cref="ProfessionRules"/>) and saves them in the profile. <b>Every PC</b> sees the
    /// synced skills (the bag window's professions panel, which tiers show on the heat map), and the owner hears about
    /// skill-ups for "Mining 12" text in their own view.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerProfessions : NetworkBehaviour
    {
        [Tooltip("Data/Professions/ProfessionBook.asset (built by Voxelforge > Build Professions).")]
        [SerializeField] ProfessionBook book;

        readonly NetworkVariable<ProfessionSkills> _skills = new NetworkVariable<ProfessionSkills>(ProfessionSkills.Start);
        readonly System.Random _random = new System.Random(Environment.TickCount);

        /// <summary>Every PC: the skills, as the host last said.</summary>
        public ProfessionSkills Skills => _skills.Value;

        public ProfessionBook Book => book;

        /// <summary>Every PC: a skill changed.</summary>
        public event Action<ProfessionSkills> Changed;

        /// <summary>Owner only: <see cref="ProfessionId"/> went up to this skill, for "Mining 12" in their own view.</summary>
        public event Action<ProfessionId, int> SkilledUp;

        public override void OnNetworkSpawn() => _skills.OnValueChanged += OnSkillsChanged;

        public override void OnNetworkDespawn()
        {
            _skills.OnValueChanged -= OnSkillsChanged;
            base.OnNetworkDespawn();
        }

        void OnSkillsChanged(ProfessionSkills before, ProfessionSkills after) => Changed?.Invoke(after);

        /// <summary>Every PC: true if the hero's skill is enough for <paramref name="node"/>.</summary>
        public bool CanGather(in GatherNode node) => ProfessionRules.CanGather(_skills.Value, node);

        /// <summary>
        /// Host only: the hero just gathered <paramref name="node"/>, which may raise the skill. True if it went up.
        /// </summary>
        public bool ServerGathered(in GatherNode node)
        {
            if (!IsServer || book == null)
                return false;
            if (!ProfessionRules.TryGain(_skills.Value, node.Profession, node.RequiredSkill, book, _random.NextDouble(),
                    out ProfessionSkills after))
                return false;
            _skills.Value = after;
            SkilledUpRpc((byte)node.Profession, (byte)after.Get(node.Profession));
            return true;
        }

        /// <summary>Host only: one roll for a bonus item from <paramref name="node"/>, more likely for a skilled gatherer.</summary>
        public bool ServerRollBonusYield(in GatherNode node)
        {
            if (!IsServer || book == null)
                return false;
            float chance = ProfessionRules.BonusYieldChance(_skills.Value.Get(node.Profession), node.RequiredSkill, book);
            return _random.NextDouble() < chance;
        }

        /// <summary>Host only, right after spawning: the skills from the saved profile, within the cap.</summary>
        public void ServerRestore(ProfessionSkills saved)
        {
            if (!IsServer)
                return;
            int cap = book != null ? book.MaxSkill : byte.MaxValue;
            ProfessionSkills skills = saved;
            for (int i = 0; i < ProfessionSkills.Count; i++)
            {
                var profession = (ProfessionId)i;
                skills = skills.With(profession, Mathf.Min(saved.Get(profession), cap));
            }
            _skills.Value = skills;
        }

        /// <summary>Host only, a play-test key: every profession up by <paramref name="amount"/> (within 1 and the cap).</summary>
        public void ServerAddToAll(int amount)
        {
            if (!IsServer)
                return;
            int cap = book != null ? book.MaxSkill : byte.MaxValue;
            ProfessionSkills skills = _skills.Value;
            for (int i = 0; i < ProfessionSkills.Count; i++)
            {
                var profession = (ProfessionId)i;
                skills = skills.With(profession, Mathf.Clamp(skills.Get(profession) + amount, 1, cap));
            }
            _skills.Value = skills;
        }

        /// <summary>Host only, a play-test key: every profession back to 1.</summary>
        public void ServerReset()
        {
            if (IsServer)
                _skills.Value = ProfessionSkills.Start;
        }

        [Rpc(SendTo.Owner)]
        void SkilledUpRpc(byte profession, byte skill) => SkilledUp?.Invoke((ProfessionId)profession, skill);
    }
}
