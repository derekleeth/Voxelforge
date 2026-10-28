using NUnit.Framework;
using Voxelforge.Gameplay;

namespace Voxelforge.Tests
{
    public class FrameTimeStatsTests
    {
        const float Tolerance = 0.01f;

        [Test]
        public void Empty_GivesAnEmptySnapshot()
        {
            FrameStatsSnapshot stats = new FrameTimeStats().Snapshot(10f);
            Assert.AreEqual(0, stats.FrameCount);
            Assert.AreEqual(0f, stats.AverageFps);
        }

        [Test]
        public void SteadyFrames_AverageAndOnePercentLowMatch()
        {
            var frames = new FrameTimeStats();
            for (int i = 0; i < 500; i++)
                frames.Add(1f / 125f);

            FrameStatsSnapshot stats = frames.Snapshot(10f);
            Assert.AreEqual(500, stats.FrameCount);
            Assert.AreEqual(125f, stats.AverageFps, Tolerance);
            Assert.AreEqual(125f, stats.OnePercentLowFps, Tolerance);
            Assert.AreEqual(8f, stats.WorstFrameMs, Tolerance);
        }

        [Test]
        public void OnePercentLow_AveragesTheSlowestFrames()
        {
            // 198 fast frames at 5 ms, plus two slow ones at 20 ms and 30 ms: the slowest 1% is those two.
            var frames = new FrameTimeStats();
            for (int i = 0; i < 99; i++)
                frames.Add(0.005f);
            frames.Add(0.020f);
            for (int i = 0; i < 99; i++)
                frames.Add(0.005f);
            frames.Add(0.030f);

            FrameStatsSnapshot stats = frames.Snapshot(10f);
            Assert.AreEqual(200, stats.FrameCount);
            Assert.AreEqual(200f / (198 * 0.005f + 0.05f), stats.AverageFps, Tolerance);
            Assert.AreEqual(40f, stats.OnePercentLowFps, Tolerance); // 2 frames / 0.05 s
            Assert.AreEqual(30f, stats.WorstFrameMs, Tolerance);
        }

        [Test]
        public void Window_OnlyCoversTheNewestFrames()
        {
            var frames = new FrameTimeStats();
            for (int i = 0; i < 100; i++)
                frames.Add(0.1f); // An old slow patch...
            for (int i = 0; i < 100; i++)
                frames.Add(0.01f); // ...then one second of 100 FPS.

            FrameStatsSnapshot stats = frames.Snapshot(1f);
            Assert.AreEqual(100, stats.FrameCount, 1);
            Assert.AreEqual(100f, stats.AverageFps, Tolerance);
            Assert.AreEqual(10f, stats.WorstFrameMs, Tolerance);
        }

        [Test]
        public void Window_AlwaysKeepsAtLeastTheNewestFrame()
        {
            var frames = new FrameTimeStats();
            frames.Add(2f);
            Assert.AreEqual(1, frames.Snapshot(1f).FrameCount);
        }

        [Test]
        public void RingBuffer_DropsTheOldestFramesWhenFull()
        {
            var frames = new FrameTimeStats(capacity: 4);
            frames.Add(0.5f);
            frames.Add(0.5f);
            for (int i = 0; i < 4; i++)
                frames.Add(0.01f);

            FrameStatsSnapshot stats = frames.Snapshot(100f);
            Assert.AreEqual(4, frames.Count);
            Assert.AreEqual(4, stats.FrameCount);
            Assert.AreEqual(100f, stats.AverageFps, Tolerance);
        }

        [Test]
        public void ZeroAndInvalidFrames_AreIgnored()
        {
            var frames = new FrameTimeStats();
            frames.Add(0f);
            frames.Add(-1f);
            frames.Add(float.NaN);
            Assert.AreEqual(0, frames.Count);
        }
    }
}
