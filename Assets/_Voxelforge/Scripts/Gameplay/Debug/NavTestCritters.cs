using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Play-test keys for the nav grid (Roadmap 5.1), host only. <b>F7</b> drops a <see cref="NavTestCritter"/> a few blocks
    /// from every hero, and each one chases the hero it was dropped by. <b>F8</b> shows or hides their paths (green: a way
    /// to the hero; amber: as close as it can get). <b>F9</b> sends them all away. Real mobs replace this in 5.2.
    /// </summary>
    public sealed class NavTestCritters : MonoBehaviour
    {
        [SerializeField] NetworkManager networkManager;

        [SerializeField] VoxelWorldManager world;

        [SerializeField] PlayerRegistry players;

        [SerializeField] NavTestCritter critterPrefab;

        [Tooltip("How far from its hero a critter lands, in blocks.")]
        [SerializeField, Range(2, 24)] int spawnDistance = 8;

        [Tooltip("Most critters at once.")]
        [SerializeField, Range(1, 64)] int maxCritters = 16;

        [SerializeField] bool showPaths = true;

        readonly List<NavTestCritter> _critters = new List<NavTestCritter>();

        bool IsServer => networkManager != null && networkManager.IsServer;

        /// <summary>Host only: critters out right now.</summary>
        public int Count => _critters.Count;

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !IsServer || world == null || players == null || critterPrefab == null)
                return;
            _critters.RemoveAll(critter => critter == null);

            if (keyboard.f7Key.wasPressedThisFrame)
                SpawnForEveryHero();
            if (keyboard.f8Key.wasPressedThisFrame)
            {
                showPaths = !showPaths;
                foreach (NavTestCritter critter in _critters)
                    critter.ShowPath(showPaths);
            }
            if (keyboard.f9Key.wasPressedThisFrame)
                DespawnAll();
        }

        void SpawnForEveryHero()
        {
            foreach (PlayerController hero in players.Players)
            {
                if (hero == null || !hero.HasLanded || _critters.Count >= maxCritters)
                    continue;

                // Somewhere around the hero, on top of whatever is there.
                float3 heroFeet = hero.transform.position;
                float angle = UnityEngine.Random.Range(0f, 2f * math.PI);
                int x = (int)math.floor(heroFeet.x + math.sin(angle) * spawnDistance);
                int z = (int)math.floor(heroFeet.z + math.cos(angle) * spawnDistance);
                if (!SpawnPointFinder.TryFindSurface(world.World, x, z, (int)heroFeet.y + 12, 2, out float3 feet))
                {
                    Debug.Log($"[Voxelforge] No ground near {hero.PlayerId} for a nav critter yet.", this);
                    continue;
                }

                NavTestCritter critter = Instantiate(critterPrefab, feet, Quaternion.identity);
                critter.ServerInitialize(world, players, hero.PlayerId, feet);
                critter.ShowPath(showPaths);
                critter.NetworkObject.Spawn(destroyWithScene: true);
                _critters.Add(critter);
                Debug.Log($"[Voxelforge] Nav critter chasing {hero.PlayerId} landed at {feet}.", this);
            }
        }

        void DespawnAll()
        {
            foreach (NavTestCritter critter in _critters)
            {
                if (critter != null && critter.IsSpawned)
                    critter.NetworkObject.Despawn();
            }
            _critters.Clear();
        }
    }
}
