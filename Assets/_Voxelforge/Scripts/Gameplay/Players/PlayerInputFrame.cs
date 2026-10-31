using System;
using Unity.Mathematics;
using Unity.Netcode;

namespace Voxelforge.Gameplay
{
    [Flags]
    public enum PlayerButtons : uint
    {
        None = 0,
        Jump = 1 << 0,
        /// <summary>Held: mine the block the hero is aiming at (Roadmap 3.2, <see cref="PlayerMiner"/>).</summary>
        Mine = 1 << 1,
        /// <summary>Place the selected block where the ghost block shows (Roadmap 3.3, <see cref="PlayerBuilder"/>).</summary>
        Place = 1 << 2,
        /// <summary>Pick the next block in the bag to place.</summary>
        SelectNext = 1 << 3,
        /// <summary>Pick the previous block in the bag to place.</summary>
        SelectPrevious = 1 << 4,
        /// <summary>Turn this player's ghost block preview on or off. Placing still works with it off.</summary>
        ToggleGhost = 1 << 5,
        /// <summary>Open or close the crafting window (Roadmap 3.5, <see cref="PlayerCrafter"/>).</summary>
        Interact = 1 << 6,
        /// <summary>Ask to hearth back to this player's Home Totem (Roadmap 3.8, <see cref="PlayerHearth"/>). Place confirms.</summary>
        Home = 1 << 7,
        /// <summary>
        /// Cast what's in action bar slot 1 (Roadmap 4.4, <see cref="PlayerActionBar"/>). Slots 2-9 follow in order, so
        /// slot n is <c>ActionSlot1 &lt;&lt; (n - 1)</c> (<see cref="ActionSlotButtons.For"/>).
        /// </summary>
        ActionSlot1 = 1 << 8,
        ActionSlot2 = 1 << 9,
        ActionSlot3 = 1 << 10,
        ActionSlot4 = 1 << 11,
        ActionSlot5 = 1 << 12,
        ActionSlot6 = 1 << 13,
        ActionSlot7 = 1 << 14,
        ActionSlot8 = 1 << 15,
        ActionSlot9 = 1 << 16,
        /// <summary>Open or close this player's inventory window (Roadmap 4.5, <see cref="PlayerInventory"/>). Not sent to the host.</summary>
        Inventory = 1 << 17,
        /// <summary>
        /// Open or close this player's class window (Roadmap 4.6, <see cref="PlayerClassMenu"/>). Not sent to the host.
        /// Gamepads have no button left, so they open it from the inventory window with Place (LT).
        /// </summary>
        ClassMenu = 1 << 18,
        /// <summary>
        /// Run faster (Roadmap 4.8). Already worked out on the owner's PC from a toggle or a hold (<see cref="SprintLatch"/>),
        /// so the host just reads it as "sprinting now".
        /// </summary>
        Sprint = 1 << 19,
    }

    public static class ActionSlotButtons
    {
        /// <summary>The button for action bar slot <paramref name="slot"/> (0-8).</summary>
        public static PlayerButtons For(int slot) => (PlayerButtons)((uint)PlayerButtons.ActionSlot1 << slot);
    }

    /// <summary>
    /// What one player is pressing this frame. The owning client sends it to the host, and the host
    /// decides what actually happens (CLAUDE.md Law #2). Sent as raw bytes, so keep it small and unmanaged.
    /// </summary>
    public struct PlayerInputFrame : INetworkSerializeByMemcpy
    {
        /// <summary>Stick or WASD direction: x = right, y = forward. Length 0-1.</summary>
        public float2 Move;
        /// <summary>Which way this player's camera faces, in degrees clockwise from north. The host turns <see cref="Move"/> by it.</summary>
        public float ViewYaw;
        /// <summary>How far this player's camera tilts down, in degrees. Used to aim at blocks (<see cref="PlayerVoxelEditor"/>).</summary>
        public float ViewPitch;
        public PlayerButtons Buttons;
        /// <summary>
        /// The item this player picked from their bag (Roadmap 3.5). The host only uses it if the bag really has one
        /// (<see cref="PlayerHands"/>), so a PC can't claim a tool it doesn't own.
        /// </summary>
        public ushort HeldItem;
        /// <summary>Counts up every frame, so the host can ignore frames that arrive late and out of order.</summary>
        public uint Sequence;

        public bool IsPressed(PlayerButtons button) => (Buttons & button) != 0;
    }
}
