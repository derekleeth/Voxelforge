using System;
using Unity.Netcode;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// This hero's one channel for <see cref="AbilityCue"/>s (Roadmap 4.2): the host's <see cref="PlayerCaster"/> and
    /// <see cref="PlayerStaff"/> say what their abilities did, and every PC hears it in order. The cast bar and cooldowns
    /// (<see cref="PlayerCaster"/>), the bolts (<see cref="ManaBoltView"/>), the cast glow (<see cref="CastingView"/>) and
    /// the sounds (<see cref="PlayerFoley"/>) all listen here, and none of them can change the game.
    /// </summary>
    public sealed class PlayerAbilityCues : NetworkBehaviour
    {
        PlayerCaster _caster;

        /// <summary>Every PC: a cue arrived, with its ability looked up (never null).</summary>
        public event Action<AbilityCue, AbilityDefinition> Received;

        /// <summary>Every PC: <see cref="Time.time"/> when this PC last heard of a projectile, for the recoil.</summary>
        public float LastShotTime { get; private set; } = float.NegativeInfinity;

        void Awake() => _caster = GetComponent<PlayerCaster>();

        /// <summary>Host only: tells every PC (this one too) what an ability did.</summary>
        public void ServerSend(in AbilityCue cue)
        {
            if (IsServer && IsSpawned)
                CueRpc(cue);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void CueRpc(AbilityCue cue)
        {
            AbilityBook book = _caster != null ? _caster.Book : null;
            if (book == null || !book.TryGet(cue.AbilityId, out AbilityDefinition ability))
                return;
            if (cue.Kind == AbilityCueKind.Launched)
                LastShotTime = Time.time;
            Received?.Invoke(cue, ability);
        }
    }
}
