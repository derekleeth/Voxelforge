namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Something on the owning client that reads one player's controls. <see cref="PlayerController"/> asks it for
    /// a frame every Update and sends that frame to the host. <see cref="LocalPlayerInput"/> is the real one.
    /// </summary>
    public interface IPlayerInputSource
    {
        PlayerInputFrame Read();
    }
}
