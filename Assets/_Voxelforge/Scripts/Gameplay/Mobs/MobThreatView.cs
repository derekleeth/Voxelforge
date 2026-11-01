using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using Voxelforge.Core;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// What every PC may know about a mob's threat (Roadmap 5.3): who it's fighting, and for up to <see cref="Slots"/>
    /// heroes how close they are to pulling it off them, so each player's own view can mark the mobs after them and warn
    /// them before they pull one (<see cref="ThreatMarksView"/>, <see cref="AggroWarnings"/>). The host works it out from
    /// its <see cref="ThreatTable"/> (<see cref="From"/>) and <see cref="Mob"/> shares it; the table itself never leaves
    /// the host. Heroes are by <see cref="PlayerId"/> (CLAUDE.md Law #1). Five bytes plus four pairs on the wire.
    /// </summary>
    public struct MobThreatView : INetworkSerializable, IEquatable<MobThreatView>
    {
        public const int Slots = 4;

        /// <summary>A share of this means "you have it, or you'd pull it now".</summary>
        public const byte Pull = 255;

        /// <summary>The <see cref="PlayerId.Value"/> of the hero it's fighting, or 0.</summary>
        public byte Aggro;

        // Up to four heroes on its table, the angriest first, and their shares: their threat over what it takes to pull it
        // off its target (the target's threat times ThreatRules' melee or ranged margin), 0-255.
        byte _hero0, _hero1, _hero2, _hero3;
        byte _share0, _share1, _share2, _share3;

        public PlayerId AggroHero => new PlayerId(Aggro);

        /// <summary>
        /// How close <paramref name="hero"/> is to pulling it, 0 (not on its table) to <see cref="Pull"/>. The hero it's
        /// fighting is always <see cref="Pull"/>.
        /// </summary>
        public byte ShareOf(PlayerId hero)
        {
            if (!hero.IsValid)
                return 0;
            if (hero.Value == Aggro)
                return Pull;
            for (int i = 0; i < Slots; i++)
            {
                if (HeroAt(i) == hero.Value)
                    return ShareAt(i);
            }
            return 0;
        }

        /// <summary>
        /// Host: the view of <paramref name="table"/> for a mob fighting <paramref name="target"/> (none if it isn't
        /// fighting). <paramref name="pick"/> says where everyone stands, for the melee or ranged margin.
        /// </summary>
        public static MobThreatView From(ThreatTable table, PlayerId target, in ThreatPick pick)
        {
            var view = new MobThreatView();
            if (table == null || !target.IsValid)
                return view;
            view.Aggro = target.Value;
            float targetThreat = table.Of(target);
            IReadOnlyList<ThreatEntry> entries = table.Entries;
            // The angriest few, without sorting the list: pick the biggest not yet taken, Slots times. Tables are tiny.
            int taken = 0;
            for (int slot = 0; slot < Slots; slot++)
            {
                int best = -1;
                for (int i = 0; i < entries.Count; i++)
                {
                    if ((taken & (1 << i)) != 0 || i >= 31)
                        continue;
                    if (best < 0 || entries[i].Threat > entries[best].Threat)
                        best = i;
                }
                if (best < 0)
                    break;
                taken |= 1 << best;
                ThreatEntry entry = entries[best];
                byte share = entry.Hero == target ? Pull : Share(entry, targetThreat, pick);
                view.Set(slot, entry.Hero.Value, share);
            }
            return view;
        }

        static byte Share(in ThreatEntry entry, float targetThreat, in ThreatPick pick)
        {
            float margin = InMelee(entry.Hero, pick) ? ThreatRules.MeleeMargin : ThreatRules.RangedMargin;
            float pullAt = targetThreat * margin;
            if (pullAt <= 0f)
                return Pull;
            return (byte)math.clamp((int)math.round(entry.Threat / pullAt * Pull), 0, Pull);
        }

        static bool InMelee(PlayerId hero, in ThreatPick pick)
        {
            if (pick.Heroes == null)
                return false;
            foreach (HeroSense sense in pick.Heroes)
            {
                if (sense.Id == hero)
                    return math.distance(sense.Feet, pick.Feet) <= pick.MeleeReach;
            }
            return false;
        }

        /// <summary>
        /// Host: worth sending again: someone else has aggro, a hero came or went, or a share moved by more than
        /// <paramref name="step"/>.
        /// </summary>
        public bool Differs(in MobThreatView other, int step)
        {
            if (Aggro != other.Aggro)
                return true;
            for (int i = 0; i < Slots; i++)
            {
                if (HeroAt(i) != other.HeroAt(i) || math.abs(ShareAt(i) - other.ShareAt(i)) > step)
                    return true;
            }
            return false;
        }

        byte HeroAt(int slot) => slot switch { 0 => _hero0, 1 => _hero1, 2 => _hero2, _ => _hero3 };

        byte ShareAt(int slot) => slot switch { 0 => _share0, 1 => _share1, 2 => _share2, _ => _share3 };

        void Set(int slot, byte hero, byte share)
        {
            switch (slot)
            {
                case 0: _hero0 = hero; _share0 = share; break;
                case 1: _hero1 = hero; _share1 = share; break;
                case 2: _hero2 = hero; _share2 = share; break;
                default: _hero3 = hero; _share3 = share; break;
            }
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Aggro);
            serializer.SerializeValue(ref _hero0);
            serializer.SerializeValue(ref _share0);
            serializer.SerializeValue(ref _hero1);
            serializer.SerializeValue(ref _share1);
            serializer.SerializeValue(ref _hero2);
            serializer.SerializeValue(ref _share2);
            serializer.SerializeValue(ref _hero3);
            serializer.SerializeValue(ref _share3);
        }

        public bool Equals(MobThreatView other) => !Differs(other, 0);
        public override bool Equals(object obj) => obj is MobThreatView other && Equals(other);
        public override int GetHashCode() =>
            HashCode.Combine(Aggro, _hero0, _share0, _hero1, _share1, HashCode.Combine(_hero2, _share2, _hero3, _share3));
    }

    /// <summary>
    /// When to warn a player they're about to pull a mob off whoever is tanking it (Roadmap 5.3): their share of it
    /// passes <see cref="WarnShare"/>, at most once every <see cref="EverySeconds"/> per mob. Plain C#, so it's tested;
    /// <see cref="PlayerCombatText"/> keeps one per hero and says "PULLING AGGRO!" in their own view.
    /// </summary>
    public sealed class AggroWarnings
    {
        public const float WarnShare = 0.9f;
        public const float EverySeconds = 3f;

        readonly Dictionary<ulong, float> _warnedAt = new Dictionary<ulong, float>();
        readonly List<ulong> _stale = new List<ulong>();

        /// <summary>True if <paramref name="me"/> should be warned about mob <paramref name="mobId"/> now.</summary>
        public bool ShouldWarn(ulong mobId, in MobThreatView view, PlayerId me, float now)
        {
            if (!me.IsValid || view.Aggro == 0 || view.Aggro == me.Value ||
                view.ShareOf(me) < WarnShare * MobThreatView.Pull)
                return false;
            if (_warnedAt.TryGetValue(mobId, out float at) && now - at < EverySeconds)
                return false;
            _warnedAt[mobId] = now;
            return true;
        }

        /// <summary>Forgets mobs not warned about for a while, so the list doesn't grow as mobs come and go.</summary>
        public void Prune(float now)
        {
            _stale.Clear();
            foreach (KeyValuePair<ulong, float> pair in _warnedAt)
            {
                if (now - pair.Value >= EverySeconds)
                    _stale.Add(pair.Key);
            }
            foreach (ulong id in _stale)
                _warnedAt.Remove(id);
        }
    }
}
