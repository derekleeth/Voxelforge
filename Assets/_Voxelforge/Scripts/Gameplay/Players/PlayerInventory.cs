using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// One player's inventory window (Roadmap 4.5, D-028), owner-side. The Inventory button opens it in their own view
    /// (<see cref="InventoryView"/>, CLAUDE.md Law #5). While it's open <see cref="PlayerController"/> stops the hero and
    /// hands the controls to this: the stick moves the cursor, Select Next / Previous change tab, Jump picks a stack up and
    /// puts it down, Interact holds an item or wears a bag, Home sorts, Toggle Ghost sends the stack to the nearest partner
    /// and Mine drops it as a loot bag (the mouse player drops by clicking outside the window with a stack picked up).
    /// Every change is only a request: the
    /// <see cref="PlayerBlockBag"/> asks the host, which decides (Law #2), and the window just shows the synced slots.
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(PlayerBlockBag))]
    public sealed class PlayerInventory : MonoBehaviour
    {
        [Tooltip("How long the \"Holding ...\" line stays up, in seconds.")]
        [SerializeField, Range(0.5f, 5f)] float messageSeconds = 1.6f;

        PlayerController _player;
        PlayerBlockBag _bag;
        PlayerHands _hands;
        readonly InventoryModel _model = new InventoryModel();
        readonly List<ItemStack> _slots = new List<ItemStack>();
        NavRepeat _nav;
        PlayerButtons _previousButtons;
        string _message;
        float _messageUntil;

        /// <summary>Owner only: the window is showing and has this player's controls.</summary>
        public bool IsOpen { get; private set; }

        public InventoryModel Model => _model;

        /// <summary>The bag's slots as of this frame.</summary>
        public IReadOnlyList<ItemStack> Slots => _slots;

        /// <summary>The bag slot picked up to be put somewhere else, or -1.</summary>
        public int HeldSlot { get; private set; } = -1;

        public PlayerBlockBag Bag => _bag;

        /// <summary>The control scheme in use (from the HUD), so a gamepad's d-pad moves the cursor and number keys pick tabs.</summary>
        public string Scheme { get; set; }

        /// <summary>A short line after an action, or null.</summary>
        public string Message => Time.time < _messageUntil ? _message : null;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _bag = GetComponent<PlayerBlockBag>();
            _hands = GetComponent<PlayerHands>();
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

            if ((pressed & PlayerButtons.Inventory) != 0 && _player.HasLanded)
            {
                SetOpen(!IsOpen);
                return;
            }
            if (!IsOpen)
                return;
            if (!_player.HasLanded)
            {
                SetOpen(false);
                return;
            }

            Refresh();
            bool pad = Scheme == "Gamepad";
            if (pad && (pressed & PlayerButtons.ActionSlot9) != 0) // A tap of B: back out.
            {
                SetOpen(false);
                return;
            }

            int2 step = _nav.Update(input.Move + (pad ? DPad(input.Buttons) : float2.zero), Time.unscaledTime);
            if (!step.Equals(int2.zero))
                _model.Move(step.x, step.y);

            // The mouse player's wheel is Select Next / Previous, and scrolls the grid instead (InventoryView).
            if (Scheme != "KeyboardMouse")
            {
                if ((pressed & PlayerButtons.SelectNext) != 0)
                    _model.StepTab(+1);
                if ((pressed & PlayerButtons.SelectPrevious) != 0)
                    _model.StepTab(-1);
            }
            if (!pad)
            {
                for (int tab = 0; tab < InventoryModel.TabCount; tab++)
                {
                    if ((pressed & ActionSlotButtons.For(tab)) != 0)
                        _model.SetTab((InventoryTab)tab);
                }
            }
            _model.Rebuild(_slots, _bag.Rules);

            if ((pressed & PlayerButtons.Jump) != 0)
                PickOrPut();
            if ((pressed & PlayerButtons.Interact) != 0)
                Use();
            if ((pressed & PlayerButtons.Home) != 0)
                Sort();
            if ((pressed & PlayerButtons.ToggleGhost) != 0)
                SendToPartner();
            // The mouse player's Mine is the left click, which the window uses; they drop by clicking outside it.
            if ((pressed & PlayerButtons.Mine) != 0 && Scheme != "KeyboardMouse")
                Drop();
        }

        // The d-pad is action slots 1-4 (up, right, down, left) on a gamepad.
        static float2 DPad(PlayerButtons buttons)
        {
            float2 d = float2.zero;
            if ((buttons & PlayerButtons.ActionSlot1) != 0) d.y += 1f;
            if ((buttons & PlayerButtons.ActionSlot2) != 0) d.x += 1f;
            if ((buttons & PlayerButtons.ActionSlot3) != 0) d.y -= 1f;
            if ((buttons & PlayerButtons.ActionSlot4) != 0) d.x -= 1f;
            return d;
        }

        void SetOpen(bool open)
        {
            IsOpen = open;
            HeldSlot = -1;
            if (open)
                Refresh();
        }

        void Refresh()
        {
            _slots.Clear();
            for (int i = 0; i < _bag.SlotCount; i++)
                _slots.Add(_bag.GetSlot(i));
            // What was picked up went away (used, sorted or moved by the host): let go of it.
            if (HeldSlot >= _slots.Count || (HeldSlot >= 0 && _slots[HeldSlot].IsEmpty))
                HeldSlot = -1;
            _model.Rebuild(_slots, _bag.Rules);
        }

        /// <summary>Pick up the stack under the cursor, or put the one picked up there (moving, merging or swapping).</summary>
        public void PickOrPut()
        {
            if (_model.CursorSocket >= 0)
            {
                // Dropping a bag on a socket wears it.
                if (HeldSlot >= 0 && _bag.Rules.BagSlotsOf(_slots[HeldSlot].ItemId) > 0)
                    _bag.RequestEquip(HeldSlot);
                HeldSlot = -1;
                return;
            }
            int slot = _model.CursorSlot;
            if (slot < 0)
                return;
            if (HeldSlot < 0)
            {
                if (!_slots[slot].IsEmpty)
                    HeldSlot = slot;
                return;
            }
            if (slot != HeldSlot)
                _bag.RequestMove(HeldSlot, slot);
            HeldSlot = -1;
        }

        /// <summary>Wear the bag under the cursor, take off the socket's bag, or hold the item in hand.</summary>
        public void Use()
        {
            HeldSlot = -1;
            if (_model.CursorSocket >= 0)
            {
                if (_bag.Bags[_model.CursorSocket] != 0)
                    _bag.RequestUnequip(_model.CursorSocket);
                return;
            }
            int slot = _model.CursorSlot;
            if (slot < 0 || _slots[slot].IsEmpty)
                return;
            ushort itemId = _slots[slot].ItemId;
            if (_bag.Rules.BagSlotsOf(itemId) > 0)
            {
                _bag.RequestEquip(slot);
                return;
            }
            if (_hands != null)
            {
                _hands.OwnerSelect(itemId);
                Say("In hand!");
            }
        }

        /// <summary>The stack picked up, else the one under the cursor; -1 if neither.</summary>
        int ChosenSlot()
        {
            if (HeldSlot >= 0)
                return HeldSlot;
            int slot = _model.CursorSlot;
            return slot >= 0 && !_slots[slot].IsEmpty ? slot : -1;
        }

        /// <summary>Send to Partner (Roadmap 4.5): tosses the chosen stack to the nearest other hero.</summary>
        public void SendToPartner()
        {
            int slot = ChosenSlot();
            HeldSlot = -1;
            if (slot >= 0)
                _bag.RequestSendToPartner(slot);
        }

        /// <summary>Drops the chosen stack in front of the hero as a loot bag.</summary>
        public void Drop()
        {
            int slot = ChosenSlot();
            HeldSlot = -1;
            if (slot < 0)
                return;
            _bag.RequestDrop(slot);
            Say("Dropped!");
        }

        public void Sort()
        {
            HeldSlot = -1;
            _bag.RequestSort();
            Say("Sorted!");
        }

        void Say(string message)
        {
            _message = message;
            _messageUntil = Time.time + messageSeconds;
        }
    }
}
