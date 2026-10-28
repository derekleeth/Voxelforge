using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// What a player holds (Roadmap 3.5): a block to build with or a tool to mine with, picked from their bag with
    /// Select Next / Previous (D-016's block picker, now for every item).
    /// <list type="bullet">
    /// <item><b>Owner:</b> keeps the selection and sends it with every input frame (<see cref="PlayerInputFrame.HeldItem"/>).</item>
    /// <item><b>Host:</b> holds that item only if the bag really has one, and shares the result with every PC through
    /// <see cref="HeldItem"/>, so a PC can't mine with a tool it doesn't own (CLAUDE.md Law #2) and everyone sees the
    /// same thing in the hero's hand (<see cref="HeldItemView"/>).</item>
    /// </list>
    /// </summary>
    [RequireComponent(typeof(PlayerController), typeof(PlayerBlockBag))]
    public sealed class PlayerHands : NetworkBehaviour
    {
        [Tooltip("Data/Items/ItemRegistry.asset: tools and item models.")]
        [SerializeField] ItemRegistry items;

        [Tooltip("Materials/VoxelBlocks.mat: for blocks held in the hand.")]
        [SerializeField] Material blockMaterial;

        readonly NetworkVariable<ushort> _held = new NetworkVariable<ushort>();
        readonly List<ItemStack> _stacks = new List<ItemStack>();

        PlayerController _player;
        PlayerBlockBag _bag;
        PlayerCrafter _crafter;
        ItemLooks _looks;

        // Owner state.
        ushort _selected;
        PlayerButtons _previousButtons;

        /// <summary>Owner only: the item picked from the bag, or <see cref="BlockIds.Air"/> when the bag is empty.</summary>
        public ushort OwnerSelected => _selected;

        /// <summary>Every PC: what the host agreed this player is holding (<see cref="BlockIds.Air"/> = empty hands).</summary>
        public ushort HeldItem => _held.Value;

        public ItemRegistry Items => items;

        /// <summary>Meshes and materials for items, shared by this player's hand, ghost and HUD.</summary>
        public ItemLooks Looks => _looks ??= new ItemLooks(items, blockMaterial);

        /// <summary>Mining power multipliers of the held item (<see cref="ToolPower.Hands"/> for blocks and empty hands).</summary>
        public ToolPower HeldPower => items != null ? items.ToolPowerOf(_held.Value) : ToolPower.Hands;

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _bag = GetComponent<PlayerBlockBag>();
            _crafter = GetComponent<PlayerCrafter>();
        }

        public override void OnDestroy()
        {
            _looks?.Dispose();
            _looks = null;
            base.OnDestroy();
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner)
                return;

            PlayerButtons buttons = _player.OwnerInput.Buttons;
            PlayerButtons pressed = buttons & ~_previousButtons;
            _previousButtons = buttons;

            _stacks.Clear();
            for (int i = 0; i < _bag.SlotCount; i++)
                _stacks.Add(_bag.GetSlot(i));
            _selected = ItemSelection.Fix(_stacks, _selected);
            // The crafting window uses the same buttons to choose a recipe.
            if (_crafter != null && _crafter.IsMenuOpen)
                return;
            if ((pressed & PlayerButtons.SelectNext) != 0)
                _selected = ItemSelection.Next(_stacks, _selected);
            if ((pressed & PlayerButtons.SelectPrevious) != 0)
                _selected = ItemSelection.Previous(_stacks, _selected);
        }

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned)
                return;
            ushort wanted = _player.ServerInput.HeldItem;
            ushort held = wanted != BlockIds.Air && _bag.CountOf(wanted) > 0 ? wanted : BlockIds.Air;
            if (_held.Value != held)
                _held.Value = held;
        }
    }
}
