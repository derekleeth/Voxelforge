using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// One player's class window (Roadmap 4.6), owner-side. The Class button (K, or ] on the right-hand keyboard) opens it
    /// in their own view (<see cref="ClassPickerView"/>, CLAUDE.md Law #5); gamepads have no button left, so they open it
    /// from the inventory window with Place (LT). While it's open <see cref="PlayerController"/> stops the hero and hands
    /// the controls to this: left and right pick a class card (number keys jump to one), Jump or Interact chooses it,
    /// and Class, Inventory or a B tap close it. Choosing is only a request: <see cref="PlayerClass"/> asks the host,
    /// which decides (Law #2).
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(PlayerClass))]
    public sealed class PlayerClassMenu : MonoBehaviour
    {
        PlayerController _player;
        PlayerClass _class;
        PlayerInventory _inventory;
        readonly ClassPickerModel _model = new ClassPickerModel();
        NavRepeat _nav;
        PlayerButtons _previousButtons;

        /// <summary>Owner only: the window is showing and has this player's controls.</summary>
        public bool IsOpen { get; private set; }

        public ClassPickerModel Model => _model;

        public PlayerClass Class => _class;

        /// <summary>The control scheme in use (from the HUD), for the gamepad's way in and the hints.</summary>
        public string Scheme { get; set; }

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _class = GetComponent<PlayerClass>();
            _inventory = GetComponent<PlayerInventory>();
        }

        void Update()
        {
            if (!_player.IsSpawned || !_player.IsOwner)
            {
                IsOpen = false;
                return;
            }

            PlayerInputFrame input = _player.OwnerRawInput;
            PlayerButtons pressed = input.Buttons & ~_previousButtons;
            _previousButtons = input.Buttons;
            bool pad = Scheme == "Gamepad";

            if (!IsOpen)
            {
                bool fromInventory = pad && _inventory != null && _inventory.IsOpen && (pressed & PlayerButtons.Place) != 0;
                if (((pressed & PlayerButtons.ClassMenu) != 0 || fromInventory) && _player.HasLanded)
                {
                    if (fromInventory)
                        _inventory.OwnerClose();
                    Open();
                }
                return;
            }

            // Closing: Class again, Inventory (which then opens the bag), or a tap of B on a gamepad.
            if (!_player.HasLanded || (pressed & (PlayerButtons.ClassMenu | PlayerButtons.Inventory)) != 0 ||
                (pad && (pressed & PlayerButtons.ActionSlot9) != 0))
            {
                IsOpen = false;
                return;
            }

            int2 step = _nav.Update(input.Move + (pad ? PlayerInventory.DPad(input.Buttons) : float2.zero), Time.unscaledTime);
            if (step.x != 0)
                _model.Move(step.x);
            if (!pad)
            {
                for (int card = 0; card < _model.Count && card < PlayerActionBar.SlotCount; card++)
                {
                    if ((pressed & ActionSlotButtons.For(card)) != 0)
                        _model.PointAt(card);
                }
            }
            if ((pressed & (PlayerButtons.Jump | PlayerButtons.Interact)) != 0)
                Choose();
        }

        void Open()
        {
            ClassBook book = _class.Book;
            int count = book != null ? book.Classes.Count : 0;
            int current = 0;
            for (int i = 0; i < count; i++)
            {
                if (book.Classes[i] != null && book.Classes[i].Id == _class.Class)
                    current = i;
            }
            _model.Open(count, current);
            IsOpen = true;
            // Whatever opened it this frame mustn't also act in the window.
            _previousButtons = _player.OwnerRawInput.Buttons;
        }

        /// <summary>Chooses the class under the cursor (asking the host) and closes the window.</summary>
        public void Choose()
        {
            ClassBook book = _class.Book;
            if (book != null && _model.Cursor < book.Classes.Count && book.Classes[_model.Cursor] != null)
                _class.RequestChoose(book.Classes[_model.Cursor].Id);
            IsOpen = false;
        }

        /// <summary>Closes the window without choosing.</summary>
        public void Close() => IsOpen = false;
    }
}
