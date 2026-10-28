using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using UnityEngine.InputSystem.Utilities;
using Object = UnityEngine.Object;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The controls for one player slot on this PC. It holds its own copy of the actions and an <see cref="InputUser"/>
    /// paired to that player's devices, so each player's actions only hear their own keyboard or gamepad.
    /// <para>
    /// One object lives per slot for the whole session, so the <see cref="PlayerController"/> keeps the same input
    /// source even when a new controller takes over a disconnected slot (<see cref="Pair"/> just swaps devices).
    /// </para>
    /// </summary>
    public sealed class LocalPlayerInput : IPlayerInputSource, IDisposable
    {
        readonly InputActionAsset _actions;
        readonly InputAction _move;
        readonly InputAction _jump;
        readonly InputAction _look;
        readonly InputAction _mine;
        readonly InputAction _place;
        readonly InputAction _selectNext;
        readonly InputAction _selectPrevious;
        readonly InputAction _toggleGhost;
        InputUser _user;

        public LocalPlayerInput(byte localSlot, InputActionAsset template)
        {
            LocalSlot = localSlot;
            _actions = Object.Instantiate(template);
            _actions.name = $"{template.name} (slot {localSlot})";
            _move = _actions.FindAction("Player/Move", throwIfNotFound: true);
            _jump = _actions.FindAction("Player/Jump", throwIfNotFound: true);
            _look = _actions.FindAction("Player/Look", throwIfNotFound: true);
            _mine = _actions.FindAction("Player/Mine", throwIfNotFound: true);
            _place = _actions.FindAction("Player/Place", throwIfNotFound: true);
            _selectNext = _actions.FindAction("Player/SelectNext", throwIfNotFound: true);
            _selectPrevious = _actions.FindAction("Player/SelectPrevious", throwIfNotFound: true);
            _toggleGhost = _actions.FindAction("Player/ToggleGhost", throwIfNotFound: true);
        }

        public byte LocalSlot { get; }

        /// <summary>The control scheme in use: KeyboardMouse, Gamepad, KeyboardLeft or KeyboardRight.</summary>
        public string ControlScheme { get; private set; }

        public InputUser User => _user;

        /// <summary>
        /// Which way this player's camera faces (degrees clockwise from north). Their camera rig sets it every frame,
        /// and it's sent with each input frame so "forward" means "away from my camera".
        /// </summary>
        public float ViewYaw { get; set; }

        /// <summary>How far this player's camera tilts down, in degrees. Their camera rig sets it every frame, for aiming.</summary>
        public float ViewPitch { get; set; }

        /// <summary>True for the keyboard + mouse scheme, where look input is mouse movement rather than a stick.</summary>
        public bool UsesMouse => ControlScheme == "KeyboardMouse";

        /// <summary>Set by the roster while this player's device is unplugged.</summary>
        public bool IsDisconnected { get; internal set; }

        /// <summary>A short name for the debug overlay, e.g. "Keyboard" or "Xbox Controller".</summary>
        public string DeviceName => _user.valid && _user.pairedDevices.Count > 0 ? _user.pairedDevices[0].displayName
            : _user.valid && _user.lostDevices.Count > 0 ? _user.lostDevices[0].displayName : "none";

        /// <summary>True if <paramref name="device"/> belongs to this player, even while it's unplugged.</summary>
        public bool Owns(InputDevice device) =>
            _user.valid && (_user.pairedDevices.ContainsReference(device) || _user.lostDevices.ContainsReference(device));

        /// <summary>Gives this player <paramref name="devices"/> (replacing any old ones) and switches to <paramref name="controlScheme"/>.</summary>
        public void Pair(string controlScheme, IReadOnlyList<InputDevice> devices)
        {
            Unpair();
            foreach (InputDevice device in devices)
                _user = InputUser.PerformPairingWithDevice(device, _user);

            _user.AssociateActionsWithUser(_actions);
            _user.ActivateControlScheme(controlScheme);
            ControlScheme = controlScheme;
            IsDisconnected = false;
            _actions.Enable();
        }

        public PlayerInputFrame Read()
        {
            if (IsDisconnected || !_user.valid)
                return default;

            var buttons = PlayerButtons.None;
            if (_jump.IsPressed())
                buttons |= PlayerButtons.Jump;
            if (_mine.IsPressed())
                buttons |= PlayerButtons.Mine;
            if (_place.IsPressed())
                buttons |= PlayerButtons.Place;
            if (_selectNext.IsPressed())
                buttons |= PlayerButtons.SelectNext;
            if (_selectPrevious.IsPressed())
                buttons |= PlayerButtons.SelectPrevious;
            if (_toggleGhost.IsPressed())
                buttons |= PlayerButtons.ToggleGhost;

            return new PlayerInputFrame
            {
                Move = (float2)_move.ReadValue<Vector2>(),
                ViewYaw = ViewYaw,
                ViewPitch = ViewPitch,
                Buttons = buttons,
            };
        }

        /// <summary>
        /// Camera look input: mouse movement in pixels this frame, or the right stick (-1 to 1). Stays on this PC; the
        /// host only hears the resulting <see cref="ViewYaw"/>.
        /// </summary>
        public Vector2 ReadLook() => IsDisconnected || !_user.valid ? Vector2.zero : _look.ReadValue<Vector2>();

        void Unpair()
        {
            _actions.Disable();
            if (_user.valid)
                _user.UnpairDevicesAndRemoveUser();
            _user = default;
        }

        public void Dispose()
        {
            Unpair();
            if (Application.isPlaying)
                Object.Destroy(_actions);
            else
                Object.DestroyImmediate(_actions);
        }
    }
}
