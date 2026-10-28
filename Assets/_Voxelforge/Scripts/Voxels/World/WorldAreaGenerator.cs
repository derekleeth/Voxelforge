using System.Collections.Generic;
using UnityEngine;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// Generates a fixed box of terrain chunks when Play starts, nearest the middle first.
    /// <b>Temporary:</b> the multi-viewer chunk loader (Roadmap 2.4) replaces it by streaming chunks around each player.
    /// </summary>
    public sealed class WorldAreaGenerator : MonoBehaviour
    {
        [SerializeField] VoxelWorldManager worldManager;

        [Tooltip("How many chunks to generate on each axis, starting at chunk (0, 0, 0). Each chunk is 32 blocks.")]
        [SerializeField] Vector3Int sizeInChunks = new Vector3Int(16, 8, 16);

        float _startTime = -1f;

        void Start()
        {
            if (worldManager == null || worldManager.Generator == null)
            {
                Debug.LogError($"{nameof(WorldAreaGenerator)} needs a working {nameof(VoxelWorldManager)}.", this);
                enabled = false;
                return;
            }

            var coords = new List<ChunkCoord>();
            for (int y = 0; y < sizeInChunks.y; y++)
            for (int z = 0; z < sizeInChunks.z; z++)
            for (int x = 0; x < sizeInChunks.x; x++)
                coords.Add(new ChunkCoord(x, y, z));

            // Middle columns first, top down, so the view fills in from the centre outwards.
            Vector2 middle = new Vector2(sizeInChunks.x - 1, sizeInChunks.z - 1) * 0.5f;
            coords.Sort((a, b) =>
            {
                int byDistance = (new Vector2(a.X, a.Z) - middle).sqrMagnitude.CompareTo((new Vector2(b.X, b.Z) - middle).sqrMagnitude);
                return byDistance != 0 ? byDistance : b.Y.CompareTo(a.Y);
            });

            foreach (ChunkCoord coord in coords)
                worldManager.Generator.Request(coord);
            _startTime = Time.realtimeSinceStartup;
        }

        void Update()
        {
            if (_startTime < 0f || !worldManager.Generator.IsIdle)
                return;

            Debug.Log($"[Voxelforge] Terrain (seed {worldManager.Seed}): {worldManager.Generator.GeneratedCount} chunks generated in " +
                      $"{(Time.realtimeSinceStartup - _startTime) * 1000f:F0} ms.", this);
            _startTime = -1f;
            enabled = false;
        }
    }
}
