using System.Threading;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 4.8: the parchment map is painted from the seed, off the main thread, and matches the terrain.</summary>
    public class MapTileTests
    {
        const string TerrainAssetPath = "Assets/_Voxelforge/Data/Terrain/TerrainSettings.asset";
        const string RegistryAssetPath = "Assets/_Voxelforge/Data/Blocks/BlockRegistry.asset";

        static readonly WorldSeed TestSeed = new WorldSeed(20261027);

        NativeArray<BiomeInfo> _biomes;
        NativeArray<BlockInfo> _blocks;
        TerrainParams _terrain;

        [SetUp]
        public void SetUp()
        {
            var settings = AssetDatabase.LoadAssetAtPath<TerrainSettings>(TerrainAssetPath);
            var registry = AssetDatabase.LoadAssetAtPath<BlockRegistry>(RegistryAssetPath);
            _biomes = settings.CreateBiomeLookup(Allocator.Persistent);
            _blocks = registry.CreateLookup(Allocator.Persistent);
            _terrain = settings.ToTerrainParams();
        }

        [TearDown]
        public void TearDown()
        {
            _biomes.Dispose();
            _blocks.Dispose();
        }

        NativeArray<Color32> Paint(WorldSeed seed, int2 tile, out NativeArray<MapColumn> columns)
        {
            int2 origin = MapTile.Origin(tile);
            columns = new NativeArray<MapColumn>(MapTile.BorderedSize * MapTile.BorderedSize, Allocator.TempJob);
            var pixels = new NativeArray<Color32>(MapTile.Size * MapTile.Size, Allocator.TempJob);
            JobHandle sampled = new SampleMapColumnsJob { Origin = origin, Seed = seed, Terrain = _terrain, Biomes = _biomes, Columns = columns }
                .Schedule(MapTile.BorderedSize, 4);
            new PaintMapTileJob { Origin = origin, Columns = columns, Blocks = _blocks, Pixels = pixels }
                .Schedule(pixels.Length, 256, sampled).Complete();
            return pixels;
        }

        [Test]
        public void TileOf_HandlesNegativeColumns()
        {
            Assert.AreEqual(new int2(0, 0), MapTile.Of(0, 127));
            Assert.AreEqual(new int2(1, 0), MapTile.Of(128, 0));
            Assert.AreEqual(new int2(-1, -1), MapTile.Of(-1, -128));
            Assert.AreEqual(new int2(-2, 0), MapTile.Of(-129, 5));
            Assert.AreEqual(new int2(-1, 0), MapTile.Of(new float2(-0.5f, 0.5f)));
        }

        [Test]
        public void SameSeed_PaintsTheSameTile()
        {
            NativeArray<Color32> a = Paint(TestSeed, new int2(-1, 2), out NativeArray<MapColumn> ca);
            NativeArray<Color32> b = Paint(TestSeed, new int2(-1, 2), out NativeArray<MapColumn> cb);
            try
            {
                for (int i = 0; i < a.Length; i++)
                    Assert.AreEqual(a[i], b[i], $"pixel {i}");
            }
            finally
            {
                a.Dispose(); b.Dispose(); ca.Dispose(); cb.Dispose();
            }
        }

        [Test]
        public void Columns_MatchTheGeneratedTerrain()
        {
            NativeArray<Color32> pixels = Paint(TestSeed, new int2(3, -2), out NativeArray<MapColumn> columns);
            var sampler = new TerrainSampler(TestSeed, _terrain, _biomes, Allocator.Temp);
            try
            {
                int2 origin = MapTile.Origin(new int2(3, -2));
                foreach (int2 p in new[] { new int2(0, 0), new int2(17, 90), new int2(127, 127), new int2(64, 3) })
                {
                    TerrainColumn expected = sampler.SampleColumn(origin.x + p.x, origin.y + p.y);
                    MapColumn actual = columns[(p.y + 1) * MapTile.BorderedSize + p.x + 1];
                    Assert.AreEqual(expected.Height, actual.Height, $"height at {p}");
                    Assert.AreEqual(sampler.SurfaceBlock(expected), actual.Surface, $"surface at {p}");
                    Assert.AreEqual(expected.TreeHeight > 0, actual.Tree, $"tree at {p}");
                }
                for (int i = 0; i < pixels.Length; i++)
                    Assert.AreEqual(255, pixels[i].a, "every pixel is painted");
            }
            finally
            {
                sampler.Dispose(); pixels.Dispose(); columns.Dispose();
            }
        }

        static NativeArray<MapColumn> FlatColumns(int height, ushort surface)
        {
            var columns = new NativeArray<MapColumn>(MapTile.BorderedSize * MapTile.BorderedSize, Allocator.Temp);
            for (int i = 0; i < columns.Length; i++)
                columns[i] = new MapColumn { Height = height, Surface = surface, GrassTint = 1f, FoliageTint = new float3(0.3f, 0.6f, 0.2f) };
            return columns;
        }

        static float Brightness(Color32 c) => c.r + c.g + c.b;

        [Test]
        public void ContourLine_DarkensTheHighSideOfAStep()
        {
            NativeArray<MapColumn> columns = FlatColumns(MapInk.ContourStep * 4 - 1, BlockIds.Sand);
            int s = MapTile.BorderedSize;
            // Column (10, 10) and everything east of it climbs one block, over a contour line.
            for (int z = 0; z < s; z++)
            for (int x = 11; x < s; x++)
                columns[z * s + x] = new MapColumn { Height = MapInk.ContourStep * 4, Surface = BlockIds.Sand, GrassTint = 1f };
            Color32 flat = MapInk.Paint(columns, _blocks, 30, 10, new int2(30, 10));
            Color32 edge = MapInk.Paint(columns, _blocks, 10, 10, new int2(10, 10));
            Assert.Less(Brightness(edge), Brightness(flat) - 40f);
            columns.Dispose();
        }

        [Test]
        public void Trees_PaintDarkCanopies()
        {
            NativeArray<MapColumn> columns = FlatColumns(40, BlockIds.Grass);
            int s = MapTile.BorderedSize;
            MapColumn tree = columns[21 * s + 21];
            tree.Tree = true;
            columns[21 * s + 21] = tree;
            Color32 open = MapInk.Paint(columns, _blocks, 60, 60, new int2(60, 60));
            Color32 trunk = MapInk.Paint(columns, _blocks, 20, 20, new int2(20, 20));
            Color32 leaves = MapInk.Paint(columns, _blocks, 21, 20, new int2(21, 20));
            Assert.Less(Brightness(trunk), Brightness(open));
            Assert.Less(Brightness(leaves), Brightness(open));
            columns.Dispose();
        }

        [Test]
        public void Cache_PaintsAskedForTilesOnWorkerThreads_AndKeepsOnlyItsCapacity()
        {
            using var cache = new MapTileCache(TestSeed, _terrain, _biomes, _blocks, capacity: 2, maxJobsInFlight: 2);
            Assert.IsFalse(cache.TryGetTile(new int2(0, 0), out _), "not painted yet");

            Texture2D texture = null;
            for (int frame = 0; frame < 2000 && texture == null; frame++)
            {
                if (!cache.TryGetTile(new int2(0, 0), out texture))
                {
                    cache.TryGetTile(new int2(1, 0), out _);
                    cache.TryGetTile(new int2(2, 0), out _);
                    cache.Update();
                    Thread.Sleep(1);
                }
            }
            Assert.IsNotNull(texture, "tile painted");
            Assert.AreEqual(MapTile.Size, texture.width);

            for (int frame = 0; frame < 2000 && (cache.PendingCount > 0 || cache.CachedCount < 2); frame++)
            {
                cache.TryGetTile(new int2(1, 0), out _);
                cache.TryGetTile(new int2(2, 0), out _);
                cache.Update();
                Thread.Sleep(1);
            }
            cache.Update();
            Assert.LessOrEqual(cache.CachedCount, 2);
        }
    }
}
