using System;
using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Turns what the host said about this hero into <see cref="CombatTextEvent"/>s for the floating combat text in their
    /// own view (Roadmap 4.4): spell hits and craters from <see cref="AbilityCue"/>s, mana spent when a spell goes off,
    /// refused casts and items picked up, and damage, heals and shields from <see cref="PlayerHealth"/> (4.6). It only listens, so it can't change the game (Roadmap 4.2).
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(PlayerAbilityCues), typeof(PlayerCaster))]
    public sealed class PlayerCombatText : MonoBehaviour
    {
        [Tooltip("How far above the feet text about the hero (mana, refusals, pickups) starts, in metres.")]
        [SerializeField, Range(0f, 4f)] float overHead = 2.2f;

        [Tooltip("Spells cheaper than this don't say what they cost, so the M.G.R.S. firing nonstop doesn't flood the view.")]
        [SerializeField, Min(0f)] float minManaShown = 10f;

        PlayerAbilityCues _cues;
        PlayerCaster _caster;
        PlayerBlockBag _bag;
        PlayerHealth _health;

        /// <summary>Every PC for hits and mana; the owner's PC for refusals and pickups.</summary>
        public event Action<CombatTextEvent> Raised;

        void Awake()
        {
            _cues = GetComponent<PlayerAbilityCues>();
            _caster = GetComponent<PlayerCaster>();
            _bag = GetComponent<PlayerBlockBag>();
            _health = GetComponent<PlayerHealth>();
        }

        void OnEnable()
        {
            _cues.Received += OnCue;
            _caster.Refused += OnRefused;
            if (_bag != null)
            {
                _bag.PickedUp += OnPickedUp;
                _bag.Refused += OnBagRefused;
                _bag.Sent += OnSent;
            }
            if (_health != null)
                _health.Changed += OnHealth;
        }

        void OnDisable()
        {
            _cues.Received -= OnCue;
            _caster.Refused -= OnRefused;
            if (_bag != null)
            {
                _bag.PickedUp -= OnPickedUp;
                _bag.Refused -= OnBagRefused;
                _bag.Sent -= OnSent;
            }
            if (_health != null)
                _health.Changed -= OnHealth;
        }

        float3 Head => (float3)transform.position + new float3(0f, overHead, 0f);

        void OnCue(AbilityCue cue, AbilityDefinition ability)
        {
            switch (cue.Kind)
            {
                case AbilityCueKind.Impact:
                    // A hit on a Training Dummy (Roadmap 4.6) shows the damage it took, not block damage.
                    if (cue.TargetDamage > 0)
                        Raise(new CombatTextEvent
                        {
                            Kind = CombatTextKind.TargetHit, Amount = cue.TargetDamage, AbilityId = ability.Id,
                            Element = ability.Element, Position = (float3)cue.Position + new float3(0f, 0.6f, 0f),
                        });
                    bool crater = ability.AoeRadius > 0f && cue.BlocksBroken > 0;
                    if (!crater && cue.TargetDamage > 0)
                        break;
                    Raise(new CombatTextEvent
                    {
                        Kind = crater ? CombatTextKind.CraterHit : CombatTextKind.SpellHit,
                        Amount = crater ? cue.BlocksBroken : Mathf.RoundToInt(ability.BlockDamage),
                        AbilityId = ability.Id, Element = ability.Element, Position = cue.Position,
                    });
                    break;
                case AbilityCueKind.Released when ability.ManaCost > 0f && ability.ManaCost >= minManaShown:
                    Raise(new CombatTextEvent
                    {
                        Kind = CombatTextKind.ManaSpent, Amount = Mathf.RoundToInt(ability.ManaCost), AbilityId = ability.Id,
                        Element = ability.Element, Position = Head,
                    });
                    break;
            }
        }

        void OnHealth(HealthEvent e)
        {
            CombatTextKind kind;
            switch (e.Kind)
            {
                case HealthEventKind.Damaged:
                    kind = CombatTextKind.Hurt;
                    break;
                case HealthEventKind.Healed:
                    kind = CombatTextKind.Healed;
                    break;
                case HealthEventKind.Shielded:
                    kind = CombatTextKind.Shielded;
                    break;
                case HealthEventKind.Absorbed:
                    kind = CombatTextKind.Absorbed;
                    break;
                default:
                    return;
            }
            int amount = Mathf.RoundToInt(e.Amount);
            if (amount > 0)
                Raise(new CombatTextEvent { Kind = kind, Amount = amount, AbilityId = e.AbilityId, Position = Head });
        }

        void OnRefused(CastCheck reason) =>
            Raise(new CombatTextEvent { Kind = CombatTextKind.Refused, Reason = reason, Position = Head });

        void OnPickedUp(ushort itemId, int count) =>
            Raise(new CombatTextEvent { Kind = CombatTextKind.Pickup, ItemId = itemId, Amount = count, Position = Head });

        void OnBagRefused(BagRefusal reason) =>
            Raise(new CombatTextEvent { Kind = CombatTextKind.BagRefused, BagReason = reason, Position = Head });

        void OnSent(ushort itemId, int count) =>
            Raise(new CombatTextEvent { Kind = CombatTextKind.Sent, ItemId = itemId, Amount = count, Position = Head });

        void Raise(in CombatTextEvent e) => Raised?.Invoke(e);
    }
}
