using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The see-through block that shows where a player's next block will go (Roadmap 3.3, D-016), in their team colour,
    /// or red where it isn't allowed. Presentation only: the host decides what really gets placed. Owned by
    /// <see cref="PlayerBuilder"/>.
    /// </summary>
    public sealed class BlockGhost : System.IDisposable
    {
        // A hair bigger than a block, so it never flickers against the faces of its neighbours.
        const float Size = 1.002f;

        static readonly int InvalidId = Shader.PropertyToID("_Invalid");

        readonly GameObject _object;
        readonly MeshFilter _filter;
        readonly Material _material;
        readonly Dictionary<ushort, Mesh> _meshes = new Dictionary<ushort, Mesh>();

        public BlockGhost(string name, Material template, Color color)
        {
            _object = new GameObject(name);
            _object.transform.localScale = Vector3.one * Size;
            _filter = _object.AddComponent<MeshFilter>();
            var renderer = _object.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _material = new Material(template) { name = $"{template.name} ({name})" };
            _material.SetColor("_BaseColor", color);
            renderer.sharedMaterial = _material;
            _object.SetActive(false);
        }

        /// <summary>Shows <paramref name="blockId"/> in <paramref name="cell"/>, red if not <paramref name="allowed"/>.</summary>
        public void Show(NativeArray<BlockInfo> blocks, ushort blockId, int3 cell, bool allowed)
        {
            if (!_meshes.TryGetValue(blockId, out Mesh mesh))
            {
                mesh = BlockItemMesh.Create(blocks, blockId);
                _meshes.Add(blockId, mesh);
            }
            _filter.sharedMesh = mesh;
            _object.transform.position = (float3)cell + 0.5f;
            _material.SetFloat(InvalidId, allowed ? 0f : 1f);
            if (!_object.activeSelf)
                _object.SetActive(true);
        }

        public void Hide()
        {
            if (_object.activeSelf)
                _object.SetActive(false);
        }

        public void Dispose()
        {
            Object.Destroy(_object);
            Object.Destroy(_material);
            foreach (Mesh mesh in _meshes.Values)
                Object.Destroy(mesh);
            _meshes.Clear();
        }
    }
}
