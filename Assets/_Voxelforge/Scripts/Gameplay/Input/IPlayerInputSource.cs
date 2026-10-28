namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Something on the owning client that reads one player's controls. <see cref="PlayerController"/> asks it for
    /// a frame every Update and sends that frame to the host. Roadmap 2.2 adds a device-bound source to replace
    /// <see cref="TempKeyboardInputSource"/>.
    /// </summary>
    public interface IPlayerInputSource
    {
        /// <summary>Reads the controls for the player in <paramref name="localSlot"/> (0 = Player 1 on this PC).</summary>
        PlayerInputFrame Read(byte localSlot);
    }
}
