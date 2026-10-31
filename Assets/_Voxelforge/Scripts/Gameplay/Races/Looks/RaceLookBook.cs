using System.Collections.Generic;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>Every <see cref="RaceLook"/> (Roadmap 4.7), found by race.</summary>
    [CreateAssetMenu(menuName = "Voxelforge/Race Look Book", fileName = "RaceLookBook")]
    public sealed class RaceLookBook : ScriptableObject
    {
        [SerializeField] List<RaceLook> looks = new List<RaceLook>();

        public IReadOnlyList<RaceLook> Looks => looks;

        public bool TryGet(RaceId race, out RaceLook look)
        {
            foreach (RaceLook candidate in looks)
            {
                if (candidate != null && candidate.Race == race)
                {
                    look = candidate;
                    return true;
                }
            }
            look = null;
            return false;
        }
    }
}
