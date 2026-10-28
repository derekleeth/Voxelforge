using System;
using System.Collections.Generic;
using UnityEngine;

namespace Voxelforge.Voxels
{
    public enum DecorationKind : byte
    {
        /// <summary>Grass, flowers, bushes: placed on top of a block by chance, no collision.</summary>
        Ground,
        /// <summary>Drawn over an invisible <see cref="BlockIds.TreeTrunk"/> column that terrain generation placed.</summary>
        Tree,
    }

    /// <summary>How likely (ground) or how often picked (tree) a decoration is in one biome.</summary>
    [Serializable]
    public struct BiomeWeight
    {
        [Tooltip("Biome name, exactly as written in Terrain Settings.")]
        public string biome;

        [Range(0f, 1f)] public float weight;
    }

    /// <summary>
    /// One kind of decoration: a grass tuft, a flower, a bush or a tree (Roadmap 2.6). Decorations aren't voxels:
    /// they are placed from the seed while a chunk is meshed, so they need no save data or network sync.
    /// Create one with Assets &gt; Create &gt; Voxelforge &gt; Decoration, then add it to the <see cref="DecorationSet"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Decoration", fileName = "NewDecoration")]
    public sealed class DecorationDefinition : ScriptableObject
    {
        [SerializeField] DecorationKind kind;

        [Header("Look")]
        [Tooltip("Mesh drawn up close. Its origin is the bottom centre; +Y is up.")]
        [SerializeField] Mesh mesh;

        [Tooltip("Cheaper mesh drawn beyond LOD Distance. Empty = always use the main mesh.")]
        [SerializeField] Mesh lodMesh;

        [Tooltip("Must use the Voxelforge/Foliage shader.")]
        [SerializeField] Material material;

        [Tooltip("Biome colour multiplied into the green parts of the texture.")]
        [SerializeField] BiomeTint tint;

        [SerializeField] bool castShadows = true;

        [Header("Distance")]
        [Tooltip("Switch to the LOD mesh beyond this distance, in metres.")]
        [SerializeField, Min(0f)] float lodDistance = 60f;

        [Tooltip("Not drawn beyond this distance. It fades out over the last part of the way.")]
        [SerializeField, Min(1f)] float maxDistance = 80f;

        [Header("Placement")]
        [Tooltip("Ground decorations only: the blocks it can grow on. The block above must be air.")]
        [SerializeField] List<BlockDefinition> surfaces = new List<BlockDefinition>();

        [Tooltip("Ground: chance per block top, multiplied by the biome weight. Trees: ignored (Terrain Settings sets tree chance).")]
        [SerializeField, Range(0f, 1f)] float density = 0.1f;

        [Tooltip("Ground: chance multiplier per biome. Trees: how often this tree is picked in each biome, relative to other trees. Biomes not listed get 0.")]
        [SerializeField] List<BiomeWeight> biomes = new List<BiomeWeight>();

        [SerializeField, Min(0.01f)] float minScale = 0.8f;
        [SerializeField, Min(0.01f)] float maxScale = 1.2f;

        [Tooltip("Trees only: the trunk height, in blocks, the mesh was modelled for. Taller trunks scale the tree up.")]
        [SerializeField, Min(1f)] float modelTrunkHeight = 5f;

        public DecorationKind Kind => kind;
        public Mesh Mesh => mesh;
        public Mesh LodMesh => lodMesh != null ? lodMesh : mesh;
        public Material Material => material;
        public bool CastShadows => castShadows;
        public float LodDistance => lodDistance;
        public float MaxDistance => maxDistance;
        public IReadOnlyList<BlockDefinition> Surfaces => surfaces;
        public IReadOnlyList<BiomeWeight> Biomes => biomes;

        /// <summary>This definition's weight in the biome called <paramref name="biomeName"/>, or 0 if it isn't listed.</summary>
        public float WeightIn(string biomeName)
        {
            foreach (BiomeWeight entry in biomes)
            {
                if (entry.biome == biomeName)
                    return entry.weight;
            }
            return 0f;
        }

        public DecorationInfo ToDecorationInfo()
        {
            ulong mask = 0;
            foreach (BlockDefinition surface in surfaces)
            {
                if (surface != null && surface.Id < DecorationInfo.MaxSurfaceId)
                    mask |= 1ul << surface.Id;
            }

            return new DecorationInfo
            {
                Kind = kind,
                Tint = tint,
                SurfaceMask = mask,
                Density = density,
                MinScale = minScale,
                MaxScale = Mathf.Max(maxScale, minScale),
                ModelTrunkHeight = modelTrunkHeight,
            };
        }

        /// <summary>Builds a definition in code, for tests and tools.</summary>
        internal static DecorationDefinition Create(DecorationKind kind, float density, IEnumerable<BlockDefinition> surfaces,
            IEnumerable<BiomeWeight> biomes, float minScale = 1f, float maxScale = 1f)
        {
            var definition = CreateInstance<DecorationDefinition>();
            definition.kind = kind;
            definition.density = density;
            definition.surfaces = new List<BlockDefinition>(surfaces);
            definition.biomes = new List<BiomeWeight>(biomes);
            definition.minScale = minScale;
            definition.maxScale = maxScale;
            return definition;
        }
    }
}
