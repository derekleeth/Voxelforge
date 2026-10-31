using NUnit.Framework;
using Unity.Mathematics;
using Voxelforge.Gameplay;

namespace Voxelforge.Tests
{
    /// <summary>Sprinting (Roadmap 4.8): the owner's toggle or hold, and the host's speed for it.</summary>
    public class SprintTests
    {
        [Test]
        public void Hold_SprintsOnlyWhileHeldAndMoving()
        {
            var latch = new SprintLatch();
            Assert.IsTrue(latch.Update(pressed: true, toggle: false, moveLength: 1f));
            Assert.IsFalse(latch.Update(pressed: true, toggle: false, moveLength: 0f), "standing still");
            Assert.IsTrue(latch.Update(pressed: true, toggle: false, moveLength: 1f));
            Assert.IsFalse(latch.Update(pressed: false, toggle: false, moveLength: 1f), "let go");
        }

        [Test]
        public void Toggle_OneClickKeepsSprintingAfterLettingGo()
        {
            var latch = new SprintLatch();
            Assert.IsTrue(latch.Update(true, true, 1f));
            Assert.IsTrue(latch.Update(false, true, 1f));
            Assert.IsTrue(latch.Update(false, true, 0.8f));
        }

        [Test]
        public void Toggle_SecondClickStops()
        {
            var latch = new SprintLatch();
            latch.Update(true, true, 1f);
            latch.Update(false, true, 1f);
            Assert.IsFalse(latch.Update(true, true, 1f));
            Assert.IsFalse(latch.Update(true, true, 1f), "holding the click doesn't flip it back");
        }

        [Test]
        public void Toggle_StoppingEndsTheSprint()
        {
            var latch = new SprintLatch();
            latch.Update(true, true, 1f);
            latch.Update(false, true, 1f);
            Assert.IsFalse(latch.Update(false, true, 0f));
            Assert.IsFalse(latch.Update(false, true, 1f), "walking off again walks");
        }

        [Test]
        public void Toggle_ResetForgetsTheSprint()
        {
            var latch = new SprintLatch();
            latch.Update(true, true, 1f);
            latch.Reset();
            Assert.IsFalse(latch.Update(false, true, 1f));
        }

        [Test]
        public void Host_SprintsOnlyWhenMoving()
        {
            var moving = new PlayerInputFrame { Move = new float2(0f, 1f), Buttons = PlayerButtons.Sprint };
            var still = new PlayerInputFrame { Move = float2.zero, Buttons = PlayerButtons.Sprint };
            var walking = new PlayerInputFrame { Move = new float2(0f, 1f) };
            Assert.AreEqual(1.6f, PlayerController.SprintScale(moving, 1.6f), 1e-5f);
            Assert.AreEqual(1f, PlayerController.SprintScale(still, 1.6f), 1e-5f);
            Assert.AreEqual(1f, PlayerController.SprintScale(walking, 1.6f), 1e-5f);
        }
    }
}
