using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    public class SoundMathTests
    {
        const float Tolerance = 1e-4f;
        static readonly float3 Right = new float3(1f, 0f, 0f);

        [Test]
        public void Gain_IsFullUpClose_SilentAtMaxDistance_AndFallsInBetween()
        {
            Assert.AreEqual(1f, SoundMath.Gain(0f, 2f, 32f), Tolerance);
            Assert.AreEqual(1f, SoundMath.Gain(2f, 2f, 32f), Tolerance);
            Assert.AreEqual(0f, SoundMath.Gain(32f, 2f, 32f), Tolerance);
            Assert.AreEqual(0f, SoundMath.Gain(100f, 2f, 32f), Tolerance);

            float previous = 1f;
            for (float d = 3f; d < 32f; d += 1f)
            {
                float gain = SoundMath.Gain(d, 2f, 32f);
                Assert.Less(gain, previous, $"at {d} m");
                previous = gain;
            }
        }

        [Test]
        public void FullScreenView_UsesTheWholeStereoField()
        {
            var view = new PlayerView { ScreenRect = new Rect(0f, 0f, 1f, 1f) };
            PanRegion region = SoundMath.RegionFor(view);
            Assert.AreEqual(0f, region.Center, Tolerance);
            Assert.AreEqual(1f, region.HalfWidth, Tolerance);
        }

        [Test]
        public void ClassicSplit_GivesEachPlayerTheirHalf()
        {
            PanRegion left = SoundMath.RegionFor(new PlayerView { ScreenRect = new Rect(0f, 0f, 0.5f, 1f) });
            PanRegion right = SoundMath.RegionFor(new PlayerView { ScreenRect = new Rect(0.5f, 0f, 0.5f, 1f) });
            Assert.AreEqual(-0.5f, left.Center, Tolerance);
            Assert.AreEqual(0.5f, right.Center, Tolerance);
            Assert.AreEqual(0.5f, left.HalfWidth, Tolerance);
        }

        [Test]
        public void GridQuarter_LeansToItsSide()
        {
            PanRegion topRight = SoundMath.RegionFor(new PlayerView { ScreenRect = new Rect(0.5f, 0.5f, 0.5f, 0.5f) });
            Assert.AreEqual(0.5f, topRight.Center, Tolerance);
        }

        [Test]
        public void VoronoiSplit_FollowsTheSideOfTheLine()
        {
            var normal = new float2(1f, 0f); // The second player is to the right of the first.
            PanRegion first = SoundMath.RegionFor(new PlayerView { ScreenRect = new Rect(0f, 0f, 1f, 1f), VoronoiNormal = normal, VoronoiSide = -1f });
            PanRegion second = SoundMath.RegionFor(new PlayerView { ScreenRect = new Rect(0f, 0f, 1f, 1f), VoronoiNormal = normal, VoronoiSide = 1f });
            Assert.AreEqual(-0.5f, first.Center, Tolerance);
            Assert.AreEqual(0.5f, second.Center, Tolerance);

            // Split top and bottom: both centred.
            PanRegion top = SoundMath.RegionFor(new PlayerView { VoronoiNormal = new float2(0f, 1f), VoronoiSide = 1f });
            Assert.AreEqual(0f, top.Center, Tolerance);
        }

        [Test]
        public void Pan_ASoundRightOfTheCamera_LandsRightOfTheRegionCentre()
        {
            var secondPlayer = new PanRegion(0.5f, 0.5f);
            float3 ear = new float3(10f, 65f, 10f);
            Assert.AreEqual(1f, SoundMath.Pan(secondPlayer, ear, Right, ear + new float3(5f, 0f, 0f)), Tolerance);
            Assert.AreEqual(0f, SoundMath.Pan(secondPlayer, ear, Right, ear - new float3(5f, 0f, 0f)), Tolerance);
            Assert.AreEqual(0.5f, SoundMath.Pan(secondPlayer, ear, Right, ear + new float3(0f, 0f, 5f)), Tolerance);
            Assert.AreEqual(0.5f, SoundMath.Pan(secondPlayer, ear, Right, ear), Tolerance);
        }

        [Test]
        public void HitPitch_RisesFromTheFirstToTheLastCrackStage()
        {
            Assert.AreEqual(0.9f, SoundMath.HitPitch(0, 0.9f, 1.35f), Tolerance);
            Assert.AreEqual(1.35f, SoundMath.HitPitch(BlockCrackRenderer.StageCount - 1, 0.9f, 1.35f), Tolerance);
            Assert.Less(SoundMath.HitPitch(3, 0.9f, 1.35f), SoundMath.HitPitch(4, 0.9f, 1.35f));
        }

        [Test]
        public void SoundBook_UnlistedBlocksGetTheDefaultSurface()
        {
            var fallback = ScriptableObject.CreateInstance<SurfaceSound>();
            var stone = ScriptableObject.CreateInstance<SurfaceSound>();
            BlockDefinition stoneBlock = BlockDefinition.Create(BlockIds.Stone, "Stone");
            SoundBook book = SoundBook.Create(fallback, new BlockSound { block = stoneBlock, surface = stone });
            try
            {
                Assert.AreSame(stone, book.SurfaceFor(BlockIds.Stone));
                Assert.AreSame(fallback, book.SurfaceFor(BlockIds.Dirt));
                Assert.AreSame(fallback, book.SurfaceFor(500));
            }
            finally
            {
                Object.DestroyImmediate(book);
                Object.DestroyImmediate(stoneBlock);
                Object.DestroyImmediate(stone);
                Object.DestroyImmediate(fallback);
            }
        }
    }
}
