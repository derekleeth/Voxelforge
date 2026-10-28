using System;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Writes a <see cref="VoxelDeltaLog"/> as compact bytes and reads it back (Roadmap 3.4, D-017). Saves use it, and
    /// sending the world to joining players (7.2) can too. The caller compresses the stream.
    /// <para>
    /// Layout: <c>int chunkCount</c>, then per chunk <c>int x, y, z</c>, <c>int count</c> and its blocks sorted by
    /// local index: <c>varint gap</c> (index minus the previous index), <c>ushort currentId, byte currentState,
    /// ushort generatedId, byte generatedState</c>. Light is worked out again later and who made the edit is
    /// per-session, so neither is written.
    /// </para>
    /// </summary>
    public static class DeltaLogCodec
    {
        public static void Write(BinaryWriter writer, VoxelDeltaLog log)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (log == null) throw new ArgumentNullException(nameof(log));

            var indices = new List<int>();
            writer.Write(log.ChunkCount);
            foreach (ChunkCoord coord in log.Chunks)
            {
                IReadOnlyDictionary<int, VoxelDelta> deltas = log.GetChunkDeltas(coord);
                writer.Write(coord.X);
                writer.Write(coord.Y);
                writer.Write(coord.Z);
                writer.Write(deltas.Count);

                // Sorted, so the gaps are small and neighbouring edits (tunnels, walls) compress well.
                indices.Clear();
                indices.AddRange(deltas.Keys);
                indices.Sort();
                int previous = 0;
                foreach (int index in indices)
                {
                    VoxelDelta delta = deltas[index];
                    WriteVarInt(writer, (uint)(index - previous));
                    previous = index;
                    writer.Write(delta.Current.BlockId);
                    writer.Write(delta.Current.State);
                    writer.Write(delta.Generated.BlockId);
                    writer.Write(delta.Generated.State);
                }
            }
        }

        /// <summary>Reads a log written by <see cref="Write"/>. Throws <see cref="InvalidDataException"/> on bad data.</summary>
        public static VoxelDeltaLog Read(BinaryReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));

            var log = new VoxelDeltaLog();
            int chunkCount = reader.ReadInt32();
            if (chunkCount < 0)
                throw new InvalidDataException($"Negative chunk count {chunkCount}.");

            for (int c = 0; c < chunkCount; c++)
            {
                var coord = new ChunkCoord(new int3(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()));
                int count = reader.ReadInt32();
                if (count < 0 || count > VoxelConstants.ChunkVolume)
                    throw new InvalidDataException($"Chunk {coord} claims {count} edited blocks.");

                int index = 0;
                for (int i = 0; i < count; i++)
                {
                    index += (int)ReadVarInt(reader);
                    if ((uint)index >= VoxelConstants.ChunkVolume)
                        throw new InvalidDataException($"Block index {index} is outside chunk {coord}.");
                    var current = new Voxel(reader.ReadUInt16(), state: reader.ReadByte());
                    var generated = new Voxel(reader.ReadUInt16(), state: reader.ReadByte());
                    log.Restore(coord, index, current, generated);
                }
            }
            return log;
        }

        static void WriteVarInt(BinaryWriter writer, uint value)
        {
            while (value >= 0x80)
            {
                writer.Write((byte)(value | 0x80));
                value >>= 7;
            }
            writer.Write((byte)value);
        }

        static uint ReadVarInt(BinaryReader reader)
        {
            uint value = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                byte b = reader.ReadByte();
                value |= (uint)(b & 0x7F) << shift;
                if (b < 0x80)
                    return value;
            }
            throw new InvalidDataException("Variable-length number is too long.");
        }
    }
}
