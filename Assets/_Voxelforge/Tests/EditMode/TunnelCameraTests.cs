using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Gameplay;
using Voxelforge.Voxels;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 3.7: the camera in tunnels and the classic split-screen.</summary>
    public class TunnelCameraTests
    {
        [Test]
        public void Fade_HidesCloseHeroes_AndWaitsLongerToShowThemAgain()
        {
            const float aside = 1f; // Well off to the side of the view, like over the shoulder.
            Assert.IsFalse(HeroCameraFade.ShouldHide(false, 3f, aside), "A camera at the normal distance shows the hero.");
            Assert.IsTrue(HeroCameraFade.ShouldHide(false, 0.5f, aside), "A camera squeezed against the hero hides them.");

            float between = (HeroCameraFade.HideDistance + HeroCameraFade.ShowDistance) * 0.5f;
            Assert.IsFalse(HeroCameraFade.ShouldHide(false, between, aside), "Not hidden yet, so don't start in the gap.");
            Assert.IsTrue(HeroCameraFade.ShouldHide(true, between, aside), "Already hidden: stay hidden in the gap, so it can't flicker.");
            Assert.IsFalse(HeroCameraFade.ShouldHide(true, HeroCameraFade.ShowDistance + 0.1f, aside));
        }

        [Test]
        public void Fade_HidesAHeroBlockingTheMiddleOfTheView_ButNotFromFarAway()
        {
            Assert.IsTrue(HeroCameraFade.ShouldHide(false, 1.8f, 0.1f), "Right in front of the camera in a 1-wide tunnel.");
            Assert.IsFalse(HeroCameraFade.ShouldHide(false, 1.8f, 0.8f), "Over the shoulder: off to the side, so shown.");
            Assert.IsFalse(HeroCameraFade.ShouldHide(false, 7f, 0f), "The normal camera always looks at the hero.");

            var camera = new float3(0f, 1.75f, -2f); // Level with the top body sample (the head).
            var forward = new float3(0f, 0f, 1f);
            Assert.Less(HeroCameraFade.DistanceOffCentre(camera, forward, float3.zero), 0.2f, "Looking straight at them.");
            Assert.AreEqual(0.7f, HeroCameraFade.DistanceOffCentre(camera + new float3(0.7f, 0f, 0f), forward, float3.zero), 1e-4f);
            Assert.IsTrue(float.IsPositiveInfinity(HeroCameraFade.DistanceOffCentre(camera, -forward, float3.zero)), "Behind the camera.");
        }

        [Test]
        public void Fade_MeasuresToTheBody_NotTheFeet()
        {
            var feet = new float3(10f, 20f, 10f);
            Assert.AreEqual(0f, HeroCameraFade.DistanceToBody(feet + new float3(0f, 1.2f, 0f), feet), 1e-5f, "Inside the chest.");
            Assert.AreEqual(0.4f, HeroCameraFade.DistanceToBody(feet + new float3(0.4f, 1.6f, 0f), feet), 1e-5f, "Beside the head.");
            Assert.Greater(HeroCameraFade.DistanceToBody(feet + new float3(0f, 4f, 0f), feet), 2f, "Far above the head.");
        }

        [Test]
        public void ShoulderShift_StopsShortOfAWall_AndGoesAllTheWayInTheOpen()
        {
            var blocks = new NativeArray<BlockInfo>(BlockIds.Stone + 1, Allocator.Persistent);
            blocks[BlockIds.Air] = new BlockInfo { Id = BlockIds.Air, Flags = BlockFlags.Registered | BlockFlags.Transparent };
            blocks[BlockIds.Stone] = new BlockInfo { Id = BlockIds.Stone, MaxDurability = 60, Flags = BlockFlags.Registered | BlockFlags.Solid };
            using var world = new VoxelWorld(blocks);
            world.CreateChunk(new ChunkCoord(0, 0, 0));
            // A 1-wide tunnel along z: walls at x = 4 and x = 6, the hero's head in the middle of x = 5.
            for (int z = 0; z < 10; z++)
            {
                world.SetVoxel(new int3(4, 5, z), new Voxel(BlockIds.Stone));
                world.SetVoxel(new int3(6, 5, z), new Voxel(BlockIds.Stone));
            }

            var head = new float3(5.5f, 5.5f, 5f);
            float right = PlayerCameraRig.ClearSideways(world, head, new float3(1f, 0f, 0f), PlayerCameraRig.ShoulderOffset);
            Assert.Less(right, PlayerCameraRig.ShoulderOffset, "The wall is half a block away, so the shift stops short.");
            Assert.LessOrEqual(head.x + right, 6f - 0.29f, "The pivot keeps a gap from the wall.");

            var open = new float3(5.5f, 15.5f, 5f);
            Assert.AreEqual(PlayerCameraRig.ShoulderOffset,
                PlayerCameraRig.ClearSideways(world, open, new float3(1f, 0f, 0f), PlayerCameraRig.ShoulderOffset), 1e-5f);
        }

        [Test]
        public void ClassicRect_PutsTwoPlayersSideBySide_AndKeepsTheGridForMore()
        {
            Assert.AreEqual(new Rect(0f, 0f, 0.5f, 1f), SplitScreenMath.ClassicRect(0, 2));
            Assert.AreEqual(new Rect(0.5f, 0f, 0.5f, 1f), SplitScreenMath.ClassicRect(1, 2));
            Assert.AreEqual(SplitScreenMath.GridRect(2, 3), SplitScreenMath.ClassicRect(2, 3));
            Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), SplitScreenMath.ClassicRect(0, 1));
        }
    }
}
