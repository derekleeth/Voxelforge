using System;
using Unity.Mathematics;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The block a recipe must be made near (Roadmap 5.8). The profession's stations also decide which skill the recipe
    /// needs and raises (<see cref="CraftStations.TryGetProfession"/>). Stored in recipe assets, so never renumber one.
    /// </summary>
    public enum CraftStation : byte
    {
        Anywhere = 0,
        Workbench = 1,
        AlchemyTable = 2,
        Forge = 3,
        Anvil = 4,
        TanningRack = 5,
    }

    /// <summary>Which stations are within a hero's reach, from one look at the blocks around them.</summary>
    [Flags]
    public enum CraftStationMask : byte
    {
        None = 0,
        Workbench = 1 << 0,
        AlchemyTable = 1 << 1,
        Forge = 1 << 2,
        Anvil = 1 << 3,
        TanningRack = 1 << 4,
    }

    /// <summary>What each <see cref="CraftStation"/> is: its block, its name and its profession (Roadmap 5.8).</summary>
    public static class CraftStations
    {
        const CraftStationMask All = CraftStationMask.Workbench | CraftStationMask.AlchemyTable | CraftStationMask.Forge |
                                     CraftStationMask.Anvil | CraftStationMask.TanningRack;

        /// <summary>The station's block, or <see cref="BlockIds.Air"/> for <see cref="CraftStation.Anywhere"/>.</summary>
        public static ushort BlockOf(CraftStation station) => station switch
        {
            CraftStation.Workbench => BlockIds.Workbench,
            CraftStation.AlchemyTable => BlockIds.AlchemyTable,
            CraftStation.Forge => BlockIds.Forge,
            CraftStation.Anvil => BlockIds.Anvil,
            CraftStation.TanningRack => BlockIds.TanningRack,
            _ => BlockIds.Air,
        };

        public static string DisplayName(CraftStation station) => station switch
        {
            CraftStation.Workbench => "Workbench",
            CraftStation.AlchemyTable => "Alchemy Table",
            CraftStation.Forge => "Forge",
            CraftStation.Anvil => "Anvil",
            CraftStation.TanningRack => "Tanning Rack",
            _ => "Anywhere",
        };

        /// <summary>"a Forge", "an Anvil": for "Stand near an Anvil".</summary>
        public static string WithArticle(CraftStation station) =>
            station == CraftStation.AlchemyTable || station == CraftStation.Anvil
                ? $"an {DisplayName(station)}"
                : $"a {DisplayName(station)}";

        /// <summary>The profession a station's recipes belong to. False for Anywhere and the Workbench: they have none.</summary>
        public static bool TryGetProfession(CraftStation station, out ProfessionId profession)
        {
            switch (station)
            {
                case CraftStation.AlchemyTable:
                    profession = ProfessionId.Alchemy;
                    return true;
                case CraftStation.Forge:
                case CraftStation.Anvil:
                    profession = ProfessionId.Blacksmithing;
                    return true;
                case CraftStation.TanningRack:
                    profession = ProfessionId.Leatherworking;
                    return true;
                default:
                    profession = default;
                    return false;
            }
        }

        /// <summary>True if <paramref name="nearby"/> has <paramref name="station"/>. Anywhere is always near.</summary>
        public static bool IsNear(CraftStationMask nearby, CraftStation station) =>
            station == CraftStation.Anywhere || (nearby & MaskOf(station)) != 0;

        static CraftStationMask MaskOf(CraftStation station) => station switch
        {
            CraftStation.Workbench => CraftStationMask.Workbench,
            CraftStation.AlchemyTable => CraftStationMask.AlchemyTable,
            CraftStation.Forge => CraftStationMask.Forge,
            CraftStation.Anvil => CraftStationMask.Anvil,
            CraftStation.TanningRack => CraftStationMask.TanningRack,
            _ => CraftStationMask.None,
        };

        // Anywhere means the block is no station.
        static CraftStation StationOfBlock(ushort blockId)
        {
            switch (blockId)
            {
                case BlockIds.Workbench: return CraftStation.Workbench;
                case BlockIds.AlchemyTable: return CraftStation.AlchemyTable;
                case BlockIds.Forge: return CraftStation.Forge;
                case BlockIds.Anvil: return CraftStation.Anvil;
                case BlockIds.TanningRack: return CraftStation.TanningRack;
                default: return CraftStation.Anywhere;
            }
        }

        /// <summary>
        /// Every station within <paramref name="radius"/> blocks of <paramref name="center"/> (a box, measured block to
        /// block). Stops early once it has found them all.
        /// </summary>
        public static CraftStationMask ScanNear(VoxelWorld world, float3 center, int radius)
        {
            CraftStationMask found = CraftStationMask.None;
            if (world == null)
                return found;
            var middle = (int3)math.floor(center);
            for (int y = -radius; y <= radius; y++)
            for (int z = -radius; z <= radius; z++)
            for (int x = -radius; x <= radius; x++)
            {
                int3 cell = middle + new int3(x, y, z);
                if (!world.TryGetChunk(VoxelMath.WorldToChunk(cell), out _))
                    continue;
                found |= MaskOf(StationOfBlock(world.GetVoxel(cell).BlockId));
                if (found == All)
                    return found;
            }
            return found;
        }

        /// <summary>
        /// The closest station within <paramref name="radius"/> blocks of <paramref name="center"/>, or
        /// <see cref="CraftStation.Anywhere"/> if there's none: which tab the crafting window opens on.
        /// </summary>
        public static CraftStation Nearest(VoxelWorld world, float3 center, int radius)
        {
            CraftStation nearest = CraftStation.Anywhere;
            if (world == null)
                return nearest;
            var middle = (int3)math.floor(center);
            int best = int.MaxValue;
            for (int y = -radius; y <= radius; y++)
            for (int z = -radius; z <= radius; z++)
            for (int x = -radius; x <= radius; x++)
            {
                int distance = x * x + y * y + z * z;
                int3 cell = middle + new int3(x, y, z);
                if (distance >= best || !world.TryGetChunk(VoxelMath.WorldToChunk(cell), out _))
                    continue;
                CraftStation station = StationOfBlock(world.GetVoxel(cell).BlockId);
                if (station == CraftStation.Anywhere)
                    continue;
                best = distance;
                nearest = station;
            }
            return nearest;
        }
    }
}
