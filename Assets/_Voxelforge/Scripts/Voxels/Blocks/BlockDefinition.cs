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

        [Tooltip("Blocks movement and has collision.")]
        [SerializeField] bool isSolid = true;

        [Tooltip("Light and neighbouring faces show through (glass, leaves, air).")]
        [SerializeField] bool isTransparent;

        [Tooltip("Light the block gives off, 0-15.")]
        [SerializeField, Range(0, Voxel.MaxLight)] int lightEmission;

        [Tooltip("Placeholder color until block textures arrive (Roadmap 1.5).")]
        [SerializeField] Color32 mapColor = new Color32(255, 0, 255, 255);

        public ushort Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public int MaxDurability => maxDurability;
        public bool IsSolid => isSolid;
        public bool IsTransparent => isTransparent;
        public int LightEmission => lightEmission;
        public Color32 MapColor => mapColor;

        public BlockInfo ToBlockInfo()
        {
            var flags = BlockFlags.Registered;
            if (isSolid)
                flags |= BlockFlags.Solid;
            if (isTransparent)
                flags |= BlockFlags.Transparent;

            return new BlockInfo
            {
                Id = id,
                MaxDurability = (ushort)Mathf.Clamp(maxDurability, 0, ushort.MaxValue),
                LightEmission = (byte)Mathf.Clamp(lightEmission, 0, Voxel.MaxLight),
                Flags = flags,
                MapColor = mapColor,
            };
        }

        /// <summary>Builds a definition in code, for tests and tools.</summary>
        internal static BlockDefinition Create(ushort id, string displayName, int maxDurability = 20,
            bool isSolid = true, bool isTransparent = false, int lightEmission = 0)
        {
            var definition = CreateInstance<BlockDefinition>();
            definition.name = displayName;
            definition.id = id;
            definition.displayName = displayName;
            definition.maxDurability = maxDurability;
            definition.isSolid = isSolid;
            definition.isTransparent = isTransparent;
            definition.lightEmission = lightEmission;
            return definition;
        }
    }
}
