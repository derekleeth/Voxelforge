using System;
using Unity.Mathematics;
using Unity.Netcode;

namespace Voxelforge.Gameplay
{
    [Flags]
    public enum PlayerButtons : byte
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
