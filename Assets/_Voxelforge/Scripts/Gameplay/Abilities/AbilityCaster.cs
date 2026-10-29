using System.Collections.Generic;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The rules for one hero's spells (Roadmap 4.1): their mana, their cooldowns and the cast they're in the middle of.
    /// Plain C#, so it's tested without a scene; <see cref="PlayerCaster"/> runs one per player on the host.
    /// <para>Like <i>WoW</i>, mana is spent and the cooldown starts when a cast <b>finishes</b>, and both are checked
    /// again then. Instant abilities finish the moment they start.</para>
    /// </summary>
    public sealed class AbilityCaster
    {
        readonly Dictionary<ushort, float> _readyAt = new Dictionary<ushort, float>();

        public AbilityCaster(float maxMana, float regen)
        {
            MaxMana = Mathf.Max(0f, maxMana);
            Mana = MaxMana;
            Regen = Mathf.Max(0f, regen);
        }

        public float Mana { get; private set; }
        public float MaxMana { get; private set; }

        /// <summary>Mana back per second, always (also while casting).</summary>
        public float Regen { get; set; }

        /// <summary>The ability being cast, or null.</summary>
        public AbilityDefinition Casting { get; private set; }

        /// <summary>When <see cref="Casting"/> started.</summary>
        public float CastStart { get; private set; }

        public bool IsCasting => Casting != null;

        /// <summary>Changes the pool (a class or race, 4.6/4.7). Mana stays the same, unless it's now over the top.</summary>
        public void SetMaxMana(float maxMana)
        {
            MaxMana = Mathf.Max(0f, maxMana);
            Mana = Mathf.Min(Mana, MaxMana);
        }

        public void Refill() => Mana = MaxMana;

        /// <summary>Seconds until <paramref name="abilityId"/> can be cast again; 0 if it's ready.</summary>
        public float CooldownRemaining(ushort abilityId, float now) =>
            _readyAt.TryGetValue(abilityId, out float readyAt) ? Mathf.Max(0f, readyAt - now) : 0f;

        /// <summary>Whether <paramref name="ability"/> could start now.</summary>
        public CastCheck Check(AbilityDefinition ability, float now)
        {
            if (ability == null)
                return CastCheck.UnknownAbility;
            if (IsCasting)
                return CastCheck.Busy;
            if (CooldownRemaining(ability.Id, now) > 0f)
                return CastCheck.OnCooldown;
            if (Mana < ability.ManaCost)
                return CastCheck.NotEnoughMana;
            return CastCheck.Ok;
        }

        /// <summary>
        /// Starts casting <paramref name="ability"/> if <see cref="Check"/> allows it. An instant one finishes at once and
        /// is added to <paramref name="finished"/>; one with a cast time finishes in a later <see cref="Tick"/>.
        /// </summary>
        public CastCheck TryBegin(AbilityDefinition ability, float now, List<AbilityDefinition> finished)
        {
            CastCheck check = Check(ability, now);
            if (check != CastCheck.Ok)
                return check;
            if (ability.IsInstant)
            {
                Finish(ability, now, finished);
                return CastCheck.Ok;
            }
            Casting = ability;
            CastStart = now;
            return CastCheck.Ok;
        }

        /// <summary>
        /// Regenerates mana over <paramref name="dt"/> seconds and moves the current cast on. Returns true if the cast
        /// ended this tick, with <paramref name="result"/> <see cref="CastCheck.Ok"/> if it went off (and was added to
        /// <paramref name="finished"/>), else why it didn't.
        /// </summary>
        /// <param name="moving">The hero moved or jumped since the last tick.</param>
        public bool Tick(float now, float dt, bool moving, List<AbilityDefinition> finished, out CastCheck result)
        {
            Mana = Mathf.Min(MaxMana, Mana + Regen * Mathf.Max(0f, dt));
            result = CastCheck.Ok;
            if (!IsCasting)
                return false;

            AbilityDefinition ability = Casting;
            if (moving && !ability.CastWhileMoving)
            {
                Cancel();
                result = CastCheck.Interrupted;
                return true;
            }
            if (now - CastStart < ability.CastTime)
                return false;

            Casting = null;
            if (Mana < ability.ManaCost)
            {
                result = CastCheck.NotEnoughMana;
                return true;
            }
            Finish(ability, now, finished);
            return true;
        }

        /// <summary>Stops the current cast without spending anything.</summary>
        public void Cancel() => Casting = null;

        void Finish(AbilityDefinition ability, float now, List<AbilityDefinition> finished)
        {
            Mana -= ability.ManaCost;
            if (ability.Cooldown > 0f)
                _readyAt[ability.Id] = now + ability.Cooldown;
            finished?.Add(ability);
        }
    }
}
