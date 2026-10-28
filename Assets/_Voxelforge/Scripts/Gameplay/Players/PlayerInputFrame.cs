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
        /// <summary>Temporary (3.1): dig the block in front of the hero. Replaced by real mining in 3.2.</summary>
        DevDig = 1 << 1,
        /// <summary>Temporary (3.1): place a block in front of the hero. Replaced by ghost-block placement in 3.3.</summary>
        DevPlace = 1 << 2,
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
        /// <summary>Counts up every frame, so the host can ignore frames that arrive late and out of order.</summary>
        public uint Sequence;

        public bool IsPressed(PlayerButtons button) => (Buttons & button) != 0;
    }
}
