using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Shakes each local player's own view when a spell blasts a crater near their hero (Roadmap 4.3). It hears the
    /// <see cref="AbilityCue"/>s of every hero on this PC, local or remote, and shakes each view by how close that view's
    /// hero is, using the spell's <see cref="AbilityLook.ShakeStrength"/>. A partner across the map isn't shaken
    /// (CLAUDE.md Law #5). Presentation only.
    /// <para>Owned by <see cref="SplitScreenDirector"/>, which shares its rig and player arrays, so nothing is global.</para>
    /// </summary>
    public sealed class BlastShake : IDisposable
    {
        /// <summary>A blast is felt out to this many times its crater radius.</summary>
        const float ReachPerRadius = 6f;

        readonly PlayerCameraRig[] _rigs;
        readonly PlayerController[] _heroes;
        readonly AbilityLookBook _looks;
        readonly Dictionary<PlayerAbilityCues, Action<AbilityCue, AbilityDefinition>> _listening =
            new Dictionary<PlayerAbilityCues, Action<AbilityCue, AbilityDefinition>>();

        /// <param name="rigs">Player cameras by local slot (entries may be null).</param>
        /// <param name="heroes">Heroes by local slot, null when not playing. Read on every blast, so the caller keeps it current.</param>
        /// <param name="looks">How hard each spell shakes. Null = a gentle default for every crater.</param>
        public BlastShake(PlayerCameraRig[] rigs, PlayerController[] heroes, AbilityLookBook looks)
        {
            _rigs = rigs;
            _heroes = heroes;
            _looks = looks;
            PlayerController.AnySpawned += Listen;
            PlayerController.AnyDespawned += Forget;
        }

        public void Dispose()
        {
            PlayerController.AnySpawned -= Listen;
            PlayerController.AnyDespawned -= Forget;
            foreach (var pair in _listening)
            {
                if (pair.Key != null)
                    pair.Key.Received -= pair.Value;
            }
            _listening.Clear();
        }

        /// <summary>How hard a blast of <paramref name="radius"/> and <paramref name="strength"/> shakes a hero <paramref name="distance"/> metres away.</summary>
        public static float ShakeAt(float distance, float radius, float strength)
        {
            if (radius <= 0f || strength <= 0f)
                return 0f;
            float reach = radius * ReachPerRadius;
            // Full strength inside the crater, fading to nothing at the reach.
            return strength * math.saturate(1f - (distance - radius) / (reach - radius));
        }

        void Listen(PlayerController hero)
        {
            if (hero == null || !hero.TryGetComponent(out PlayerAbilityCues cues) || _listening.ContainsKey(cues))
                return;
            Action<AbilityCue, AbilityDefinition> handler = (cue, ability) => OnCue(cue, ability);
            cues.Received += handler;
            _listening.Add(cues, handler);
        }

        void Forget(PlayerController hero)
        {
            if (hero == null || !hero.TryGetComponent(out PlayerAbilityCues cues) || !_listening.TryGetValue(cues, out var handler))
                return;
            cues.Received -= handler;
            _listening.Remove(cues);
        }

        void OnCue(AbilityCue cue, AbilityDefinition ability)
        {
            if (cue.Kind != AbilityCueKind.Impact || ability == null || ability.AoeRadius <= 0f)
                return;
            AbilityLook look = _looks != null ? _looks.For(ability) : null;
            float strength = look != null ? look.ShakeStrength : 0.3f;
            for (int slot = 0; slot < _rigs.Length && slot < _heroes.Length; slot++)
            {
                if (_rigs[slot] == null || _heroes[slot] == null)
                    continue;
                float distance = Vector3.Distance(_heroes[slot].transform.position, cue.Position);
                float amount = ShakeAt(distance, ability.AoeRadius, strength);
                if (amount > 0f)
                    _rigs[slot].Shake(amount);
            }
        }
    }
}
