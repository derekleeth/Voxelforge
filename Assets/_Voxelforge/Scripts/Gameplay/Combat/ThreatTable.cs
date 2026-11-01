using System.Collections.Generic;
using Unity.Mathematics;
using Voxelforge.Core;

namespace Voxelforge.Gameplay
{
    /// <summary>One hero on a <see cref="ThreatTable"/>: how much they've angered it.</summary>
    public struct ThreatEntry
    {
        public PlayerId Hero;
        public float Threat;
    }

    /// <summary>
    /// What a <see cref="ThreatTable"/> needs to know to pick a target: who's about and where, so it skips heroes out of
    /// reach and asks the right margin of each. With no <see cref="Heroes"/> (a Training Dummy), everyone on the table
    /// counts and everyone is at range.
    /// </summary>
    public struct ThreatPick
    {
        public float Now;
        public IReadOnlyList<HeroSense> Heroes;
        /// <summary>Where it stands, to tell who's in melee.</summary>
        public float3 Feet;
        /// <summary>Heroes this close count as in melee (<see cref="ThreatRules.MeleeMargin"/>).</summary>
        public float MeleeReach;
        /// <summary>Heroes further than <see cref="Leash"/> from here across the ground are out of reach. 0 = no leash.</summary>
        public float3 Home;
        public float Leash;
    }

    /// <summary>
    /// Who a mob is angriest at (Roadmap 5.3), <i>World of Warcraft</i> style: every hero who hurts it, heals its foes or
    /// stuns it builds threat, and it fights whoever has the most. It only changes its mind when someone passes its
    /// target by a clear margin (<see cref="ThreatRules"/>), so a tank holds it while the others cast, and a taunt
    /// (<see cref="Taunt"/>) snatches it back. Plain C#, so it's tested without a scene; <see cref="MobRules"/> and
    /// <see cref="SparringDummy"/> keep one each on the host. Heroes are kept by <see cref="PlayerId"/> (CLAUDE.md Law #1).
    /// </summary>
    public sealed class ThreatTable
    {
        readonly List<ThreatEntry> _entries = new List<ThreatEntry>(4);
        PlayerId _tauntedBy;
        float _tauntUntil = float.NegativeInfinity;

        public IReadOnlyList<ThreatEntry> Entries => _entries;

        public bool IsEmpty => _entries.Count == 0;

        /// <summary>Adds <paramref name="threat"/> to <paramref name="hero"/>, putting them on the table if they're new.</summary>
        public void Add(PlayerId hero, float threat)
        {
            if (!hero.IsValid || !(threat > 0f))
                return;
            int i = IndexOf(hero);
            if (i < 0)
                _entries.Add(new ThreatEntry { Hero = hero, Threat = threat });
            else
                _entries[i] = new ThreatEntry { Hero = hero, Threat = _entries[i].Threat + threat };
        }

        /// <summary>
        /// A taunt: <paramref name="hero"/> rises to the top of the table, and it fights them until
        /// <paramref name="now"/> + <paramref name="seconds"/> whatever anyone else does.
        /// </summary>
        public void Taunt(PlayerId hero, float now, float seconds)
        {
            if (!hero.IsValid)
                return;
            float top = 0f;
            foreach (ThreatEntry entry in _entries)
                top = math.max(top, entry.Threat);
            float raise = top - Of(hero);
            if (raise > 0f)
                Add(hero, raise);
            else if (!Contains(hero))
                Add(hero, ThreatRules.NoticeThreat);
            _tauntedBy = hero;
            _tauntUntil = now + math.max(0f, seconds);
        }

        /// <summary>Takes <paramref name="hero"/> off the table: knocked out, or gone.</summary>
        public void Remove(PlayerId hero)
        {
            int i = IndexOf(hero);
            if (i >= 0)
                _entries.RemoveAt(i);
            if (_tauntedBy == hero)
                _tauntedBy = PlayerId.None;
        }

        /// <summary>Forgets everyone (home from a leash).</summary>
        public void Clear()
        {
            _entries.Clear();
            _tauntedBy = PlayerId.None;
            _tauntUntil = float.NegativeInfinity;
        }

        public float Of(PlayerId hero)
        {
            int i = IndexOf(hero);
            return i < 0 ? 0f : _entries[i].Threat;
        }

        public bool Contains(PlayerId hero) => IndexOf(hero) >= 0;

        /// <summary>Who has taunted it, while the taunt lasts; else none.</summary>
        public PlayerId TauntedBy(float now) => now < _tauntUntil ? _tauntedBy : PlayerId.None;

        /// <summary>
        /// Who it should fight now, staying on <paramref name="current"/> unless a hero within reach has passed them by
        /// <see cref="ThreatRules.MeleeMargin"/> (in melee) or <see cref="ThreatRules.RangedMargin"/> (further away). A
        /// running taunt wins outright. None if nobody on the table is within reach.
        /// </summary>
        public PlayerId PickTarget(PlayerId current, in ThreatPick pick)
        {
            PlayerId taunter = TauntedBy(pick.Now);
            if (taunter.IsValid && InReach(taunter, pick, out _))
                return taunter;

            PlayerId best = PlayerId.None;
            float bestThreat = 0f;
            HeroSense bestSense = default;
            foreach (ThreatEntry entry in _entries)
            {
                if (entry.Threat <= bestThreat || !InReach(entry.Hero, pick, out HeroSense sense))
                    continue;
                best = entry.Hero;
                bestThreat = entry.Threat;
                bestSense = sense;
            }
            if (!best.IsValid || best == current || !current.IsValid || !InReach(current, pick, out _))
                return best;
            float currentThreat = Of(current);
            if (currentThreat <= 0f)
                return best;
            bool melee = pick.Heroes != null && math.distance(bestSense.Feet, pick.Feet) <= pick.MeleeReach;
            float margin = melee ? ThreatRules.MeleeMargin : ThreatRules.RangedMargin;
            return bestThreat > currentThreat * margin ? best : current;
        }

        /// <summary>On the table and about: present, awake, and (with a leash) near enough home.</summary>
        bool InReach(PlayerId hero, in ThreatPick pick, out HeroSense sense)
        {
            sense = default;
            if (!Contains(hero))
                return false;
            if (pick.Heroes == null)
                return true;
            foreach (HeroSense candidate in pick.Heroes)
            {
                if (candidate.Id != hero)
                    continue;
                sense = candidate;
                return !candidate.KnockedOut &&
                       (pick.Leash <= 0f || math.distance(candidate.Feet.xz, pick.Home.xz) <= pick.Leash);
            }
            return false;
        }

        int IndexOf(PlayerId hero)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Hero == hero)
                    return i;
            }
            return -1;
        }
    }
}
