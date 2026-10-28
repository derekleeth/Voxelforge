using UnityEngine;
using UnityEngine.InputSystem;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Puts a <see cref="LocalPlayerRoster"/> in the scene and connects it to this PC's spawned players: when the host
    /// spawns a player we own, it gets the controls of its local slot.
    /// <b>Not a singleton:</b> other systems get a reference to this component (CLAUDE.md Law #1).
    /// </summary>
    public sealed class LocalPlayerManager : MonoBehaviour
    {
        [SerializeField] InputActionAsset actions;

        [Tooltip("Asks the host to spawn a player when someone joins.")]
        [SerializeField] PlayerJoinService joinService;

        [Tooltip("How many players can share this PC.")]
        [SerializeField, Range(1, 4)] int maxLocalPlayers = 4;

        [Tooltip("Dev mode: two players on one keyboard. Space joins with WASD, Enter joins with the arrow keys + Right Ctrl.")]
        [SerializeField] bool allowSplitKeyboard;

        readonly PlayerController[] _controllers = new PlayerController[4];
        LocalPlayerRoster _roster;

        /// <summary>Null until this component is enabled.</summary>
        public LocalPlayerRoster Roster => _roster;

        /// <summary>
        /// The player spawned for <paramref name="slot"/> on this PC, and that slot's controls.
        /// False if nobody has joined that slot or their hero hasn't spawned yet.
        /// </summary>
        public bool TryGetLocalPlayer(int slot, out PlayerController controller, out LocalPlayerInput input)
        {
            controller = slot >= 0 && slot < _controllers.Length ? _controllers[slot] : null;
            input = controller != null && _roster != null && slot < _roster.MaxSlots ? _roster.GetInput(slot) : null;
            if (input == null)
                controller = null;
            return controller != null;
        }

        void OnEnable()
        {
            if (actions == null || joinService == null)
            {
                Debug.LogError($"{nameof(LocalPlayerManager)} needs the input actions and a join service.", this);
                enabled = false;
                return;
            }

            _roster = new LocalPlayerRoster(actions, maxLocalPlayers, joinService) { AllowSplitKeyboard = allowSplitKeyboard };
            PlayerController.OwnerSpawned += HandleOwnerSpawned;
            PlayerController.OwnerDespawned += HandleOwnerDespawned;
        }

        void OnDisable()
        {
            PlayerController.OwnerSpawned -= HandleOwnerSpawned;
            PlayerController.OwnerDespawned -= HandleOwnerDespawned;
            _roster?.Dispose();
            _roster = null;
        }

        void OnValidate()
        {
            if (_roster != null)
                _roster.AllowSplitKeyboard = allowSplitKeyboard;
        }

        void HandleOwnerSpawned(PlayerController controller)
        {
            int slot = controller.LocalSlot;
            if (slot >= _roster.MaxSlots || _roster.GetInput(slot) == null)
            {
                Debug.LogWarning($"[Voxelforge] {controller.PlayerId} spawned in local slot {slot}, but nobody joined that slot here.", controller);
                return;
            }

            _controllers[slot] = controller;
            controller.SetInputSource(_roster.GetInput(slot));
            _roster.MarkSpawned(slot);
        }

        void HandleOwnerDespawned(PlayerController controller)
        {
            int slot = controller.LocalSlot;
            if (slot < _controllers.Length && _controllers[slot] == controller)
                _controllers[slot] = null;
        }
    }
}
