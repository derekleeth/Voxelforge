using System;

namespace Voxelforge.Gameplay
{
    /// <summary>Frame-rate numbers over a stretch of recent frames.</summary>
    public readonly struct FrameStatsSnapshot
    {
        public readonly int FrameCount;
        public readonly float Seconds;
        public readonly float AverageFps;

        /// <summary>The frame rate of the slowest 1% of frames: what stutters feel like.</summary>
        public readonly float OnePercentLowFps;

        public readonly float WorstFrameMs;

        public FrameStatsSnapshot(int frameCount, float seconds, float averageFps, float onePercentLowFps, float worstFrameMs)
        {
            FrameCount = frameCount;
            Seconds = seconds;
            AverageFps = averageFps;
            OnePercentLowFps = onePercentLowFps;
            WorstFrameMs = worstFrameMs;
        }

        public override string ToString() =>
            $"{AverageFps:F0} FPS avg, {OnePercentLowFps:F0} FPS 1% low, worst frame {WorstFrameMs:F1} ms ({FrameCount} frames over {Seconds:F1} s)";
    }

    /// <summary>
    /// Remembers the most recent frame times in a ring buffer, so play-tests can report average and 1% low FPS
    /// instead of a single jumpy number.
    /// </summary>
    public sealed class FrameTimeStats
    {
        readonly float[] _frames;
        float[] _scratch;
        int _next;
        int _count;

        public FrameTimeStats(int capacity = 8192)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _frames = new float[capacity];
        }

        public int Count => _count;

        public void Add(float deltaTime)
        {
            if (!(deltaTime > 0f))
                return; // Paused or invalid frames would divide by zero.
            _frames[_next] = deltaTime;
            _next = (_next + 1) % _frames.Length;
            if (_count < _frames.Length)
                _count++;
        }

        public void Clear()
        {
            _next = 0;
            _count = 0;
        }

        /// <summary>Stats for the newest frames adding up to (at most) <paramref name="windowSeconds"/>.</summary>
        public FrameStatsSnapshot Snapshot(float windowSeconds)
        {
            _scratch ??= new float[_frames.Length];

            int frames = 0;
            float seconds = 0f;
            float worst = 0f;
            for (int i = 0; i < _count; i++)
            {
                float dt = _frames[(_next - 1 - i + _frames.Length) % _frames.Length];
                if (frames > 0 && seconds + dt > windowSeconds)
                    break;
                _scratch[frames++] = dt;
                seconds += dt;
                worst = Math.Max(worst, dt);
            }

            if (frames == 0)
                return default;

            // Slowest 1% of frames (at least one), averaged.
            Array.Sort(_scratch, 0, frames);
            int slowCount = Math.Max(1, (int)Math.Ceiling(frames * 0.01));
            float slowSeconds = 0f;
            for (int i = frames - slowCount; i < frames; i++)
                slowSeconds += _scratch[i];

            return new FrameStatsSnapshot(frames, seconds, frames / seconds, slowCount / slowSeconds, worst * 1000f);
        }
    }
}
