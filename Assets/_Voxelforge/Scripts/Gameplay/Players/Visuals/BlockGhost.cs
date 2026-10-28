using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The see-through block that shows where a player's next block will go (Roadmap 3.3, D-016), in their team colour,
    /// or red where it isn't allowed. Presentation only: the host decides what really gets placed. Owned by
    /// <see cref="PlayerBuilder"/>. Prop blocks (torches, Roadmap 3.5) show their model, coloured from its own texture
    /// (the shader's <c>_PROP_MODEL</c> variant), standing on the bottom of the cell.
    /// </summary>
    public sealed class BlockGhost : System.IDisposable
    {
        // A hair bigger than a block, so it never flickers against the faces of its neighbours.
        const float Size = 1.002f;

        static readonly int InvalidId = Shader.PropertyToID("_Invalid");
        static readonly int PropMapId = Shader.PropertyToID("_PropMap");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        const string PropKeyword = "_PROP_MODEL";

        readonly GameObject _object;
        readonly MeshFilter _filter;
        readonly MeshRenderer _renderer;
        readonly Material _material;
        readonly Material _propMaterial;

        public BlockGhost(string name, Material template, Color color)
        {
            _object = new GameObject(name);
            _object.transform.localScale = Vector3.one * Size;
            _filter = _object.AddComponent<MeshFilter>();
            _renderer = _object.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _material = new Material(template) { name = $"{template.name} ({name})" };
            _material.SetColor("_BaseColor", color);
            _propMaterial = new Material(_material) { name = $"{template.name} ({name}, model)" };
            _propMaterial.EnableKeyword(PropKeyword);
            _renderer.sharedMaterial = _material;
            _object.SetActive(false);
        }

        /// <summary>Shows <paramref name="look"/> in <paramref name="cell"/>, red if not <paramref name="allowed"/>.</summary>
        public void Show(ItemLook look, int3 cell, bool allowed)
        {
            _filter.sharedMesh = look.Mesh;
            Material material = look.IsModel ? _propMaterial : _material;
            if (look.IsModel && look.Material.HasProperty(BaseMapId))
                material.SetTexture(PropMapId, look.Material.GetTexture(BaseMapId));
            _renderer.sharedMaterial = material;
            // Models stand on the bottom of the cell; block cubes are centred and a hair bigger than a block.
            _object.transform.position = look.IsModel ? (float3)cell + new float3(0.5f, 0f, 0.5f) : (float3)cell + 0.5f;
            _object.transform.localScale = Vector3.one * (look.IsModel ? 1f : Size);
            material.SetFloat(InvalidId, allowed ? 0f : 1f);
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
            Object.Destroy(_propMaterial);
        }
    }
}
