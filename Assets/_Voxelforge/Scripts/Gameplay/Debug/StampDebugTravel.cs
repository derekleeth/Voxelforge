using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Play-test keys for ruins and arenas (Roadmap 5.4), host only. <b>F6</b> takes the whole party to the nearest ruin or
    /// arena that somebody hasn't found yet; <b>Shift+F6</b> to the nearest boss arena. It asks the
    /// <see cref="ChunkLoader"/> for the land there first (<see cref="IChunkViewerSource"/>), like a hearth, and moves
    /// everyone once it's ready. Heroes walking in still discover it the usual way.
    /// </summary>
    public sealed class StampDebugTravel : MonoBehaviour, IChunkViewerSource
    {
        [SerializeField] NetworkManager networkManager;

        [SerializeField] VoxelWorldManager world;

        [SerializeField] PlayerRegistry players;

        [SerializeField] ChunkLoader chunkLoader;

        [Tooltip("How far round the party it looks for a site, in blocks.")]
        [SerializeField, Min(100f)] float searchRadius = 3000f;

        [Tooltip("Give up if the land hasn't loaded after this many seconds.")]
        [SerializeField, Min(1f)] float loadWaitSeconds = 15f;

        const int TopY = 255;
        const int Clearance = 2;

        readonly List<StampSite> _sites = new List<StampSite>();
        bool _travelling;
        int2 _landing;
        float _giveUpAt;

        bool IsServer => networkManager != null && networkManager.IsServer;

        public bool TryGetViewerPosition(out float3 position)
        {
            position = new float3(_landing.x + 0.5f, 0f, _landing.y + 0.5f);
            return _travelling;
        }

        void Update()
        {
            if (!IsServer || world == null || players == null || world.StampSites == null)
                return;
            if (_travelling)
            {
                TryArrive();
                return;
            }
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.f6Key.wasPressedThisFrame)
                return;
            if (TryPickSite(arenaOnly: keyboard.shiftKey.isPressed, out StampSite site, out StampInfo info))
                Begin(site, info);
            else
                Debug.Log("[Voxelforge] F6: no ruins or arenas within reach.", this);
        }

        /// <summary>The nearest site to the party, preferring ones a hero hasn't found yet.</summary>
        bool TryPickSite(bool arenaOnly, out StampSite best, out StampInfo bestInfo)
        {
            best = default;
            bestInfo = default;
            float3 sum = float3.zero;
            int heroes = 0;
            foreach (PlayerController hero in players.Players)
            {
                if (hero == null || !hero.HasLanded)
                    continue;
                sum += (float3)hero.transform.position;
                heroes++;
            }
            if (heroes == 0)
                return false;
            float2 party = sum.xz / heroes;

            _sites.Clear();
            world.StampSites.SitesNear(party, searchRadius, _sites);
            float bestScore = float.MaxValue;
            bool found = false;
            foreach (StampSite site in _sites)
            {
                StampInfo info = world.StampSites.InfoOf(site);
                if (arenaOnly && info.Kind != StampKind.Arena)
                    continue;
                // Somewhere new comes first; only once everything near is found does it go back to old finds.
                float score = math.distance((float2)site.Centre, party) + (FoundByAll(site) ? searchRadius * 2f : 0f);
                if (score >= bestScore)
                    continue;
                bestScore = score;
                best = site;
                bestInfo = info;
                found = true;
            }
            return found;
        }

        bool FoundByAll(in StampSite site)
        {
            foreach (PlayerController hero in players.Players)
            {
                if (hero != null && hero.TryGetComponent(out PlayerExplorer explorer) &&
                    !explorer.Map.IsExplored(site.Centre.x + 0.5f, site.Centre.y + 0.5f))
                    return false;
            }
            return true;
        }

        void Begin(in StampSite site, in StampInfo info)
        {
            // In an arena, down on the boss's dais in the middle; at a ruin, just beside its middle.
            _landing = info.Kind == StampKind.Arena ? site.Centre : site.Centre + new int2(2, 0);
            _travelling = true;
            _giveUpAt = Time.time + loadWaitSeconds;
            chunkLoader?.Register(this, 2);
            Debug.Log($"[Voxelforge] F6: off to the {info.Id} at {site.Centre} ({site.Min.y}).", this);
        }

        void TryArrive()
        {
            int index = 0;
            var landings = new List<(PlayerController, float3)>();
            foreach (PlayerController hero in players.Players)
            {
                if (hero == null || !hero.HasLanded)
                    continue;
                // Side by side, two blocks apart.
                int2 column = _landing + new int2(index * 2, 0);
                index++;
                if (!SpawnPointFinder.TryFindSurface(world.World, column.x, column.y, TopY, Clearance, out float3 feet))
                {
                    if (Time.time > _giveUpAt)
                        Stop("the land there didn't load in time");
                    return; // Still streaming in.
                }
                landings.Add((hero, feet));
            }
            foreach ((PlayerController hero, float3 feet) in landings)
                hero.ServerTeleport(feet, hero.transform.eulerAngles.y);
            Stop(null);
        }

        void Stop(string why)
        {
            _travelling = false;
            chunkLoader?.Unregister(this);
            if (why != null)
                Debug.LogWarning($"[Voxelforge] F6 gave up: {why}.", this);
        }

        void OnDisable()
        {
            if (_travelling)
                Stop(null);
        }
    }
}
