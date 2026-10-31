using NUnit.Framework;
using Unity.Mathematics;
using Voxelforge.Gameplay;

namespace Voxelforge.Tests
{
    /// <summary>Roadmap 4.8: the World Map opens on the hero, follows them until panned, zooms in steps and recentres.</summary>
    public class WorldMapModelTests
    {
        [Test]
        public void Open_StartsOnTheHero_FollowingThem_AtDefaultZoom()
        {
            var model = new WorldMapModel();
            model.ZoomBy(+2);
            model.Open(new float2(-40f, 300f));
            Assert.AreEqual(new float2(-40f, 300f), model.Center);
            Assert.IsTrue(model.Following);
            Assert.AreEqual(WorldMapModel.DefaultZoom, model.Zoom);
        }

        [Test]
        public void Following_MovesWithTheHero()
        {
            var model = new WorldMapModel();
            model.Open(float2.zero);
            model.Tick(float2.zero, new float2(10f, 5f), 0.1f);
            Assert.AreEqual(new float2(10f, 5f), model.Center);
        }

        [Test]
        public void Panning_StopsFollowing_AndCoversTheSameScreenDistanceAtAnyZoom()
        {
            var model = new WorldMapModel();
            model.Open(float2.zero);
            model.Tick(new float2(1f, 0f), float2.zero, 0.5f);
            Assert.IsFalse(model.Following);
            float blocksAtDefault = model.Center.x;
            Assert.AreEqual(WorldMapModel.PanSpeed * 0.5f / model.UnitsPerBlock, blocksAtDefault, 1e-3f);

            model.Tick(float2.zero, new float2(999f, 999f), 0.5f);
            Assert.AreEqual(blocksAtDefault, model.Center.x, 1e-3f, "a panned map stays put when the hero moves");

            model.ZoomBy(+1);
            float before = model.Center.x;
            model.Tick(new float2(1f, 0f), float2.zero, 0.5f);
            Assert.AreEqual(blocksAtDefault * 0.5f, model.Center.x - before, 1e-3f, "zoomed in twice as far: half the blocks");
        }

        [Test]
        public void SmallPushes_DontPan()
        {
            var model = new WorldMapModel();
            model.Open(float2.zero);
            model.Tick(new float2(0.1f, 0.1f), float2.zero, 1f);
            Assert.IsTrue(model.Following);
            Assert.AreEqual(float2.zero, model.Center);
        }

        [Test]
        public void Zoom_StopsAtEachEnd()
        {
            var model = new WorldMapModel();
            while (model.ZoomBy(-1)) { }
            Assert.AreEqual(0, model.Zoom);
            Assert.IsFalse(model.ZoomBy(-1));
            while (model.ZoomBy(+1)) { }
            Assert.AreEqual(WorldMapModel.ZoomLevels.Length - 1, model.Zoom);
        }

        [Test]
        public void Recenter_GoesBackToTheHero_AndFollowsAgain()
        {
            var model = new WorldMapModel();
            model.Open(float2.zero);
            model.Tick(new float2(0f, -1f), float2.zero, 1f);
            model.Recenter(new float2(7f, 7f));
            Assert.IsTrue(model.Following);
            Assert.AreEqual(new float2(7f, 7f), model.Center);
        }
    }
}
