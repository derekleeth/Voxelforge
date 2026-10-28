using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Draws a <see cref="DecorationLayer"/> with GPU instancing (Roadmap 2.6), without a GameObject per plant.
    /// <list type="bullet">
    /// <item>Each decoration type has one GPU buffer of instances. <see cref="Update"/> uploads only the slots that
    /// changed since last frame.</item>
    /// <item>Just before <b>each</b> camera renders, <c>DecorationCull.compute</c> tests every instance against that
    /// camera's frustum and draw distance and splits the visible ones into a near (full mesh) and a far (LOD mesh)
    /// list. Two <see cref="Graphics.RenderMeshIndirect"/> calls per type then draw them for that camera only.</item>
    /// </list>
    /// So every split-screen camera gets its own culling, and no code looks for "the" camera (CLAUDE.md Law #1).
    /// </summary>
    public sealed class DecorationRenderer : IDisposable
    {
        /// <summary>Cameras that don't draw this layer (e.g. the split-screen compositor) are skipped.</summary>
        public const int Layer = 0;

        const int ThreadGroupSize = 64;
        const int ArgsStride = 5 * sizeof(uint); // GraphicsBuffer.IndirectDrawIndexedArgs
        const int InstanceCountOffset = sizeof(uint);
        const float ShadowPlaneMargin = 12f;

        static readonly int InstancesId = Shader.PropertyToID("_Instances");
        static readonly int VisibleId = Shader.PropertyToID("_Visible");
        static readonly int VisibleNearId = Shader.PropertyToID("_VisibleNear");
        static readonly int VisibleFarId = Shader.PropertyToID("_VisibleFar");
        static readonly int SlotCountId = Shader.PropertyToID("_SlotCount");
        static readonly int FrustumPlanesId = Shader.PropertyToID("_FrustumPlanes");
        static readonly int CameraPositionId = Shader.PropertyToID("_CameraPosition");
        static readonly int LodDistanceId = Shader.PropertyToID("_LodDistance");
        static readonly int MaxDistanceId = Shader.PropertyToID("_MaxDistance");
        static readonly int BoundsCenterYId = Shader.PropertyToID("_BoundsCenterY");
        static readonly int BoundsRadiusId = Shader.PropertyToID("_BoundsRadius");
        static readonly int PlaneMarginId = Shader.PropertyToID("_PlaneMargin");
        static readonly int ViewerPositionId = Shader.PropertyToID("_ViewerPosition");
        static readonly int FadeStartId = Shader.PropertyToID("_FadeStart");
        static readonly int FadeEndId = Shader.PropertyToID("_FadeEnd");
        static readonly int WindId = Shader.PropertyToID("_VF_Wind");

        /// <summary>One decoration type's GPU instance buffer and fixed draw info.</summary>
        sealed class TypeGpu
        {
            public GraphicsBuffer Instances;
            public float BoundsCenterY;
            public float BoundsRadius;
            public readonly GraphicsBuffer.IndirectDrawIndexedArgs[] Args = new GraphicsBuffer.IndirectDrawIndexedArgs[2];
        }

        /// <summary>One camera's cull results for one type: near and far lists, draw arguments, and properties.</summary>
        sealed class CameraTypeBuffers
        {
            public GraphicsBuffer VisibleNear;
            public GraphicsBuffer VisibleFar;
            public GraphicsBuffer Args;
            public readonly MaterialPropertyBlock NearProperties = new MaterialPropertyBlock();
            public readonly MaterialPropertyBlock FarProperties = new MaterialPropertyBlock();
            public int Capacity;

            public void Dispose()
            {
                VisibleNear?.Dispose();
                VisibleFar?.Dispose();
                Args?.Dispose();
            }
        }

        readonly DecorationLayer _layer;
        readonly ComputeShader _cull;
        readonly int _kernel;
        readonly TypeGpu[] _gpu;
        readonly Dictionary<Camera, CameraTypeBuffers[]> _cameras = new Dictionary<Camera, CameraTypeBuffers[]>();
        readonly List<Camera> _deadCameras = new List<Camera>();
        readonly Plane[] _planes = new Plane[6];
        readonly Vector4[] _planeVectors = new Vector4[6];
        bool _isDisposed;

        public DecorationRenderer(DecorationLayer layer, ComputeShader cull)
        {
            _layer = layer ?? throw new ArgumentNullException(nameof(layer));
            _cull = cull != null ? cull : throw new ArgumentNullException(nameof(cull));
            _kernel = _cull.FindKernel("Cull");

            _gpu = new TypeGpu[layer.Types.Count];
            for (int i = 0; i < _gpu.Length; i++)
                _gpu[i] = CreateTypeGpu(layer.Types[i].Definition);

            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        /// <summary>Wind for every decoration: which way it blows (on the ground), how far tips bend (m), how fast.</summary>
        public void SetWind(Vector3 direction, float strength, float speed)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z).normalized;
            Shader.SetGlobalVector(WindId, new Vector4(flat.x, flat.z, strength, speed));
        }

        /// <summary>Call once per frame after meshing: uploads changed instances to the GPU.</summary>
        public void Update()
        {
            for (int type = 0; type < _gpu.Length; type++)
            {
                DecorationLayer.TypeData data = _layer.Types[type];
                TypeGpu gpu = _gpu[type];
                if (data.Resized || gpu.Instances == null || gpu.Instances.count != data.Instances.Length)
                {
                    gpu.Instances?.Dispose();
                    gpu.Instances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, data.Instances.Length, DecorationInstance.Stride);
                    gpu.Instances.SetData(data.Instances);
                }
                else if (data.DirtyStart < data.DirtyEnd)
                {
                    gpu.Instances.SetData(data.Instances, data.DirtyStart, data.DirtyStart, data.DirtyEnd - data.DirtyStart);
                }
                data.ClearDirty();
            }

            // Forget cameras that were destroyed (e.g. a player left).
            foreach (Camera camera in _cameras.Keys)
            {
                if (camera == null)
                    _deadCameras.Add(camera);
            }
            foreach (Camera camera in _deadCameras)
            {
                foreach (CameraTypeBuffers buffers in _cameras[camera])
                    buffers.Dispose();
                _cameras.Remove(camera);
            }
            _deadCameras.Clear();
        }

        void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
                return;
            if ((camera.cullingMask & (1 << Layer)) == 0)
                return;

            if (!_cameras.TryGetValue(camera, out CameraTypeBuffers[] perType))
            {
                perType = new CameraTypeBuffers[_gpu.Length];
                for (int i = 0; i < perType.Length; i++)
                    perType[i] = new CameraTypeBuffers();
                _cameras.Add(camera, perType);
            }

            GeometryUtility.CalculateFrustumPlanes(camera, _planes);
            for (int i = 0; i < 6; i++)
                _planeVectors[i] = new Vector4(_planes[i].normal.x, _planes[i].normal.y, _planes[i].normal.z, _planes[i].distance);
            Vector3 cameraPosition = camera.transform.position;
            _cull.SetVectorArray(FrustumPlanesId, _planeVectors);
            _cull.SetVector(CameraPositionId, cameraPosition);

            for (int type = 0; type < _gpu.Length; type++)
            {
                DecorationLayer.TypeData data = _layer.Types[type];
                int slots = data.Allocator.UsedEnd;
                TypeGpu gpu = _gpu[type];
                if (slots == 0 || gpu.Instances == null || data.Definition.Material == null)
                    continue;
                DrawType(camera, cameraPosition, data.Definition, gpu, perType[type], slots);
            }
        }

        void DrawType(Camera camera, Vector3 cameraPosition, DecorationDefinition definition, TypeGpu gpu,
            CameraTypeBuffers buffers, int slots)
        {
            EnsureCapacity(buffers, gpu);

            buffers.VisibleNear.SetCounterValue(0);
            buffers.VisibleFar.SetCounterValue(0);
            _cull.SetBuffer(_kernel, InstancesId, gpu.Instances);
            _cull.SetBuffer(_kernel, VisibleNearId, buffers.VisibleNear);
            _cull.SetBuffer(_kernel, VisibleFarId, buffers.VisibleFar);
            _cull.SetInt(SlotCountId, slots);
            _cull.SetFloat(LodDistanceId, definition.LodMesh != definition.Mesh ? definition.LodDistance : float.MaxValue);
            _cull.SetFloat(MaxDistanceId, definition.MaxDistance);
            _cull.SetFloat(BoundsCenterYId, gpu.BoundsCenterY);
            _cull.SetFloat(BoundsRadiusId, gpu.BoundsRadius);
            _cull.SetFloat(PlaneMarginId, definition.CastShadows ? ShadowPlaneMargin : 0f);
            _cull.Dispatch(_kernel, (slots + ThreadGroupSize - 1) / ThreadGroupSize, 1, 1);

            GraphicsBuffer.CopyCount(buffers.VisibleNear, buffers.Args, InstanceCountOffset);
            GraphicsBuffer.CopyCount(buffers.VisibleFar, buffers.Args, ArgsStride + InstanceCountOffset);

            float fadeEnd = definition.MaxDistance;
            float fadeStart = fadeEnd * 0.8f;
            SetDrawProperties(buffers.NearProperties, gpu.Instances, buffers.VisibleNear, cameraPosition, fadeStart, fadeEnd);
            SetDrawProperties(buffers.FarProperties, gpu.Instances, buffers.VisibleFar, cameraPosition, fadeStart, fadeEnd);

            var renderParams = new RenderParams(definition.Material)
            {
                camera = camera,
                layer = Layer,
                // Culling happens in the compute pass, so Unity's own bounds test must never reject the draw.
                worldBounds = new Bounds(cameraPosition, Vector3.one * (definition.MaxDistance * 2f + 100f)),
                shadowCastingMode = definition.CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = true,
                matProps = buffers.NearProperties,
            };
            Graphics.RenderMeshIndirect(renderParams, definition.Mesh, buffers.Args, 1, 0);
            if (definition.LodMesh != definition.Mesh)
            {
                renderParams.matProps = buffers.FarProperties;
                Graphics.RenderMeshIndirect(renderParams, definition.LodMesh, buffers.Args, 1, 1);
            }
        }

        static void SetDrawProperties(MaterialPropertyBlock properties, GraphicsBuffer instances, GraphicsBuffer visible,
            Vector3 viewer, float fadeStart, float fadeEnd)
        {
            properties.SetBuffer(InstancesId, instances);
            properties.SetBuffer(VisibleId, visible);
            properties.SetVector(ViewerPositionId, viewer);
            properties.SetFloat(FadeStartId, fadeStart);
            properties.SetFloat(FadeEndId, fadeEnd);
        }

        static void EnsureCapacity(CameraTypeBuffers buffers, TypeGpu gpu)
        {
            int capacity = gpu.Instances.count;
            if (buffers.Capacity == capacity)
                return;

            buffers.Dispose();
            buffers.VisibleNear = new GraphicsBuffer(GraphicsBuffer.Target.Append, capacity, sizeof(uint));
            buffers.VisibleFar = new GraphicsBuffer(GraphicsBuffer.Target.Append, capacity, sizeof(uint));
            buffers.Args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 2, ArgsStride);
            buffers.Args.SetData(gpu.Args);
            buffers.Capacity = capacity;
        }

        static TypeGpu CreateTypeGpu(DecorationDefinition definition)
        {
            var gpu = new TypeGpu();
            Mesh near = definition.Mesh;
            Mesh far = definition.LodMesh;
            if (near == null)
                return gpu;

            Bounds bounds = near.bounds;
            if (far != null)
                bounds.Encapsulate(far.bounds);
            gpu.BoundsCenterY = bounds.center.y;
            gpu.BoundsRadius = bounds.extents.magnitude + new Vector2(bounds.center.x, bounds.center.z).magnitude;
            gpu.Args[0] = ArgsFor(near);
            gpu.Args[1] = ArgsFor(far != null ? far : near);
            return gpu;
        }

        static GraphicsBuffer.IndirectDrawIndexedArgs ArgsFor(Mesh mesh)
        {
            return new GraphicsBuffer.IndirectDrawIndexedArgs
            {
                indexCountPerInstance = mesh.GetIndexCount(0),
                startIndex = mesh.GetIndexStart(0),
                baseVertexIndex = mesh.GetBaseVertex(0),
                instanceCount = 0,
                startInstance = 0,
            };
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;
            _isDisposed = true;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

            foreach (TypeGpu gpu in _gpu)
                gpu.Instances?.Dispose();
            foreach (CameraTypeBuffers[] perType in _cameras.Values)
            {
                foreach (CameraTypeBuffers buffers in perType)
                    buffers.Dispose();
            }
            _cameras.Clear();
        }
    }
}
