using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Users;

namespace Voxelforge.Gameplay
{
    /// <summary>Asks the host for a new player in one of this PC's slots.</summary>
    public interface ILocalJoinRequester
    {
        void RequestJoin(byte localSlot);
    }

    /// <summary>
    /// Press-to-join for the players sharing this PC (Roadmap 2.2, DECISIONS.md D-008). Pressing Start or A on a
    /// gamepad, or Enter or Space on the keyboard, claims the next slot for that device, and the roster asks the host
    /// to spawn a player for it. Keyboard and mouse always join together.
    /// <para>
    /// An unplugged device puts its player in <see cref="LocalSlotState.Disconnected"/> until it comes back, or
    /// until someone presses Join on another unclaimed device, which takes the slot over.
    /// </para>
    /// <para>
    /// Plain C# (the scene uses it through <see cref="LocalPlayerManager"/>) so tests can drive it with virtual devices.
    /// </para>
    /// </summary>
    public sealed class LocalPlayerRoster : IDisposable
    {
        public const string KeyboardMouseScheme = "KeyboardMouse";
        public const string GamepadScheme = "Gamepad";
        public const string KeyboardLeftScheme = "KeyboardLeft";
        public const string KeyboardRightScheme = "KeyboardRight";

        readonly InputActionAsset _template;
        readonly ILocalJoinRequester _joinRequester;
        readonly LocalSlotAllocator _slots;
        readonly LocalPlayerInput[] _inputs;
        readonly InputAction _join;
        readonly List<InputDevice> _devices = new List<InputDevice>(2);

        public LocalPlayerRoster(InputActionAsset template, int maxSlots, ILocalJoinRequester joinRequester)
        {
            _template = template;
            _joinRequester = joinRequester;
            _slots = new LocalSlotAllocator(maxSlots);
            _inputs = new LocalPlayerInput[maxSlots];

            // Pass-through so every press reports which device it came from, even when two are held at once.
            _join = new InputAction("Join", InputActionType.PassThrough);
            _join.AddBinding("<Gamepad>/start");
            _join.AddBinding("<Gamepad>/buttonSouth");
            _join.AddBinding("<Keyboard>/enter");
            _join.AddBinding("<Keyboard>/space");
            _join.performed += OnJoinPressed;
            _join.Enable();

            InputUser.onChange += OnUserChange;
        }

        /// <summary>Dev mode: two players share the keyboard (Space joins WASD, Enter joins the arrow keys).</summary>
        public bool AllowSplitKeyboard { get; set; }

        public int MaxSlots => _slots.MaxSlots;

        /// <summary>Raised when a device takes a slot, including when it takes over a disconnected one.</summary>
        public event Action<byte> SlotJoined;

        public LocalSlotState GetState(int slot) => _slots[slot];

        /// <summary>The controls for a slot, or null if nobody has joined it yet.</summary>
        public LocalPlayerInput GetInput(int slot) => _inputs[slot];

        /// <summary>The host spawned this slot's player.</summary>
        public void MarkSpawned(int slot)
        {
            if (_slots[slot] == LocalSlotState.Joining)
                _slots[slot] = LocalSlotState.Active;
        }

        void OnJoinPressed(InputAction.CallbackContext context)
        {
            if (context.control is ButtonControl button && button.isPressed)
                TryJoin(button);
        }

        /// <summary>Handles a Join press from <paramref name="control"/>. Returns the slot it joined, or -1.</summary>
        public int TryJoin(InputControl control)
        {
            if (!TryChooseDevices(control, out string scheme))
                return -1;

            int slot = _slots.ChooseSlotForNewDevice();
            if (slot < 0)
                return -1;

            bool takingOver = _slots[slot] == LocalSlotState.Disconnected;
            _inputs[slot] ??= new LocalPlayerInput((byte)slot, _template);
            _inputs[slot].Pair(scheme, _devices);
            _slots[slot] = takingOver ? LocalSlotState.Active : LocalSlotState.Joining;
            Debug.Log($"[Voxelforge] {_devices[0].displayName} {(takingOver ? "took over" : "joined")} local slot {slot} ({scheme}).");

            if (!takingOver)
                _joinRequester.RequestJoin((byte)slot);
            SlotJoined?.Invoke((byte)slot);
            return slot;
        }

        /// <summary>Works out which devices and control scheme a Join press stands for, if it's a new player.</summary>
        bool TryChooseDevices(InputControl control, out string scheme)
        {
            _devices.Clear();
            scheme = null;
            InputDevice device = control.device;

            if (device is Gamepad)
            {
                if (IsOwnedByAnySlot(device))
                    return false;
                scheme = GamepadScheme;
                _devices.Add(device);
                return true;
            }

            if (device is Keyboard keyboard)
            {
                if (AllowSplitKeyboard)
                {
                    scheme = control == keyboard.enterKey ? KeyboardRightScheme : KeyboardLeftScheme;
                    if (IsSchemeInUse(scheme))
                        return false;
                    _devices.Add(keyboard);
                    return true;
                }

                if (IsOwnedByAnySlot(keyboard))
                    return false;
                scheme = KeyboardMouseScheme;
                _devices.Add(keyboard);
                if (Mouse.current != null && !IsOwnedByAnySlot(Mouse.current))
                    _devices.Add(Mouse.current);
                return true;
            }

            return false;
        }

        bool IsOwnedByAnySlot(InputDevice device)
        {
            foreach (LocalPlayerInput input in _inputs)
            {
                if (input != null && input.Owns(device))
                    return true;
            }
            return false;
        }

        bool IsSchemeInUse(string scheme)
        {
            for (int slot = 0; slot < _inputs.Length; slot++)
            {
                if (_inputs[slot] != null && _slots[slot] != LocalSlotState.Disconnected && _inputs[slot].ControlScheme == scheme)
                    return true;
            }
            return false;
        }

        void OnUserChange(InputUser user, InputUserChange change, InputDevice device)
        {
            if (change != InputUserChange.DeviceLost && change != InputUserChange.DeviceRegained)
                return;

            for (int slot = 0; slot < _inputs.Length; slot++)
            {
                LocalPlayerInput input = _inputs[slot];
                if (input == null || input.User != user)
                    continue;

                bool lost = change == InputUserChange.DeviceLost;
                input.IsDisconnected = lost;
                if (lost)
                    _slots[slot] = LocalSlotState.Disconnected;
                else if (_slots[slot] == LocalSlotState.Disconnected)
                    _slots[slot] = LocalSlotState.Active;
                Debug.Log($"[Voxelforge] Local slot {slot}: {device.displayName} {(lost ? "disconnected" : "reconnected")}.");
            }
        }

        public void Dispose()
        {
            InputUser.onChange -= OnUserChange;
            _join.performed -= OnJoinPressed;
            _join.Dispose();
            foreach (LocalPlayerInput input in _inputs)
                input?.Dispose();
            Array.Clear(_inputs, 0, _inputs.Length);
        }
    }
}
