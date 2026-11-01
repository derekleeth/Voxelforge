using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Voxelforge.Core;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Puts mobs in the world where they live (Roadmap 5.2 step D), on the host. Every <see cref="interval"/> seconds it
    /// takes the next hero in turn and, if there's room under the caps, looks for a spot in a ring round them, behind
    /// them where it can: out of their view, in loaded ground, away from every hero and Home Totem. It reads the biome
    /// there (and whether it's a cave floor or above the snow line), picks a mob that lives there by its
    /// <see cref="MobSpawnRule"/>, and drops a group of them. Mobs that end up far from every hero and out of a fight
    /// leave again. Mobs aren't saved; the world fills back up as heroes move about. The rules live in
    /// <see cref="MobSpawnRules"/>. <b>Ctrl+F10</b> turns spawning off and on for play-tests.
    /// </summary>
    public sealed class MobSpawner : MonoBehaviour
    {
        [SerializeField] NetworkManager networkManager;

        [SerializeField] VoxelWorldManager world;

        [SerializeField] PlayerRegistry players;

        [Tooltip("Data/Mobs/MobBook.asset: every kind of mob and where it lives.")]
        [SerializeField] MobBook mobs;

        [SerializeField] bool spawning = true;

        [Tooltip("Seconds between tries. Each try is for one hero, taking turns.")]
        [SerializeField, Min(0.1f)] float interval = 2f;

        [Tooltip("How close to its hero a spawn may be, in metres.")]
        [SerializeField, Min(8f)] float ringInner = 24f;

        [Tooltip("How far from its hero a spawn may be, in metres.")]
        [SerializeField, Min(8f)] float ringOuter = 56f;

        [Tooltip("Mobs count towards a hero's cap within this many metres of them.")]
        [SerializeField, Min(8f)] float nearRadius = 64f;

        [Tooltip("Most mobs near any one hero.")]
        [SerializeField, Range(0, 32)] int perHeroCap = 6;

        [Tooltip("Most mobs in the world at once.")]
        [SerializeField, Range(0, 64)] int totalCap = 20;

        [Tooltip("Nothing spawns closer than this to any hero or Home Totem, in metres.")]
        [SerializeField, Min(0f)] float keepAway = 20f;

        [Tooltip("A mob this far from every hero, and not fighting, leaves the world, in metres.")]
        [SerializeField, Min(16f)] float despawnDistance = 80f;

        [Tooltip("Columns tried per hero turn before giving up until the next one.")]
        [SerializeField, Range(1, 16)] int spotsPerTry = 6;

        [Tooltip("How far under the ground's surface a cave floor must be, in blocks.")]
        [SerializeField, Range(4, 32)] int caveDepth = 8;

        [Tooltip("The share of spots looked for in caves rather than on the surface.")]
        [SerializeField, Range(0f, 1f)] float caveShare = 0.35f;

        [Tooltip("How far apart the members of a group land, in blocks.")]
        [SerializeField, Range(1, 8)] int groupSpread = 3;

        TerrainSampler _sampler;
        bool _hasSampler;
        Unity.Mathematics.Random _random;
        float _nextTry;
        float _nextSweep;
        int _nextHero;
        readonly List<Mob> _alive = new List<Mob>();
        readonly List<float3> _heroFeet = new List<float3>();
        readonly List<float3> _keepAwayFrom = new List<float3>();
        readonly Dictionary<PlayerId, int> _nearHero = new Dictionary<PlayerId, int>();

        bool IsServer => networkManager != null && networkManager.IsServer;

        public bool IsSpawning => spawning;
        public int PerHeroCap => perHeroCap;
        public int TotalCap => totalCap;

        /// <summary>Host only: every mob in the world, as of the last sweep.</summary>
        public int Alive => _alive.Count;

        /// <summary>Host only: how many mobs count towards <paramref name="hero"/>'s cap, as of the last sweep.</summary>
        public int NearHero(PlayerId hero) => _nearHero.TryGetValue(hero, out int count) ? count : 0;

        void Awake() => _random = new Unity.Mathematics.Random((uint)System.Environment.TickCount | 1u);

        void OnDestroy()
        {
            if (_hasSampler)
                _sampler.Dispose();
            _hasSampler = false;
        }

        void Update()
        {
            if (!IsServer || world == null || world.World == null || players == null || mobs == null)
                return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f10Key.wasPressedThisFrame && keyboard.ctrlKey.isPressed)
            {
                spawning = !spawning;
                Debug.Log($"[Voxelforge] Mob spawning {(spawning ? "on" : "off")}.", this);
            }

            float now = Time.time;
            if (now >= _nextSweep)
            {
                _nextSweep = now + 1f;
                Sweep();
            }
            if (spawning && now >= _nextTry && EnsureSampler())
            {
                _nextTry = now + interval / math.max(1, players.Players.Count);
                TrySpawnForNextHero();
            }
        }

        /// <summary>Counts every mob, near each hero and in all, and sends away those nobody is near.</summary>
        void Sweep()
        {
            _alive.Clear();
            _heroFeet.Clear();
            _nearHero.Clear();
            foreach (PlayerController hero in players.Players)
            {
                if (hero != null && hero.HasLanded)
                    _heroFeet.Add(hero.transform.position);
            }
            if (players.Targets == null)
                return;
            foreach (ICombatCreature creature in players.Targets.Creatures)
            {
                if (creature is Mob mob && mob.IsSpawned)
                    _alive.Add(mob);
            }

            for (int i = _alive.Count - 1; i >= 0; i--)
            {
                Mob mob = _alive[i];
                bool fighting = mob.Brain != null && (mob.Brain.IsFighting || mob.Brain.State == MobState.Flee);
                if (mob.IsTargetable && _heroFeet.Count > 0 &&
                    MobSpawnRules.ShouldDespawn(mob.Feet, fighting, _heroFeet, despawnDistance))
                {
                    mob.NetworkObject.Despawn();
                    _alive.RemoveAt(i);
                }
            }

            foreach (PlayerController hero in players.Players)
            {
                if (hero == null || !hero.HasLanded)
                    continue;
                float3 feet = hero.transform.position;
                int near = 0;
                foreach (Mob mob in _alive)
                {
                    if (math.distance(mob.Feet.xz, feet.xz) <= nearRadius)
                        near++;
                }
                _nearHero[hero.PlayerId] = near;
            }
        }

        bool EnsureSampler()
        {
            if (_hasSampler)
                return true;
            ChunkGenerationScheduler generator = world.Generator;
            if (generator == null || world.TerrainSettings == null || !generator.Biomes.IsCreated)
                return false;
            _sampler = new TerrainSampler(world.Seed, generator.Terrain, generator.Biomes, Allocator.Persistent);
            _hasSampler = true;
            return true;
        }

        void TrySpawnForNextHero()
        {
            IReadOnlyList<PlayerController> heroes = players.Players;
            if (heroes.Count == 0)
                return;
            PlayerController hero = heroes[_nextHero++ % heroes.Count];
            if (hero == null || !hero.HasLanded || hero.IsKnockedOut)
                return;
            int room = MobSpawnRules.Room(NearHero(hero.PlayerId), perHeroCap, _alive.Count, totalCap);
            if (room <= 0)
                return;

            GatherKeepAway();
            float3 heroFeet = hero.transform.position;
            float2 facing = ((float3)hero.transform.forward).xz;
            for (int attempt = 0; attempt < spotsPerTry; attempt++)
            {
                // The first tries stay behind the hero, so mobs don't pop into view.
                bool behind = attempt < spotsPerTry / 2 + 1;
                float2 spot = MobSpawnRules.RingSpot(heroFeet.xz, facing, behind, ringInner, ringOuter,
                    _random.NextFloat(), _random.NextFloat());
                bool cave = _random.NextFloat() < caveShare;
                if (TryFindSpot((int)math.floor(spot.x), (int)math.floor(spot.y), cave, out float3 feet, out MobDefinition kind) &&
                    !MobSpawnRules.IsNearAny(feet, _keepAwayFrom, keepAway))
                {
                    SpawnGroup(kind, feet, room);
                    return;
                }
            }
        }

        /// <summary>Every hero and every hero's Home Totem: nothing spawns close to them.</summary>
        void GatherKeepAway()
        {
            _keepAwayFrom.Clear();
            _keepAwayFrom.AddRange(_heroFeet);
            foreach (PlayerController hero in players.Players)
            {
                if (hero != null && hero.TryGetComponent(out PlayerHearth hearth) && hearth.HasHome)
                    _keepAwayFrom.Add(hearth.ServerHomeBlock);
            }
        }

        /// <summary>
        /// Finds ground to stand on in column (<paramref name="x"/>, <paramref name="z"/>): its surface, or a cave floor
        /// at least <see cref="caveDepth"/> under it. Then picks a mob that lives there.
        /// </summary>
        bool TryFindSpot(int x, int z, bool cave, out float3 feet, out MobDefinition kind)
        {
            kind = null;
            TerrainColumn column = _sampler.SampleColumn(x, z);
            // Just above the generated surface: high enough for a little building, low enough to start under tree tops.
            int top = cave ? column.Height - caveDepth : column.Height + 2;
            if (top < 2 || !SpawnPointFinder.TryFindSurface(world.World, x, z, top, (int)math.ceil(MobBook.MaxBodyHeight), out feet))
            {
                feet = default;
                return false;
            }
            if (cave && feet.y > column.Height - caveDepth + 1)
                return false;

            IReadOnlyList<BiomeSettings> biomes = world.TerrainSettings.Biomes;
            string biome = column.Biome >= 0 && column.Biome < biomes.Count && biomes[column.Biome] != null
                ? biomes[column.Biome].Name
                : null;
            bool snow = !cave && feet.y - 1f >= world.Generator.Terrain.SnowLine;
            kind = MobSpawnRules.Pick(mobs.Mobs, biome, cave, snow, _random.NextFloat());
            return kind != null;
        }

        void SpawnGroup(MobDefinition kind, float3 leader, int room)
        {
            int count = MobSpawnRules.GroupSize(kind.Spawn, _random.NextFloat(), room);
            if (count <= 0)
                return;
            int clearance = (int)math.ceil(kind.Motor.HalfExtents.y * 2f);
            for (int i = 0; i < count; i++)
            {
                float3 feet = leader;
                if (i > 0)
                {
                    int x = (int)math.floor(leader.x) + _random.NextInt(-groupSpread, groupSpread + 1);
                    int z = (int)math.floor(leader.z) + _random.NextInt(-groupSpread, groupSpread + 1);
                    // Beside the leader at about its height; if there's no room there, on the leader's own spot.
                    if (!SpawnPointFinder.TryFindSurface(world.World, x, z, (int)leader.y + 2, clearance, out feet) ||
                        math.abs(feet.y - leader.y) > 2f)
                        feet = leader;
                }
                Mob mob = Instantiate(kind.Prefab, (Vector3)feet, Quaternion.identity);
                mob.ServerInitialize(world, players, feet, _random.NextUInt(1u, uint.MaxValue));
                mob.NetworkObject.Spawn(destroyWithScene: true);
                _alive.Add(mob);
            }
            Debug.Log($"[Voxelforge] {count} x {kind.DisplayName} spawned at {leader}.", this);
        }
    }
}
