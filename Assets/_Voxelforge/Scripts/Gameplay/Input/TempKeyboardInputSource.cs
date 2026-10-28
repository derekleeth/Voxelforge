using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// <b>Temporary (Roadmap 2.1):</b> both players share one keyboard. Player 1 uses WASD + Space,
    /// Player 2 uses the arrow keys + Right Ctrl. Roadmap 2.2 replaces this with real per-device binding.
    /// </summary>
    public sealed class TempKeyboardInputSource : MonoBehaviour, IPlayerInputSource
    {
        InputAction _move;
        InputAction _jump;
        byte _boundSlot = byte.MaxValue;

        public PlayerInputFrame Read(byte localSlot)
        {
            if (localSlot != _boundSlot)
                Bind(localSlot);

            return new PlayerInputFrame
            {
                Move = (float2)_move.ReadValue<Vector2>(),
                Buttons = _jump.IsPressed() ? PlayerButtons.Jump : PlayerButtons.None,
            };
        }

        void Bind(byte localSlot)
        {
            DisposeActions();
            bool playerOne = localSlot == 0;

            _move = new InputAction($"Move (slot {localSlot})", InputActionType.Value);
            _move.AddCompositeBinding("2DVector")
                .With("Up", playerOne ? "<Keyboard>/w" : "<Keyboard>/upArrow")
                .With("Down", playerOne ? "<Keyboard>/s" : "<Keyboard>/downArrow")
                .With("Left", playerOne ? "<Keyboard>/a" : "<Keyboard>/leftArrow")
                .With("Right", playerOne ? "<Keyboard>/d" : "<Keyboard>/rightArrow");
            _jump = new InputAction($"Jump (slot {localSlot})", InputActionType.Button,
                playerOne ? "<Keyboard>/space" : "<Keyboard>/rightCtrl");

            _move.Enable();
            _jump.Enable();
            _boundSlot = localSlot;
        }

        void DisposeActions()
        {
            _move?.Dispose();
            _jump?.Dispose();
            _move = null;
            _jump = null;
            _boundSlot = byte.MaxValue;
        }

        void OnDestroy() => DisposeActions();
    }
}
