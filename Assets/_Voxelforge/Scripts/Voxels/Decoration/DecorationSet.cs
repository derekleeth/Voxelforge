using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Every decoration in the game (Roadmap 2.6). A decoration's index in this list is its type number in
    /// <see cref="DecorationInstance.Type"/>. <see cref="CreateLookups"/> turns it into job-friendly arrays.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Decoration Set", fileName = "DecorationSet")]
    public sealed class DecorationSet : ScriptableObject
    {
        [SerializeField] List<DecorationDefinition> decorations = new List<DecorationDefinition>();

        public IReadOnlyList<DecorationDefinition> Decorations => decorations;

        /// <summary>Returns every problem with the set. An empty list means it's valid.</summary>
        public List<string> Validate(TerrainSettings terrain, BlockRegistry registry)
        {
            var errors = new List<string>();
            var biomeNames = new HashSet<string>();
            if (terrain != null)
            {
                foreach (BiomeSettings biome in terrain.Biomes)
                    biomeNames.Add(biome.Name);
            }

            for (int i = 0; i < decorations.Count; i++)
            {
                DecorationDefinition decoration = decorations[i];
                if (decoration == null)
                {
                    errors.Add($"Entry {i} is empty.");
                    continue;
                }

                if (decoration.Mesh == null || decoration.Material == null)
                    errors.Add($"'{decoration.name}' needs a mesh and a material.");
                if (decoration.Kind == DecorationKind.Ground && decoration.Surfaces.Count == 0)
                    errors.Add($"'{decoration.name}' has no surface blocks to grow on.");
                if (decoration.Kind == DecorationKind.Prop)
                {
                    if (decoration.PropBlock == null || !decoration.PropBlock.IsProp)
                        errors.Add($"'{decoration.name}' is a prop, so it needs a Prop Block marked as a prop.");
                    else if (registry != null && !registry.TryGet(decoration.PropBlock.Id, out _))
                        errors.Add($"'{decoration.name}' is drawn on '{decoration.PropBlock.DisplayName}', which is not in the block registry.");
                }
                foreach (BlockDefinition surface in decoration.Surfaces)
                {
                    if (surface == null)
                        errors.Add($"'{decoration.name}' has an empty surface entry.");
                    else if (surface.Id >= DecorationInfo.MaxSurfaceId)
                        errors.Add($"'{decoration.name}' grows on '{surface.DisplayName}', but surface IDs must be below {DecorationInfo.MaxSurfaceId}.");
                    else if (registry != null && !registry.TryGet(surface.Id, out _))
                        errors.Add($"'{decoration.name}' grows on '{surface.DisplayName}', which is not in the block registry.");
                }
                foreach (BiomeWeight weight in decoration.Biomes)
                {
                    if (terrain != null && !biomeNames.Contains(weight.biome))
                        errors.Add($"'{decoration.name}' lists biome '{weight.biome}', which isn't in Terrain Settings.");
                }
            }

            // Every prop block needs a model, or it would be invisible (a block with only a floor model uses it on walls too).
            if (registry != null)
            {
                foreach (BlockDefinition block in registry.Blocks)
                {
                    if (block == null || !block.IsProp)
                        continue;
                    bool hasModel = false;
                    foreach (DecorationDefinition decoration in decorations)
                        hasModel |= decoration != null && decoration.Kind == DecorationKind.Prop && decoration.PropBlock == block;
                    if (!hasModel)
                        errors.Add($"Prop block '{block.DisplayName}' has no prop decoration, so it would be invisible.");
                }
            }

            // Every tree trunk needs a mesh over it, or it would be an invisible wall.
            if (terrain != null)
            {
                foreach (BiomeSettings biome in terrain.Biomes)
                {
                    if (biome.TreeChance <= 0f)
                        continue;
                    bool hasTree = false;
                    foreach (DecorationDefinition decoration in decorations)
                        hasTree |= decoration != null && decoration.Kind == DecorationKind.Tree && decoration.WeightIn(biome.Name) > 0f;
                    if (!hasTree)
                        errors.Add($"Biome '{biome.Name}' grows trees but no tree decoration lists it.");
                }
            }
            return errors;
        }

        /// <summary>
        /// Builds the decoration list for jobs, plus a flat table of weights: <c>weights[type * biomeCount + biome]</c>,
        /// with biomes in <paramref name="terrain"/>'s order. The caller owns both arrays and must Dispose them.
        /// </summary>
        /// <exception cref="InvalidOperationException">The set fails <see cref="Validate"/>.</exception>
        public void CreateLookups(TerrainSettings terrain, BlockRegistry registry, Allocator allocator,
            out NativeArray<DecorationInfo> infos, out NativeArray<float> biomeWeights)
        {
            List<string> errors = Validate(terrain, registry);
            if (errors.Count > 0)
                throw new InvalidOperationException($"Decoration set '{name}' is invalid:\n{string.Join("\n", errors)}");

            int biomeCount = terrain.Biomes.Count;
            infos = new NativeArray<DecorationInfo>(decorations.Count, allocator);
            biomeWeights = new NativeArray<float>(decorations.Count * biomeCount, allocator);
            for (int type = 0; type < decorations.Count; type++)
            {
                infos[type] = decorations[type].ToDecorationInfo();
                for (int biome = 0; biome < biomeCount; biome++)
                    biomeWeights[type * biomeCount + biome] = decorations[type].WeightIn(terrain.Biomes[biome].Name);
            }
        }

        /// <summary>Builds a set in code, for tests and tools.</summary>
        internal static DecorationSet Create(IEnumerable<DecorationDefinition> definitions)
        {
            var set = CreateInstance<DecorationSet>();
            set.decorations = new List<DecorationDefinition>(definitions);
            return set;
        }
    }
}
