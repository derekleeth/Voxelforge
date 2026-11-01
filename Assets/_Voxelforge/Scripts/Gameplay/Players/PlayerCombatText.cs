using System;
using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Core;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Turns what the host said about this hero into <see cref="CombatTextEvent"/>s for the floating combat text in their
    /// own view (Roadmap 4.4): spell hits and craters from <see cref="AbilityCue"/>s, mana spent when a spell goes off,
    /// refused casts and items picked up, and damage, heals and shields from <see cref="PlayerHealth"/> (4.6). It only listens, so it can't change the game (Roadmap 4.2).
    /// On the owner's PC it also watches the mobs' shared threat (Roadmap 5.3) and warns when the hero is about to pull one
    /// off whoever is tanking it.
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(PlayerAbilityCues), typeof(PlayerCaster))]
    public sealed class PlayerCombatText : MonoBehaviour
    {
        [Tooltip("How far above the feet text about the hero (mana, refusals, pickups) starts, in metres.")]
        [SerializeField, Range(0f, 4f)] float overHead = 2.2f;

        [Tooltip("Spells cheaper than this don't say what they cost, so the M.G.R.S. firing nonstop doesn't flood the view.")]
        [SerializeField, Min(0f)] float minManaShown = 10f;

        // How often the owner's PC looks over the mobs for a "PULLING AGGRO!" warning (Roadmap 5.3), in seconds.
        const float AggroCheckEvery = 0.25f;

        readonly AggroWarnings _aggroWarnings = new AggroWarnings();
        float _nextAggroCheck;
        PlayerController _player;
        PlayerAbilityCues _cues;
        PlayerCaster _caster;
        PlayerBlockBag _bag;
        PlayerHealth _health;
        PlayerExplorer _explorer;

        /// <summary>Every PC for hits and mana; the owner's PC for refusals and pickups.</summary>
        public event Action<CombatTextEvent> Raised;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _cues = GetComponent<PlayerAbilityCues>();
            _caster = GetComponent<PlayerCaster>();
            _bag = GetComponent<PlayerBlockBag>();
            _health = GetComponent<PlayerHealth>();
            _explorer = GetComponent<PlayerExplorer>();
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
            if (_explorer != null)
                _explorer.Discovered += OnDiscovered;
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
            if (_explorer != null)
                _explorer.Discovered -= OnDiscovered;
        }

        float3 Head => (float3)transform.position + new float3(0f, overHead, 0f);

        void OnCue(AbilityCue cue, AbilityDefinition ability)
        {
            switch (cue.Kind)
            {
                case AbilityCueKind.TargetHit:
                    Raise(new CombatTextEvent
                    {
                        Kind = !cue.Toppled ? CombatTextKind.TargetHit
                            : cue.Target.IsCreature ? CombatTextKind.Defeated : CombatTextKind.Toppled,
                        Amount = cue.TargetDamage,
                        AbilityId = ability.Id, Element = ability.Element, Position = (float3)cue.Position + new float3(0f, 0.6f, 0f),
                    });
                    break;
                case AbilityCueKind.TargetStunned:
                case AbilityCueKind.TargetTaunted:
                    Raise(new CombatTextEvent
                    {
                        Kind = cue.Kind == AbilityCueKind.TargetTaunted ? CombatTextKind.Taunted : CombatTextKind.Stunned,
                        AbilityId = ability.Id, Element = ability.Element,
                        Position = (float3)cue.Position + new float3(0f, 1.1f, 0f),
                    });
                    break;
                case AbilityCueKind.Impact:
                    // A hit on a Training Dummy (Roadmap 4.6) or a creature (5.2) shows its damage with the TargetHit
                    // cue, not block damage.
                    if (cue.OnTarget || cue.BlockId == BlockIds.TrainingDummy)
                        break;
                    // A stomp (Roadmap 4.7) doesn't hurt the ground; its stuns and hits say what it did.
                    if (ability.Delivery == AbilityDelivery.Quake)
                        break;
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

        void Update()
        {
            float now = Time.time;
            if (now < _nextAggroCheck || !_player.IsSpawned || !_player.IsOwner)
                return;
            _nextAggroCheck = now + AggroCheckEvery;
            CombatTargets targets = _player.Registry != null ? _player.Registry.Targets : null;
            if (targets == null)
                return;
            PlayerId me = _player.PlayerId;
            foreach (ICombatCreature creature in targets.Creatures)
            {
                if (!creature.IsTargetable || !_aggroWarnings.ShouldWarn(creature.CreatureId, creature.ThreatView, me, now))
                    continue;
                Raise(new CombatTextEvent { Kind = CombatTextKind.PullingAggro, Position = Head });
                // One warning at a time is plenty, even with a whole pack about to turn.
                break;
            }
            _aggroWarnings.Prune(now);
        }

        /// <summary>Roadmap 5.4: the host says this hero found a ruin or arena.</summary>
        void OnDiscovered(StampId stamp, int2 centre)
        {
            StampLibrary library = _player.WorldManager != null ? _player.WorldManager.StampLibrary : null;
            string name = library != null && library.TryGet(stamp, out StampDefinition definition) ? definition.DisplayName : "Ruins";
            Raise(new CombatTextEvent { Kind = CombatTextKind.Discovered, Label = name, Position = Head });
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
