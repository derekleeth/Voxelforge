using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// A play-test key for levels (Roadmap 5.6), host only. <b>F1</b> takes every hero up a level (with the full
    /// level-up: refill, fanfare, pillar of light); <b>Shift+F1</b> puts every hero back to level 1.
    /// </summary>
    public sealed class LevelDebugKeys : MonoBehaviour
    {
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
            foreach (PlayerController hero in players.Players)
            {
                if (hero != null && hero.TryGetComponent(out PlayerLevel level))
                    level.ServerSetLevel(reset ? 1 : level.Level + 1);
            }
        }
    }
}
