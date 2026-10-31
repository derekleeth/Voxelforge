using System;
using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// What a creature can walk through, for pathfinding (Roadmap 5.1). Matches what <see cref="VoxelBodyMotor"/> lets
    /// the same body do, so a path never asks for a climb the motor can't make. See DECISIONS.md D-032.
    /// </summary>
    [Serializable]
    public struct NavAgentProfile
    {
        [Tooltip("Blocks of open space the body needs to stand in. A 1.8 m hero needs 2.")]
        public int Height;

        [Tooltip("Tallest ledge, in blocks, it walks up without jumping. The motor auto-steps 1.")]
        public int MaxStepUp;

        [Tooltip("Deepest drop, in blocks, it will walk off on purpose.")]
        public int MaxDrop;

        [Tooltip("Half the body's width, in blocks. Smoothed paths keep this far from walls.")]
        public float Radius;

        [Tooltip("Most cells one search may look at before giving up and taking the closest it found.")]
        public int MaxNodes;

        [Tooltip("Furthest a path may wander from its start along x or z, in blocks.")]
        public int MaxRange;

        public static NavAgentProfile Default => new NavAgentProfile
        {
            Height = 2,
            MaxStepUp = 1,
            MaxDrop = 3,
            Radius = 0.3f,
            MaxNodes = 4000,
            MaxRange = 64,
        };

        /// <summary>The <see cref="Default"/> limits, sized and stepped to fit a body moved with <paramref name="motor"/>.</summary>
        public static NavAgentProfile For(in MotorSettings motor)
        {
            NavAgentProfile profile = Default;
            profile.Height = math.max(1, (int)math.ceil(motor.HalfExtents.y * 2f - 0.01f));
            profile.MaxStepUp = math.max(0, (int)math.floor(motor.StepHeight + 0.01f));
            profile.Radius = math.max(motor.HalfExtents.x, motor.HalfExtents.z);
            return profile;
        }
    }
}
