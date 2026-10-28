using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Little pictures of items for menus (Roadmap 3.6's crafting window): each item's model or block cube
    /// (<see cref="ItemLooks"/>) drawn once by a hidden camera into its own small texture, then kept. Presentation only,
    /// on any PC; nothing here is per player, so one cache serves every split-screen view.
    /// </summary>
    public sealed class ItemIcons : IDisposable
    {
        // An unnamed layer only the icon camera draws, far below the world, so no player camera ever sees the stage.
        const int StageLayer = 31;
        static readonly Vector3 StagePosition = new Vector3(0f, -5000f, 0f);

        readonly ItemLooks _looks;
        readonly int _size;
        readonly Dictionary<ushort, RenderTexture> _icons = new Dictionary<ushort, RenderTexture>();
        Camera _camera;
        GameObject _stage;
        MeshFilter _filter;
        MeshRenderer _renderer;
        Light _light;

        public ItemIcons(ItemLooks looks, int size = 128)
        {
            _looks = looks;
            _size = size;
        }

        /// <summary>
        /// The item's picture, or null until it's drawn: the first ask queues it for <see cref="DrawQueued"/>, because a
        /// camera can't render from inside OnGUI.
        /// </summary>
        public Texture Get(ushort itemId)
        {
            if (_icons.TryGetValue(itemId, out RenderTexture icon))
                return icon;
            _queued.Add(itemId);
            return null;
        }

        /// <summary>Draws the pictures asked for since last time. Call from Update or LateUpdate.</summary>
        /// <param name="blocks">For block cubes. Blocks wait until there is a world.</param>
        public void DrawQueued(NativeArray<BlockInfo> blocks)
        {
            if (_queued.Count == 0)
                return;
            foreach (ushort itemId in _queued)
            {
                if (_icons.ContainsKey(itemId) || _looks == null || !_looks.TryGet(itemId, blocks, out ItemLook look))
                    continue;
                EnsureStage();
                var icon = new RenderTexture(_size, _size, 16, RenderTextureFormat.ARGB32) { name = $"Item Icon {itemId}", antiAliasing = 4 };
                icon.Create();
                Render(look, icon);
                _icons.Add(itemId, icon);
            }
            _queued.Clear();
        }

        readonly HashSet<ushort> _queued = new HashSet<ushort>();

        void Render(ItemLook look, RenderTexture target)
        {
            _filter.sharedMesh = look.Mesh;
            _renderer.sharedMaterial = look.Material;
            Transform t = _filter.transform;
            // Fit into a unit box around the stage. Tools lean across the slot (their +Y is the handle);
            // blocks turn to show their top and two sides.
            Quaternion turn = look.IsModel
                ? Quaternion.Euler(0f, 0f, -40f) * Quaternion.Euler(0f, 35f, 0f)
                : Quaternion.Euler(28f, -40f, 0f);
            float scale = look.FitScale * (look.IsModel ? 1.1f : 0.68f);
            t.localScale = Vector3.one * scale;
            t.rotation = turn;
            t.position = StagePosition - turn * (look.Center * scale);

            _stage.SetActive(true);
            _camera.targetTexture = target;
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(_camera, request))
                RenderPipeline.SubmitRenderRequest(_camera, request);
            else
                _camera.Render();
            _camera.targetTexture = null;
            _stage.SetActive(false);
        }

        void EnsureStage()
        {
            if (_stage != null)
                return;
            _stage = new GameObject("Item Icon Stage") { hideFlags = HideFlags.HideAndDontSave };
            _stage.SetActive(false);

            var model = new GameObject("Model") { layer = StageLayer, hideFlags = HideFlags.HideAndDontSave };
            model.transform.SetParent(_stage.transform, false);
            _filter = model.AddComponent<MeshFilter>();
            _renderer = model.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            // The camera stays active (only its component is off) so render requests work while the stage is hidden.
            _camera = new GameObject("Item Icon Camera") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.cullingMask = 1 << StageLayer;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _camera.orthographic = true;
            _camera.orthographicSize = 0.62f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 10f;
            _camera.allowHDR = false;
            _camera.allowMSAA = true;
            _camera.transform.position = StagePosition + new Vector3(0f, 0f, -3f);
            _camera.transform.rotation = Quaternion.identity;

            // Its own key light, from the upper left, only on the stage, so icons look the same at noon and at midnight.
            _light = new GameObject("Key Light") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Light>();
            _light.transform.SetParent(_stage.transform, false);
            _light.type = LightType.Point;
            _light.transform.position = StagePosition + new Vector3(-1.5f, 2f, -2.5f);
            _light.range = 8f;
            _light.intensity = 6f;
            _light.cullingMask = 1 << StageLayer;
            _light.shadows = LightShadows.None;
        }

        public void Dispose()
        {
            foreach (RenderTexture icon in _icons.Values)
            {
                if (icon != null)
                {
                    icon.Release();
                    UnityEngine.Object.Destroy(icon);
                }
            }
            _icons.Clear();
            if (_stage != null)
                UnityEngine.Object.Destroy(_stage);
            if (_camera != null)
                UnityEngine.Object.Destroy(_camera.gameObject);
            _stage = null;
            _camera = null;
        }
    }
}
