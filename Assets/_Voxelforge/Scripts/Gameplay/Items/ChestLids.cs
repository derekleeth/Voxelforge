using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// The lids of Storage Chests (Roadmap 4.5), on every PC. A chest's body is a prop decoration like the Workbench, drawn
    /// with the rest of the props; its lid is drawn here instead, so it can swing open while anyone has that chest open
    /// (<see cref="PlayerChest"/>, synced to every PC) and fall shut after, with a creak and a thump. Like
    /// <see cref="PropLights"/>, it reads the decoration layer to find the chests near any player and gives the nearest a
    /// pooled lid. Presentation only: it never changes blocks or items.
    /// </summary>
    public sealed class ChestLids : MonoBehaviour
    {
        [SerializeField] VoxelWorldManager world;

        [Tooltip("Where the players are: chests near any of them get lids.")]
        [SerializeField] ChunkLoader chunkLoader;

        [Tooltip("ChestLid.fbx: the lid, its origin on the hinge.")]
        [SerializeField] Mesh lidMesh;

        [Tooltip("Props_Held.mat, the props atlas.")]
        [SerializeField] Material material;

        [Tooltip("Where the hinge is from the block's bottom centre, before the chest is turned (build_props.py).")]
        [SerializeField] Vector3 hinge = new Vector3(0f, 0.5f, 0.31f);

        [Tooltip("How far an open lid swings back, in degrees.")]
        [SerializeField, Range(30f, 120f)] float openAngle = 100f;

        [Tooltip("How fast lids open and close, in degrees per second.")]
        [SerializeField, Min(10f)] float swingSpeed = 360f;

        [Tooltip("Chests further than this from every player get no lid (their decorations stop being drawn at 90 m).")]
        [SerializeField, Range(8f, 128f)] float maxDistance = 90f;

        [Tooltip("The most lids drawn at once.")]
        [SerializeField, Range(1, 256)] int maxLids = 96;

        [Tooltip("For the creak when a lid opens and the thump when it shuts (Roadmap 3.9).")]
        [SerializeField] SoundRouter router;

        [SerializeField] SoundBook sounds;

        struct Candidate
        {
            public float Distance;
            public int3 Block;
            public float3 Position;
            public float Yaw;
        }

        readonly List<PlayerChest> _heroes = new List<PlayerChest>();
        readonly HashSet<int3> _open = new HashSet<int3>();
        // Lids part-way open or open, by chest block; a shut lid isn't listed.
        readonly Dictionary<int3, float> _angles = new Dictionary<int3, float>();
        readonly List<int3> _swinging = new List<int3>();
        readonly List<Candidate> _candidates = new List<Candidate>();
        readonly List<MeshRenderer> _pool = new List<MeshRenderer>();
        static readonly Comparer<Candidate> ByDistance = Comparer<Candidate>.Create((a, b) => a.Distance.CompareTo(b.Distance));

        void OnEnable()
        {
            PlayerController.AnySpawned += OnSpawned;
            PlayerController.AnyDespawned += OnDespawned;
        }

        void OnDisable()
        {
            PlayerController.AnySpawned -= OnSpawned;
            PlayerController.AnyDespawned -= OnDespawned;
            _heroes.Clear();
        }

        void OnSpawned(PlayerController player)
        {
            if (player.TryGetComponent(out PlayerChest chest) && !_heroes.Contains(chest))
                _heroes.Add(chest);
        }

        void OnDespawned(PlayerController player)
        {
            if (player != null && player.TryGetComponent(out PlayerChest chest))
                _heroes.Remove(chest);
        }

        void LateUpdate()
        {
            Swing();
            Gather();
            _candidates.Sort(ByDistance);

            int count = lidMesh != null && material != null ? math.min(_candidates.Count, maxLids) : 0;
            for (int i = 0; i < count; i++)
            {
                MeshRenderer lid = i < _pool.Count ? _pool[i] : CreateLid();
                Candidate chest = _candidates[i];
                _angles.TryGetValue(chest.Block, out float angle);
                Quaternion turn = Quaternion.Euler(0f, math.degrees(chest.Yaw), 0f);
                lid.transform.SetPositionAndRotation((Vector3)chest.Position + turn * hinge, turn * Quaternion.Euler(angle, 0f, 0f));
                if (!lid.enabled)
                    lid.enabled = true;
            }
            for (int i = count; i < _pool.Count; i++)
            {
                if (_pool[i].enabled)
                    _pool[i].enabled = false;
            }
        }

        /// <summary>Opens the lids of chests someone has open and lets the others fall shut.</summary>
        void Swing()
        {
            _open.Clear();
            foreach (PlayerChest hero in _heroes)
            {
                if (hero != null && hero.IsSpawned && hero.IsOpen)
                    _open.Add(hero.Block);
            }
            foreach (int3 block in _open)
            {
                if (!_angles.ContainsKey(block))
                {
                    _angles[block] = 0f;
                    Play(sounds != null ? sounds.TreeCreak : null, block, 0.35f, 1.7f);
                }
            }

            _swinging.Clear();
            _swinging.AddRange(_angles.Keys);
            float step = swingSpeed * Time.deltaTime;
            foreach (int3 block in _swinging)
            {
                bool open = _open.Contains(block);
                float angle = _angles[block];
                // Opening eases in; closing falls faster, like a heavy lid dropping.
                angle = open ? math.min(openAngle, angle + step) : angle - step * 1.5f;
                if (angle > 0f)
                {
                    _angles[block] = angle;
                    continue;
                }
                _angles.Remove(block);
                Play(sounds != null ? sounds.PickupPop : null, block, 0.6f, 0.55f);
            }
        }

        void Gather()
        {
            _candidates.Clear();
            DecorationLayer layer = world != null ? world.Decorations : null;
            IReadOnlyList<ChunkViewer> viewers = chunkLoader != null ? chunkLoader.Viewers : null;
            if (layer == null || viewers == null || viewers.Count == 0)
                return;

            foreach (DecorationLayer.TypeData type in layer.Types)
            {
                BlockDefinition block = type.Definition.PropBlock;
                if (type.Definition.Kind != DecorationKind.Prop || block == null || block.Id != BlockIds.Chest)
                    continue;
                for (int i = 0; i < type.Instances.Length; i++)
                {
                    DecorationInstance instance = type.Instances[i];
                    if (instance.Scale <= 0f)
                        continue; // An empty slot.
                    float nearest = float.MaxValue;
                    foreach (ChunkViewer viewer in viewers)
                        nearest = math.min(nearest, math.distance(instance.Position, viewer.Position));
                    if (nearest > maxDistance)
                        continue;
                    _candidates.Add(new Candidate
                    {
                        Distance = nearest,
                        // A prop sits at its block's bottom centre.
                        Block = (int3)math.floor(instance.Position - new float3(0.5f, 0f, 0.5f) + 0.01f),
                        Position = instance.Position,
                        Yaw = instance.Yaw,
                    });
                }
            }
        }

        void Play(AudioClip clip, int3 block, float volume, float pitch)
        {
            if (router != null && clip != null)
                router.Play(clip, (float3)block + new float3(0.5f, 0.6f, 0.5f), volume, pitch * UnityEngine.Random.Range(0.95f, 1.05f), 16f);
        }

        MeshRenderer CreateLid()
        {
            var go = new GameObject($"Chest Lid {_pool.Count}");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = lidMesh;
            var lid = go.AddComponent<MeshRenderer>();
            lid.sharedMaterial = material;
            lid.shadowCastingMode = ShadowCastingMode.On;
            _pool.Add(lid);
            return lid;
        }

        void OnDestroy()
        {
            foreach (MeshRenderer lid in _pool)
            {
                if (lid != null)
                    Destroy(lid.gameObject);
            }
            _pool.Clear();
        }
    }
}
