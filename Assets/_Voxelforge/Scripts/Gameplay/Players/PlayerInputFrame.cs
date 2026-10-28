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
    }

    /// <summary>
    /// What one player is pressing this frame. The owning client sends it to the host, and the host
    /// decides what actually happens (CLAUDE.md Law #2). Sent as raw bytes, so keep it small and unmanaged.
    /// </summary>
    public struct PlayerInputFrame : INetworkSerializeByMemcpy
    {
        /// <summary>Stick or WASD direction: x = right, y = forward. Length 0-1.</summary>
        public float2 Move;
        public PlayerButtons Buttons;
        /// <summary>Counts up every frame, so the host can ignore frames that arrive late and out of order.</summary>
        public uint Sequence;

        public bool IsPressed(PlayerButtons button) => (Buttons & button) != 0;
    }
}
