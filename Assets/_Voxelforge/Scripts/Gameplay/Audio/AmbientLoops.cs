using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Looping sounds beside placed props, such as torches crackling (Roadmap 3.9, D-022). Like <see cref="PropLights"/>,
    /// a world can hold any number of torches, so only the few nearest to <b>any</b> local player get a looping voice
    /// from the <see cref="SoundRouter"/>; which props loop, and what they play, comes from <see cref="SoundBook.Loops"/>.
    /// Reads the decoration layer, so it never changes blocks.
    /// </summary>
    public sealed class AmbientLoops : MonoBehaviour
    {
        [SerializeField] SoundRouter router;

        [SerializeField] SoundBook sounds;

        [SerializeField] VoxelWorldManager world;

        [Tooltip("Most loops playing at once, across every player.")]
        [SerializeField, Range(1, 16)] int maxLoops = 6;

        [Tooltip("Seconds between looking for the nearest props.")]
        [SerializeField, Range(0.05f, 1f)] float refreshInterval = 0.2f;

        struct Candidate
        {
            public float Distance;
            public int3 Key;
            public float3 Position;
            public int Loop;
        }

        readonly List<Candidate> _candidates = new List<Candidate>();
        readonly Dictionary<int3, int> _playing = new Dictionary<int3, int>();
        readonly HashSet<int3> _chosen = new HashSet<int3>();
        readonly List<int3> _stale = new List<int3>();
        static readonly Comparer<Candidate> ByDistance = Comparer<Candidate>.Create((a, b) => a.Distance.CompareTo(b.Distance));
        float _nextRefresh;

        void OnDisable()
        {
            if (router != null)
            {
                foreach (int id in _playing.Values)
                    router.StopLoop(id);
            }
            _playing.Clear();
        }

        void LateUpdate()
        {
            if (router == null || sounds == null || Time.time < _nextRefresh)
                return;
            _nextRefresh = Time.time + refreshInterval;

            Gather();
            _candidates.Sort(ByDistance);
            _chosen.Clear();
            int count = math.min(_candidates.Count, maxLoops);
            for (int i = 0; i < count; i++)
            {
                Candidate candidate = _candidates[i];
                _chosen.Add(candidate.Key);
                if (_playing.ContainsKey(candidate.Key))
                    continue;
                AmbientLoop loop = sounds.Loops[candidate.Loop];
                int id = router.StartLoop(loop.clip, candidate.Position, loop.volume, loop.maxDistance);
                if (id != 0)
                    _playing.Add(candidate.Key, id);
            }

            _stale.Clear();
            foreach (KeyValuePair<int3, int> pair in _playing)
            {
                if (!_chosen.Contains(pair.Key))
                    _stale.Add(pair.Key);
            }
            foreach (int3 key in _stale)
            {
                router.StopLoop(_playing[key]);
                _playing.Remove(key);
            }
        }

        void Gather()
        {
            _candidates.Clear();
            DecorationLayer layer = world != null ? world.Decorations : null;
            AmbientLoop[] loops = sounds.Loops;
            if (layer == null || loops == null || loops.Length == 0)
                return;

            foreach (DecorationLayer.TypeData type in layer.Types)
            {
                int loop = IndexOf(loops, type.Definition);
                if (loop < 0)
                    continue;
                float maxDistance = loops[loop].maxDistance;
                for (int i = 0; i < type.Instances.Length; i++)
                {
                    DecorationInstance instance = type.Instances[i];
                    if (instance.Scale <= 0f)
                        continue; // An empty slot.
                    float3 position = instance.Position + new float3(0f, 0.5f, 0f);
                    float distance = router.NearestEarDistance(position);
                    if (distance <= maxDistance)
                        _candidates.Add(new Candidate { Distance = distance, Key = (int3)math.floor(position * 4f), Position = position, Loop = loop });
                }
            }
        }

        static int IndexOf(AmbientLoop[] loops, DecorationDefinition definition)
        {
            for (int i = 0; i < loops.Length; i++)
            {
                if (loops[i].decoration == definition && loops[i].clip != null)
                    return i;
            }
            return -1;
        }
    }
}
