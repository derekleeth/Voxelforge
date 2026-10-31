using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Unity.Mathematics;
using Voxelforge.Core;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>A whole saved world: the seed it grows from, the blocks players changed, and every player's profile.</summary>
    public sealed class WorldSave
    {
        public WorldSeed Seed;

        /// <summary><see cref="TerrainSampler.GeneratorVersion"/> when this was saved. Older = terrain may differ.</summary>
        public int GeneratorVersion;

        public DateTime SavedUtc;

        public VoxelDeltaLog Deltas = new VoxelDeltaLog();

        public Dictionary<PlayerUuid, PlayerProfile> Profiles = new Dictionary<PlayerUuid, PlayerProfile>();

        /// <summary>What's in every Storage Chest (Roadmap 4.5), by the chest block's position. Slots in order.</summary>
        public Dictionary<int3, List<ItemStack>> Chests = new Dictionary<int3, List<ItemStack>>();

        /// <summary>
        /// Earthen Rock raised by earth spells (Roadmap 4.6): each block and the seconds it has left before it crumbles,
        /// so a wall saved mid-way still crumbles on time after loading.
        /// </summary>
        public Dictionary<int3, float> Earth = new Dictionary<int3, float>();
    }

    public enum WorldLoadResult
    {
        Loaded,
        /// <summary>There is no save yet: start a new world.</summary>
        NotFound,
        /// <summary>The file isn't a Voxelforge save.</summary>
        NotASave,
        /// <summary>Made by a newer version of the game. Never overwrite it.</summary>
        NewerFormat,
        /// <summary>Damaged or cut short, e.g. the PC lost power mid-write.</summary>
        Corrupt,
    }

    /// <summary>
    /// Reads and writes <see cref="WorldSave"/>s (Roadmap 3.4, D-017). Plain C# so tests can drive it.
    /// <para>
    /// A save is the bytes <c>VFSV</c> and an <c>int</c> format version, then a GZip-compressed body: generator version,
    /// seed, save time, the <see cref="DeltaLogCodec"/> deltas, the profiles, and an end marker that catches files cut
    /// short. The version sits outside the compressed part so a newer save is refused before anything is decoded.
    /// </para>
    /// </summary>
    public static class WorldSaveFile
    {
        /// <summary>
        /// 1: Roadmap 3.4. 2: each profile ends with its Home Totem (3.8). 3: each profile's worn bags, and the Storage
        /// Chests after the profiles (4.5). 4: each profile's class, and the Earthen Rock after the chests (4.6). 5: each
        /// profile's race and whether they've chosen it (4.7). Older saves still load.
        /// </summary>
        public const int FormatVersion = 5;

        static readonly byte[] Magic = { (byte)'V', (byte)'F', (byte)'S', (byte)'V' };
        const uint EndMarker = 0x21444E45; // "END!"

        /// <summary>
        /// Encodes everything except the header, <b>uncompressed</b>. Cheap, so the host does it on the main thread
        /// while the world can't change, and leaves compressing and writing to a worker (<see cref="WriteFile"/>).
        /// </summary>
        public static byte[] EncodeBody(WorldSave save)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));

            using var memory = new MemoryStream();
            using (var writer = new BinaryWriter(memory, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(save.GeneratorVersion);
                writer.Write(save.Seed.Value);
                writer.Write(save.SavedUtc.ToUniversalTime().Ticks);
                DeltaLogCodec.Write(writer, save.Deltas ?? new VoxelDeltaLog());

                writer.Write(save.Profiles.Count);
                foreach (PlayerProfile profile in save.Profiles.Values)
                {
                    writer.Write(profile.Uuid.High);
                    writer.Write(profile.Uuid.Low);
                    writer.Write(profile.Name ?? "");
                    writer.Write(profile.Feet.x);
                    writer.Write(profile.Feet.y);
                    writer.Write(profile.Feet.z);
                    writer.Write(profile.Yaw);
                    WriteStacks(writer, profile.Bag);
                    writer.Write(profile.HasHome);
                    writer.Write(profile.HomeBlock.x);
                    writer.Write(profile.HomeBlock.y);
                    writer.Write(profile.HomeBlock.z);
                    for (int i = 0; i < EquippedBags.SocketCount; i++)
                        writer.Write(profile.Bags[i]);
                    writer.Write((byte)profile.Class);
                    writer.Write((byte)profile.Race);
                    writer.Write(profile.RaceChosen);
                }

                writer.Write(save.Chests.Count);
                foreach (KeyValuePair<int3, List<ItemStack>> chest in save.Chests)
                {
                    writer.Write(chest.Key.x);
                    writer.Write(chest.Key.y);
                    writer.Write(chest.Key.z);
                    WriteStacks(writer, chest.Value);
                }

                Dictionary<int3, float> earth = save.Earth ?? new Dictionary<int3, float>();
                writer.Write(earth.Count);
                foreach (KeyValuePair<int3, float> block in earth)
                {
                    writer.Write(block.Key.x);
                    writer.Write(block.Key.y);
                    writer.Write(block.Key.z);
                    writer.Write(block.Value);
                }
                writer.Write(EndMarker);
            }
            return memory.ToArray();
        }

        /// <summary>Writes the header and the compressed <paramref name="body"/> from <see cref="EncodeBody"/>.</summary>
        public static void WriteEncoded(Stream stream, byte[] body)
        {
            stream.Write(Magic, 0, Magic.Length);
            stream.Write(BitConverter.GetBytes(FormatVersion), 0, sizeof(int));
            using var gzip = new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true);
            gzip.Write(body, 0, body.Length);
        }

        public static void Write(Stream stream, WorldSave save) => WriteEncoded(stream, EncodeBody(save));

        /// <summary>
        /// Writes <paramref name="body"/> to <paramref name="path"/> safely: into a <c>.tmp</c> file first, which then
        /// replaces the save, and the old save is kept as <c>.bak</c>. A crash mid-write never loses the last save.
        /// Safe to call from a worker thread. Returns the file's size in bytes.
        /// </summary>
        public static long WriteFile(string path, byte[] body)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                WriteEncoded(file, body);
                file.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
                File.Replace(temp, path, path + ".bak", ignoreMetadataErrors: true);
            else
                File.Move(temp, path);
            return new FileInfo(path).Length;
        }

        /// <summary>Reads a save. Never throws for a bad file: the result says what was wrong.</summary>
        public static WorldLoadResult Read(Stream stream, out WorldSave save)
        {
            save = null;
            try
            {
                var header = new byte[Magic.Length + sizeof(int)];
                if (ReadFully(stream, header) < header.Length)
                    return WorldLoadResult.NotASave;
                for (int i = 0; i < Magic.Length; i++)
                {
                    if (header[i] != Magic[i])
                        return WorldLoadResult.NotASave;
                }
                int format = BitConverter.ToInt32(header, Magic.Length);
                if (format > FormatVersion)
                    return WorldLoadResult.NewerFormat;
                if (format < 1)
                    return WorldLoadResult.NotASave;

                using var gzip = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
                using var reader = new BinaryReader(gzip, Encoding.UTF8, leaveOpen: true);
                var result = new WorldSave
                {
                    GeneratorVersion = reader.ReadInt32(),
                    Seed = new WorldSeed(reader.ReadInt32()),
                    SavedUtc = new DateTime(reader.ReadInt64(), DateTimeKind.Utc),
                    Deltas = DeltaLogCodec.Read(reader),
                };

                int profileCount = reader.ReadInt32();
                if (profileCount < 0)
                    return WorldLoadResult.Corrupt;
                for (int p = 0; p < profileCount; p++)
                {
                    var profile = new PlayerProfile
                    {
                        Uuid = new PlayerUuid(reader.ReadUInt64(), reader.ReadUInt64()),
                        Name = reader.ReadString(),
                        Feet = new float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                        Yaw = reader.ReadSingle(),
                    };
                    if (!ReadStacks(reader, profile.Bag))
                        return WorldLoadResult.Corrupt;
                    if (format >= 2)
                    {
                        profile.HasHome = reader.ReadBoolean();
                        profile.HomeBlock = new int3(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                    }
                    if (format >= 3)
                    {
                        for (int i = 0; i < EquippedBags.SocketCount; i++)
                            profile.Bags[i] = reader.ReadUInt16();
                    }
                    if (format >= 4)
                        profile.Class = (HeroClassId)reader.ReadByte();
                    if (format >= 5)
                    {
                        profile.Race = (RaceId)reader.ReadByte();
                        profile.RaceChosen = reader.ReadBoolean();
                    }
                    if (profile.Uuid.IsValid)
                        result.Profiles[profile.Uuid] = profile;
                }

                if (format >= 3)
                {
                    int chestCount = reader.ReadInt32();
                    if (chestCount < 0)
                        return WorldLoadResult.Corrupt;
                    for (int c = 0; c < chestCount; c++)
                    {
                        var block = new int3(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                        var slots = new List<ItemStack>();
                        if (!ReadStacks(reader, slots))
                            return WorldLoadResult.Corrupt;
                        result.Chests[block] = slots;
                    }
                }

                if (format >= 4)
                {
                    int earthCount = reader.ReadInt32();
                    if (earthCount < 0)
                        return WorldLoadResult.Corrupt;
                    for (int e = 0; e < earthCount; e++)
                    {
                        var block = new int3(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                        result.Earth[block] = reader.ReadSingle();
                    }
                }

                if (reader.ReadUInt32() != EndMarker)
                    return WorldLoadResult.Corrupt;
                save = result;
                return WorldLoadResult.Loaded;
            }
            catch (Exception e) when (e is EndOfStreamException || e is InvalidDataException || e is IOException ||
                                      e is ArgumentException)
            {
                return WorldLoadResult.Corrupt;
            }
        }

        static void WriteStacks(BinaryWriter writer, List<ItemStack> stacks)
        {
            writer.Write(stacks.Count);
            foreach (ItemStack stack in stacks)
            {
                writer.Write(stack.ItemId);
                writer.Write(stack.Count);
            }
        }

        static bool ReadStacks(BinaryReader reader, List<ItemStack> stacks)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > ushort.MaxValue)
                return false;
            for (int s = 0; s < count; s++)
                stacks.Add(new ItemStack(reader.ReadUInt16(), reader.ReadUInt16()));
            return true;
        }

        public static WorldLoadResult ReadFile(string path, out WorldSave save)
        {
            save = null;
            if (!File.Exists(path))
                return WorldLoadResult.NotFound;
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Read(file, out save);
        }

        static int ReadFully(Stream stream, byte[] buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = stream.Read(buffer, total, buffer.Length - total);
                if (read == 0)
                    break;
                total += read;
            }
            return total;
        }
    }
}
