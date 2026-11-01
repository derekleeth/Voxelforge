using System;
using Unity.Netcode;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// A hero's level and XP (Roadmap 5.6, D-037). <b>The host</b> grants XP (<see cref="ExperienceAwards"/>), works out
    /// level-ups with <see cref="LevelRules"/>, refills health and mana on a level-up and saves it in the profile;
    /// <see cref="PlayerClass"/> adds the level's health and mana to the hero's pools. <b>Every PC</b> sees the synced
    /// state (the HUD's level badge and XP bar) and hears about level-ups for the celebration.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerLevel : NetworkBehaviour
    {
        [Tooltip("Data/Levels/LevelBook.asset (built by Voxelforge > Build Levels).")]
        [SerializeField] LevelBook book;

        readonly NetworkVariable<LevelState> _state = new NetworkVariable<LevelState>(LevelState.Start);

        PlayerHealth _health;
        PlayerCaster _caster;

        /// <summary>Every PC: the level and XP, as the host last said.</summary>
        public LevelState State => _state.Value;

        public int Level => _state.Value.EffectiveLevel;

        public LevelBook Book => book;

        /// <summary>Every PC: the level or XP changed. The host's <see cref="PlayerClass"/> updates the pools from it.</summary>
        public event Action<LevelState> Changed;

        /// <summary>Every PC: the hero reached a new level (the number), for the fanfare and the pillar of light.</summary>
        public event Action<int> LeveledUp;

        /// <summary>Owner only: the hero earned this much XP, for "+20 XP" in their own view.</summary>
        public event Action<int> XpGained;

        void Awake()
        {
            _health = GetComponent<PlayerHealth>();
            _caster = GetComponent<PlayerCaster>();
        }

        public override void OnNetworkSpawn() => _state.OnValueChanged += OnStateChanged;

        public override void OnNetworkDespawn()
        {
            _state.OnValueChanged -= OnStateChanged;
            base.OnNetworkDespawn();
        }

        void OnStateChanged(LevelState before, LevelState after) => Changed?.Invoke(after);

        /// <summary>Host only: the hero earns <paramref name="xp"/>, maybe going up a level or more.</summary>
        public void ServerGrant(int xp)
        {
            if (!IsServer || book == null || xp <= 0)
                return;
            LevelState after = LevelRules.Gain(_state.Value, xp, book, out int gained);
            _state.Value = after;
            XpGainedRpc(xp);
            if (gained > 0)
                ServerLevelUp(after.EffectiveLevel);
        }

        /// <summary>Host only, right after spawning: the level from the saved profile, within the cap.</summary>
        public void ServerRestore(LevelState saved)
        {
            if (!IsServer)
                return;
            int cap = book != null ? book.MaxLevel : byte.MaxValue;
            _state.Value = saved.EffectiveLevel >= cap ? new LevelState(cap, 0) : new LevelState(saved.EffectiveLevel, saved.Xp);
        }

        /// <summary>Host only, a play-test key: straight to <paramref name="level"/> (within the cap), with no XP into it.</summary>
        public void ServerSetLevel(int level)
        {
            if (!IsServer)
                return;
            int cap = book != null ? book.MaxLevel : byte.MaxValue;
            level = Mathf.Clamp(level, 1, cap);
            bool up = level > Level;
            _state.Value = new LevelState(level, 0);
            if (up)
                ServerLevelUp(level);
        }

        // The new level's pools are already set (PlayerClass heard Changed); fill them up and tell everyone.
        void ServerLevelUp(int level)
        {
            if (_health != null)
                _health.ServerRefill();
            if (_caster != null)
                _caster.ServerRefillMana();
            LeveledUpRpc((byte)level);
        }

        [Rpc(SendTo.Owner)]
        void XpGainedRpc(int xp) => XpGained?.Invoke(xp);

        [Rpc(SendTo.Everyone)]
        void LeveledUpRpc(byte level) => LeveledUp?.Invoke(level);
    }
}
