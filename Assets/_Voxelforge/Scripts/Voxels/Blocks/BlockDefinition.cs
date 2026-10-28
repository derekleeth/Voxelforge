using UnityEngine;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Designer-facing description of one block type. Create one with
    /// Assets > Create > Voxelforge > Block Definition, then add it to the <see cref="BlockRegistry"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Block Definition", fileName = "NewBlock")]
    public sealed class BlockDefinition : ScriptableObject
    {
        [Tooltip("Permanent ID saved in worlds and sent over the network. Never change or reuse an ID. Must match BlockIds.")]
        [SerializeField] ushort id;

        [SerializeField] string displayName;

        [Tooltip("Total mining damage needed to break the block. 0 = unbreakable.")]
        [SerializeField, Range(0, ushort.MaxValue)] int maxDurability = 20;

        [Tooltip("The block that drops when this one is mined or chopped. Empty = this block itself.")]
        [SerializeField] BlockDefinition drop;

        [Tooltip("How many blocks drop. 0 = nothing drops.")]
        [SerializeField, Range(0, 16)] int dropCount = 1;

        [Tooltip("Which tool mines it fastest (Roadmap 3.5): pickaxes for Pick, axes for Axe. Any = no tool needed.")]
        [SerializeField] HarvestKind harvestKind;

        [Tooltip("Blocks movement and has collision.")]
        [SerializeField] bool isSolid = true;

        [Tooltip("Light and neighbouring faces show through (glass, leaves, air).")]
        [SerializeField] bool isTransparent;

        [Tooltip("Drawn as a model (a Prop decoration for this block) instead of a cube, e.g. torches and workbenches. " +
                 "Aim rays stop at it even if it isn't solid. Props should also be transparent.")]
        [SerializeField] bool isProp;

        [Tooltip("Must rest on a solid block below or beside it, and breaks (dropping itself) when that block goes.")]
        [SerializeField] bool needsSupport;

        [Tooltip("Light the block gives off, 0-15.")]
        [SerializeField, Range(0, Voxel.MaxLight)] int lightEmission;

        [Tooltip("Flat color for maps, and shown on faces that have no texture.")]
        [SerializeField] Color32 mapColor = new Color32(255, 0, 255, 255);

        [Tooltip("Biome colour multiplied into the green parts of this block's textures (grass), or None.")]
        [SerializeField] BiomeTint biomeTint;

        [Header("Textures")]
        [Tooltip("Texture on the top face, and on every face unless Side/Bottom are set.")]
        [SerializeField] BlockTexture topTexture;

        [Tooltip("Texture on the 4 side faces. Empty = same as Top.")]
        [SerializeField] BlockTexture sideTexture;

        [Tooltip("Texture on the bottom face. Empty = same as Side.")]
        [SerializeField] BlockTexture bottomTexture;

        public ushort Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public int MaxDurability => maxDurability;
        public ushort DropId => drop != null ? drop.Id : id;
        public int DropCount => dropCount;
        public bool IsSolid => isSolid;
        public bool IsTransparent => isTransparent;
        public bool IsProp => isProp;
        public bool NeedsSupport => needsSupport;
        public HarvestKind HarvestKind => harvestKind;
        public int LightEmission => lightEmission;
        public Color32 MapColor => mapColor;
        public BiomeTint BiomeTint => biomeTint;
        public BlockTexture TopTexture => topTexture;
        public BlockTexture SideTexture => sideTexture != null ? sideTexture : topTexture;
        public BlockTexture BottomTexture => bottomTexture != null ? bottomTexture : SideTexture;

        public BlockInfo ToBlockInfo()
        {
            var flags = BlockFlags.Registered;
            if (isSolid)
                flags |= BlockFlags.Solid;
            if (isTransparent)
                flags |= BlockFlags.Transparent;
            if (isProp)
                flags |= BlockFlags.Prop;
            if (needsSupport)
                flags |= BlockFlags.NeedsSupport;

            return new BlockInfo
            {
                Id = id,
                MaxDurability = (ushort)Mathf.Clamp(maxDurability, 0, ushort.MaxValue),
                DropId = DropId,
                DropCount = (byte)Mathf.Clamp(dropCount, 0, 16),
                LightEmission = (byte)Mathf.Clamp(lightEmission, 0, Voxel.MaxLight),
                Flags = flags,
                Harvest = harvestKind,
                MapColor = mapColor,
                Tint = biomeTint,
                // BlockRegistry.CreateLookup fills in real layers; a definition alone doesn't know the array order.
                TopLayer = BlockInfo.NoTexture,
                SideLayer = BlockInfo.NoTexture,
                BottomLayer = BlockInfo.NoTexture,
            };
        }

        /// <summary>Builds a definition in code, for tests and tools.</summary>
        internal static BlockDefinition Create(ushort id, string displayName, int maxDurability = 20,
            bool isSolid = true, bool isTransparent = false, int lightEmission = 0, BiomeTint biomeTint = BiomeTint.None,
            BlockDefinition drop = null, int dropCount = 1, HarvestKind harvestKind = HarvestKind.Any, bool isProp = false,
            bool needsSupport = false)
        {
            var definition = CreateInstance<BlockDefinition>();
            definition.name = displayName;
            definition.id = id;
            definition.displayName = displayName;
            definition.maxDurability = maxDurability;
            definition.drop = drop;
            definition.dropCount = dropCount;
            definition.isSolid = isSolid;
            definition.isTransparent = isTransparent;
            definition.lightEmission = lightEmission;
            definition.biomeTint = biomeTint;
            definition.harvestKind = harvestKind;
            definition.isProp = isProp;
            definition.needsSupport = needsSupport;
            return definition;
        }
    }
}
