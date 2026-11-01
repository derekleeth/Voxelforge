using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Core;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// XP for defeated mobs (Roadmap 5.6, D-037), <b>host only</b> (CLAUDE.md Law #2). When a mob falls, every hero
    /// within the <see cref="LevelBook.ShareRadius"/> gets its full XP, fighting or not, so couch partners level together.
    /// Bosses (5.10) are just mobs with a bigger number. <b>Not a singleton:</b> it's handed its references (Law #1).
    /// </summary>
    public sealed class ExperienceAwards : MonoBehaviour
    {
        [SerializeField] NetworkManager networkManager;

        [SerializeField] PlayerRegistry players;

        [Tooltip("Data/Levels/LevelBook.asset: what each mob is worth and how far XP is shared.")]
        [SerializeField] LevelBook book;

        readonly List<HeroAt> _heroes = new List<HeroAt>();
        readonly List<PlayerId> _sharers = new List<PlayerId>();

        bool IsServer => networkManager == null || networkManager.IsServer || !networkManager.IsListening;

        void OnEnable() => Mob.ServerDefeated += OnMobDefeated;

        void OnDisable() => Mob.ServerDefeated -= OnMobDefeated;

        void OnMobDefeated(Mob mob, PlayerId killer)
        {
            if (!IsServer || mob == null || mob.Definition == null || book == null || players == null ||
                !book.TryGetMobXp(mob.Definition.Id, out int xp) || xp <= 0)
                return;
            _heroes.Clear();
            foreach (PlayerController player in players.Players)
            {
                if (player != null)
                    _heroes.Add(new HeroAt(player.PlayerId, player.transform.position));
            }
            LevelRules.Sharers(mob.Feet, _heroes, book.ShareRadius, _sharers);
            foreach (PlayerController player in players.Players)
            {
                if (player != null && _sharers.Contains(player.PlayerId) && player.TryGetComponent(out PlayerLevel level))
                    level.ServerGrant(xp);
            }
        }
    }
}
