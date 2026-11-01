using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The sounds creatures make, for every mob on every PC (Roadmap 5.2 step G): a grunt, howl or bellow when one picks
    /// a fight, its windup, its hit landing, a yelp when hurt and its death. Each kind's clips are in its
    /// <see cref="MobDefinition.Sounds"/>; the moments come from the <see cref="MobCueKind"/>s the host sends
    /// (<see cref="Mob.Cued"/>). Presentation only: it plays them through the <see cref="SoundRouter"/>, so every local
    /// player hears them from where they stand (D-022).
    /// </summary>
    public sealed class MobFoley : MonoBehaviour
    {
        [SerializeField] SoundRouter router;

        [SerializeField, Range(0f, 2f)] float volume = 0.8f;

        [Tooltip("A call (a wolf's howl) carries this far, in metres.")]
        [SerializeField, Min(1f)] float callDistance = 48f;

        [Tooltip("Every other mob sound carries this far, in metres.")]
        [SerializeField, Min(1f)] float maxDistance = 28f;

        [Tooltip("Least time between one mob's hurt sounds, in seconds, so a flurry of hits doesn't stack up.")]
        [SerializeField, Range(0f, 0.5f)] float minHurtInterval = 0.15f;

        readonly Dictionary<Mob, Action<Mob, MobCueKind, int>> _listening = new Dictionary<Mob, Action<Mob, MobCueKind, int>>();
        readonly Dictionary<Mob, float> _lastHurt = new Dictionary<Mob, float>();

        void OnEnable()
        {
            CombatCreatures.AnySpawned += OnSpawned;
            CombatCreatures.AnyDespawned += OnDespawned;
        }

        void OnDisable()
        {
            CombatCreatures.AnySpawned -= OnSpawned;
            CombatCreatures.AnyDespawned -= OnDespawned;
            foreach (KeyValuePair<Mob, Action<Mob, MobCueKind, int>> mob in _listening)
            {
                if (mob.Key != null)
                    mob.Key.Cued -= mob.Value;
            }
            _listening.Clear();
            _lastHurt.Clear();
        }

        void OnSpawned(ICombatCreature creature)
        {
            if (creature is Mob mob && !_listening.ContainsKey(mob))
            {
                Action<Mob, MobCueKind, int> listener = OnCue;
                _listening[mob] = listener;
                mob.Cued += listener;
            }
        }

        void OnDespawned(ICombatCreature creature)
        {
            if (creature is Mob mob && _listening.TryGetValue(mob, out Action<Mob, MobCueKind, int> listener))
            {
                mob.Cued -= listener;
                _listening.Remove(mob);
                _lastHurt.Remove(mob);
            }
        }

        void OnCue(Mob mob, MobCueKind kind, int attack)
        {
            if (router == null || mob == null || mob.Definition == null)
                return;
            MobSounds sounds = mob.Definition.Sounds;
            AudioClip clip;
            float distance = maxDistance, loudness = 1f, pitch = 1f;
            switch (kind)
            {
                case MobCueKind.Call:
                    clip = sounds.Call;
                    distance = callDistance;
                    break;
                case MobCueKind.Windup:
                    clip = IsCharge(mob, attack) && sounds.Charge != null ? sounds.Charge : sounds.Attack;
                    break;
                case MobCueKind.Strike:
                    clip = sounds.Strike;
                    break;
                case MobCueKind.Hurt:
                    float now = Time.time;
                    if (_lastHurt.TryGetValue(mob, out float last) && now - last < minHurtInterval)
                        return;
                    _lastHurt[mob] = now;
                    clip = sounds.Hurt;
                    loudness = 0.8f;
                    break;
                case MobCueKind.Defeated:
                    clip = sounds.Death;
                    break;
                case MobCueKind.Dazed:
                    // Running into a wall: its strike thud, lower.
                    clip = sounds.Strike;
                    pitch = 0.75f;
                    break;
                default:
                    return;
            }
            if (clip == null)
                return;
            float3 at = mob.Feet + new float3(0f, mob.HalfExtents.y, 0f);
            float baseline = sounds.Pitch > 0f ? sounds.Pitch : 1f;
            router.Play(clip, at, volume * loudness, baseline * pitch * UnityEngine.Random.Range(0.94f, 1.06f), distance);
        }

        static bool IsCharge(Mob mob, int attack)
        {
            MobAttack[] attacks = mob.Definition.Attacks;
            return attack >= 0 && attack < attacks.Length && attacks[attack].Kind == MobAttackKind.Charge;
        }
    }
}
