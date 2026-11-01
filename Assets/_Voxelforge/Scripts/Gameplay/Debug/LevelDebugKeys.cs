using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Play-test keys for levels (Roadmap 5.6) and professions (5.7), host only. <b>F1</b> takes every hero up a level
    /// (with the full level-up: refill, fanfare, pillar of light); <b>Shift+F1</b> puts every hero back to level 1.
    /// <b>Ctrl+F1</b> gives every hero 25 more skill in each profession; <b>Ctrl+Shift+F1</b> puts them all back to 1.
    /// </summary>
    public sealed class LevelDebugKeys : MonoBehaviour
    {
        const int ProfessionStep = 25;

        [SerializeField] NetworkManager networkManager;

        [SerializeField] PlayerRegistry players;

        bool IsServer => networkManager != null && networkManager.IsServer;

        void Update()
        {
            if (!IsServer || players == null)
                return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.f1Key.wasPressedThisFrame)
                return;
            bool reset = keyboard.shiftKey.isPressed;
            bool professions = keyboard.ctrlKey.isPressed;
            foreach (PlayerController hero in players.Players)
            {
                if (hero == null)
                    continue;
                if (professions)
                {
                    if (!hero.TryGetComponent(out PlayerProfessions skills))
                        continue;
                    if (reset)
                        skills.ServerReset();
                    else
                        skills.ServerAddToAll(ProfessionStep);
                    Debug.Log($"[Voxelforge] Ctrl+F1: {hero.name} now has {skills.Skills}.", this);
                }
                else if (hero.TryGetComponent(out PlayerLevel level))
                {
                    level.ServerSetLevel(reset ? 1 : level.Level + 1);
                }
            }
        }
    }
}
