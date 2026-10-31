using UnityEditor;
using UnityEngine;
using Voxelforge.Gameplay;

namespace Voxelforge.Editor
{
    /// <summary>
    /// <i>Voxelforge → Debug → Reset Race Choice</i> (Roadmap 4.7): in Play, on the host, lets every hero choose their race
    /// again, so both races can be tried without a fresh world. Race is otherwise chosen once.
    /// </summary>
    public static class RaceDebugMenu
    {
        const string MenuPath = "Voxelforge/Debug/Reset Race Choice (all players)";

        [MenuItem(MenuPath)]
        static void ResetRaceChoice()
        {
            int reset = RaceDebug.ServerResetAllChoices();
            Debug.Log($"[Voxelforge] {reset} hero(es) may choose their race again.");
        }

        [MenuItem(MenuPath, true)]
        static bool CanReset() => Application.isPlaying;
    }
}
