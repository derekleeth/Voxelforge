namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Permanent item IDs (Roadmap 3.5). Every block is also an item with its block ID (<see cref="Voxels.BlockIds"/>),
    /// so items that aren't blocks start at <see cref="FirstNonBlock"/>. These numbers are saved in player bags and sent
    /// over the network, so never change or reuse one. Each must match the ID on its <see cref="ItemDefinition"/> asset.
    /// </summary>
    public static class ItemIds
    {
        /// <summary>IDs below this are blocks; at or above it, tools and other items.</summary>
        public const ushort FirstNonBlock = 1000;

        public const ushort WoodenPickaxe = 1000;
        public const ushort StonePickaxe = 1001;
        public const ushort StoneAxe = 1002;
        /// <summary>Kian's axe on one side and pick on the other: the best tool for mining and chopping.</summary>
        public const ushort PickOfDestiny = 1003;
        /// <summary>Kian's M.G.R.S. (Magic Gun Rifle Staff, Roadmap 3.6): shoots Mana Bolts that break blocks.</summary>
        public const ushort Mgrs = 1004;

        // Wearable bags (Roadmap 4.5): each adds slots while worn in a bag socket.
        /// <summary>A small canvas satchel: +8 slots.</summary>
        public const ushort WovenSatchel = 1010;
        /// <summary>A canvas backpack with a bedroll: +12 slots.</summary>
        public const ushort TravelersPack = 1011;
        /// <summary>A big framed rucksack: +16 slots.</summary>
        public const ushort DelversRucksack = 1012;

        // Crafting materials (Roadmap 5.5) dropped by mobs: 1100-1199. Leatherworking and Alchemy (5.8) use them.
        public const ushort BoarHide = 1100;
        public const ushort BoarTusk = 1101;
        public const ushort WolfPelt = 1102;
        public const ushort Frostfang = 1103;
        /// <summary>A Crystal Slime's glowing core.</summary>
        public const ushort CrystalShard = 1104;
        public const ushort RhinoHide = 1105;
        public const ushort ThunderHorn = 1106;
        /// <summary>Elemental essences: a bit of each biome's magic, rarer than hides and fangs.</summary>
        public const ushort EarthEssence = 1107;
        public const ushort FrostEssence = 1108;
        public const ushort StormEssence = 1109;
        public const ushort ArcaneEssence = 1110;

        // Worn gear (Roadmap 5.5): 1200-1399, grouped by rarity. LootBuilder sets their stats.
        public const ushort BristlehideCap = 1200;
        public const ushort BristlehideJerkin = 1201;
        public const ushort BristlehideLeggings = 1202;
        public const ushort TrailwornBoots = 1203;
        public const ushort IronboundCudgel = 1204;
        public const ushort OakBuckler = 1205;

        public const ushort FrostfangHood = 1210;
        public const ushort TuskhideJerkin = 1211;
        public const ushort RhinohideGreaves = 1212;
        public const ushort WindrunnerBoots = 1213;
        public const ushort ShardglassWand = 1214;
        public const ushort ThunderhornShield = 1215;

        public const ushort CrystalDeepCrown = 1220;
        public const ushort StormhideHauberk = 1221;
        public const ushort GlacierheartBlade = 1222;

        /// <summary>The one Legendary for now: a storm-forged maul from the high mesas.</summary>
        public const ushort Skyrender = 1230;

        // Ore (Roadmap 5.7): what the ore blocks drop.
        public const ushort CopperOre = 1300;
        public const ushort IronOre = 1301;
        /// <summary>Mined from Mana Crystal Ore (Mining 50). The M.G.R.S., Home Totem and Delver's Rucksack take it.</summary>
        public const ushort ManaCrystal = 1302;

        // Herbs (Roadmap 5.7): picked from the foliage layer with Herbalism.
        /// <summary>A golden flower of the Meadows and Thunderhoof Plains (Herbalism 1).</summary>
        public const ushort Sunpetal = 1310;
        /// <summary>An icy blue flower of the Snowy Peaks (Herbalism 25).</summary>
        public const ushort Frostbloom = 1311;
        /// <summary>Thorny stems with glowing ember buds, from the Red Crags and the Desert (Herbalism 50).</summary>
        public const ushort Emberthorn = 1312;

        /// <summary>True if <paramref name="itemId"/> is a block, so it can be placed.</summary>
        public static bool IsBlock(ushort itemId) => itemId < FirstNonBlock;
    }
}
