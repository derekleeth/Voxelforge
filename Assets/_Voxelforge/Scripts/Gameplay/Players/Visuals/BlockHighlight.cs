using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// A thin box outline around one block, in a player's team colour, so they can see which block they're about to
    /// dig. Presentation only: it never decides what gets edited. Owned by <see cref="PlayerVoxelEditor"/>.
    /// </summary>
    public sealed class BlockHighlight : System.IDisposable
    {
        // Slightly bigger than a block so the bars sit just outside its faces and never flicker against them.
        const float Size = 1.006f;

        static Mesh s_mesh;

        readonly GameObject _object;
        readonly Material _material;

        public BlockHighlight(string name, Material template, Color color, float thickness)
        {
            _object = new GameObject(name);
            _object.AddComponent<MeshFilter>().sharedMesh = s_mesh != null ? s_mesh : s_mesh = CreateMesh(thickness);
            var renderer = _object.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _material = new Material(template) { name = $"{template.name} ({name})" };
            _material.SetColor("_BaseColor", color);
            renderer.sharedMaterial = _material;
            _object.SetActive(false);
        }

        /// <summary>Outlines the block at <paramref name="block"/>, or hides the outline when null.</summary>
        public void Show(int3? block)
        {
            if (block.HasValue)
                _object.transform.position = (float3)block.Value + 0.5f;
            if (_object.activeSelf != block.HasValue)
                _object.SetActive(block.HasValue);
        }

        public void Dispose()
        {
            Object.Destroy(_object);
            Object.Destroy(_material);
        }

        /// <summary>The 12 edges of a cube centred on the origin, each a thin square bar.</summary>
        static Mesh CreateMesh(float thickness)
        {
            var vertices = new System.Collections.Generic.List<Vector3>(12 * 8);
            var indices = new System.Collections.Generic.List<int>(12 * 36);
            float h = Size * 0.5f;
            float t = thickness * 0.5f;
            for (int axis = 0; axis < 3; axis++)
            for (int a = -1; a <= 1; a += 2)
            for (int b = -1; b <= 1; b += 2)
            {
                // A bar along `axis`, at the corner (a, b) of the other two axes.
                var center = Vector3.zero;
                var extents = Vector3.one * t;
                center[axis] = 0f;
                center[(axis + 1) % 3] = a * h;
                center[(axis + 2) % 3] = b * h;
                extents[axis] = h + t;
                AddBox(vertices, indices, center, extents);
            }

            var mesh = new Mesh { name = "BlockHighlight" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static readonly int[] BoxTriangles =
        {
            0, 2, 1, 1, 2, 3, // -z
            4, 5, 6, 5, 7, 6, // +z
            0, 1, 4, 1, 5, 4, // -y
            2, 6, 3, 3, 6, 7, // +y
            0, 4, 2, 2, 4, 6, // -x
            1, 3, 5, 3, 7, 5, // +x
        };

        static void AddBox(System.Collections.Generic.List<Vector3> vertices, System.Collections.Generic.List<int> indices,
            Vector3 center, Vector3 extents)
        {
            int start = vertices.Count;
            for (int i = 0; i < 8; i++) // Corner i: bit 0 = +x, bit 1 = +y, bit 2 = +z.
            {
                vertices.Add(center + new Vector3((i & 1) != 0 ? extents.x : -extents.x, (i & 2) != 0 ? extents.y : -extents.y,
                    (i & 4) != 0 ? extents.z : -extents.z));
            }
            foreach (int index in BoxTriangles)
                indices.Add(start + index);
        }
    }
}
