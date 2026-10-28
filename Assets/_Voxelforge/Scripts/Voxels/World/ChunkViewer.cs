using Unity.Mathematics;
using static Voxelforge.Voxels.VoxelConstants;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// A point the world must exist around, and how far: a player, or a fixed anchor such as the spawn area.
    /// Not a camera. The host loads terrain around every viewer because its physics needs the blocks (D-010).
    /// </summary>
    public readonly struct ChunkViewer
    {
        public readonly float3 Position;

        /// <summary>Columns of chunks within this many chunks (a circle, measured column to column) are loaded.</summary>
        public readonly int RadiusChunks;

        public ChunkViewer(float3 position, int radiusChunks)
        {
            Position = position;
            RadiusChunks = radiusChunks;
        }

        /// <summary>The (x, z) chunk column the viewer stands in.</summary>
        public int2 Column => (int2)math.floor(Position.xz / ChunkSize);
    }

    /// <summary>Anything the <see cref="ChunkLoader"/> should load terrain around.</summary>
    public interface IChunkViewerSource
    {
        /// <summary>Where terrain is needed now. Returns false while there's no position yet; the source is skipped.</summary>
        bool TryGetViewerPosition(out float3 position);
    }
}
