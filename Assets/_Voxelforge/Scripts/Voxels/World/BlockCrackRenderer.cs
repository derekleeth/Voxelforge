using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Draws cracks over every block in the <see cref="BlockDamageMap"/> (Roadmap 3.2, D-015): a cube a hair bigger
    /// than the block, with the <c>Voxelforge/Block Crack</c> shader multiplied over it. Cracks grow through
    /// <see cref="StageCount"/> stages as the damage nears the block's durability. One material per stage, so each
    /// stage is a single instanced draw. Drawn in world space, so every split-screen camera sees them.
    /// </summary>
    public sealed class BlockCrackRenderer : IDisposable
    {
        public const int StageCount = 8;

        // Just outside the block's faces; the shader's depth offset does the rest against z-fighting.
        const float Size = 1.004f;

        readonly Mesh _mesh;
        readonly Material[] _stages = new Material[StageCount];
        readonly List<Matrix4x4>[] _matrices = new List<Matrix4x4>[StageCount];

        public BlockCrackRenderer(Material template)
        {
            if (template == null)
                throw new ArgumentNullException(nameof(template));
            _mesh = CreateCube();
            for (int i = 0; i < StageCount; i++)
            {
                _stages[i] = new Material(template) { name = $"{template.name} (stage {i})", enableInstancing = true };
                _stages[i].SetFloat("_StageCount", StageCount);
                _stages[i].SetFloat("_Stage", i);
                _matrices[i] = new List<Matrix4x4>();
            }
        }

        /// <summary>Which crack stage a block shows at <paramref name="damage"/> out of <paramref name="maxDurability"/>.</summary>
        public static int StageFor(float damage, int maxDurability) =>
            maxDurability <= 0 ? 0 : math.clamp((int)(damage / maxDurability * StageCount), 0, StageCount - 1);

        public void Draw(BlockDamageMap damage, VoxelWorld world)
        {
            if (damage.Count == 0)
                return;

            foreach (List<Matrix4x4> list in _matrices)
                list.Clear();
            foreach (var pair in damage.Entries)
            {
                int max = world.Blocks[world.GetVoxel(pair.Key).BlockId].MaxDurability;
                if (max == 0)
                    continue;
                _matrices[StageFor(pair.Value.Damage, max)]
                    .Add(Matrix4x4.TRS((float3)pair.Key + 0.5f, Quaternion.identity, Vector3.one * Size));
            }

            for (int i = 0; i < StageCount; i++)
            {
                if (_matrices[i].Count == 0)
                    continue;
                var renderParams = new RenderParams(_stages[i])
                {
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                };
                Graphics.RenderMeshInstanced(renderParams, _mesh, 0, _matrices[i]);
            }
        }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(_mesh);
            foreach (Material material in _stages)
                UnityEngine.Object.Destroy(material);
        }

        /// <summary>A unit cube centred on the origin with its own 0-1 UVs on every face, facing outward.</summary>
        static Mesh CreateCube()
        {
            var vertices = new List<Vector3>(24);
            var uvs = new List<Vector2>(24);
            var indices = new List<int>(36);
            for (int axis = 0; axis < 3; axis++)
            for (int sign = -1; sign <= 1; sign += 2)
            {
                var normal = Vector3.zero;
                normal[axis] = sign;
                // Two axes across the face, ordered so the winding faces outward.
                var u = Vector3.zero;
                var v = Vector3.zero;
                u[(axis + 1) % 3] = 1f;
                v[(axis + 2) % 3] = 1f;
                if (sign < 0)
                    (u, v) = (v, u);

                int start = vertices.Count;
                Vector3 centre = normal * 0.5f;
                vertices.Add(centre - u * 0.5f - v * 0.5f);
                vertices.Add(centre + u * 0.5f - v * 0.5f);
                vertices.Add(centre + u * 0.5f + v * 0.5f);
                vertices.Add(centre - u * 0.5f + v * 0.5f);
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(0f, 1f));
                indices.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }

            var mesh = new Mesh { name = "Block Crack Cube" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
