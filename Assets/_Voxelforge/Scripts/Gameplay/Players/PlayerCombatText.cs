using System;
using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Turns what the host said about this hero into <see cref="CombatTextEvent"/>s for the floating combat text in their
    /// own view (Roadmap 4.4): spell hits and craters from <see cref="AbilityCue"/>s, mana spent when a spell goes off,
    /// refused casts and items picked up. It only listens, so it can't change the game (Roadmap 4.2).
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

        /// <summary>Every PC for hits and mana; the owner's PC for refusals and pickups.</summary>
        public event Action<CombatTextEvent> Raised;

        void Awake()
        {
            _cues = GetComponent<PlayerAbilityCues>();
            _caster = GetComponent<PlayerCaster>();
            _bag = GetComponent<PlayerBlockBag>();
        }

        void OnEnable()
        {
            _cues.Received += OnCue;
            _caster.Refused += OnRefused;
            if (_bag != null)
            {
                _bag.PickedUp += OnPickedUp;
                _bag.Refused += OnBagRefused;
            }
        }

        void OnDisable()
        {
            _cues.Received -= OnCue;
            _caster.Refused -= OnRefused;
            if (_bag != null)
            {
                _bag.PickedUp -= OnPickedUp;
                _bag.Refused -= OnBagRefused;
            }
        }

        float3 Head => (float3)transform.position + new float3(0f, overHead, 0f);

        void OnCue(AbilityCue cue, AbilityDefinition ability)
        {
            switch (cue.Kind)
            {
                case AbilityCueKind.Impact:
                    bool crater = ability.AoeRadius > 0f && cue.BlocksBroken > 0;
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

        void OnRefused(CastCheck reason) =>
            Raise(new CombatTextEvent { Kind = CombatTextKind.Refused, Reason = reason, Position = Head });

        void OnPickedUp(ushort itemId, int count) =>
            Raise(new CombatTextEvent { Kind = CombatTextKind.Pickup, ItemId = itemId, Amount = count, Position = Head });

        void OnBagRefused(BagRefusal reason) =>
            Raise(new CombatTextEvent { Kind = CombatTextKind.BagRefused, BagReason = reason, Position = Head });

        void Raise(in CombatTextEvent e) => Raised?.Invoke(e);
    }
}
