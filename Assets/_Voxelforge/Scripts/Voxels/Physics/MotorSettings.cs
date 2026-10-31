using System;
using Unity.Mathematics;
using UnityEngine;

namespace Voxelforge.Voxels
{
    /// <summary>How a <see cref="VoxelBody"/> moves. Tweak these on the player prefab.</summary>
    [Serializable]
    public struct MotorSettings
    {
        [Tooltip("Walking speed in blocks per second.")]
        public float WalkSpeed;

        [Tooltip("How high a jump reaches, in blocks.")]
        public float JumpHeight;

        [Tooltip("Downward acceleration in blocks per second squared.")]
        public float Gravity;

        [Tooltip("Fastest fall speed, so long falls can't tunnel through floors.")]
        public float MaxFallSpeed;

        [Tooltip("Tallest step, in blocks, a walking body climbs or drops down without jumping or falling.")]
        public float StepHeight;

        [Tooltip("Half the body's size on each axis. 0.3 x 0.9 x 0.3 is a 0.6 m wide, 1.8 m tall hero.")]
        public float3 HalfExtents;

        public static MotorSettings Default => new MotorSettings
        {
            WalkSpeed = 5f,
            JumpHeight = 1.25f,
            Gravity = 25f,
            MaxFallSpeed = 50f,
            StepHeight = 1f,
            HalfExtents = new float3(0.3f, 0.9f, 0.3f),
        };

        /// <summary>The take-off speed that reaches <see cref="JumpHeight"/>: v = sqrt(2gh).</summary>
        public float JumpSpeed => math.sqrt(2f * Gravity * JumpHeight);
    }

    /// <summary>One tick of player intent, already turned into world space.</summary>
    public struct MotorInput
    {
        /// <summary>Wanted direction on the ground plane (x, z), length 0-1.</summary>
        public float2 Move;
        public bool Jump;

        /// <summary>
        /// How many times <see cref="MotorSettings.WalkSpeed"/> to move at, e.g. 1.6 while sprinting (Roadmap 4.8).
        /// 0 (the default) means 1, so code that never sets it walks as before.
        /// </summary>
        public float SpeedScale;

        public float Speed(in MotorSettings settings) => settings.WalkSpeed * (SpeedScale > 0f ? SpeedScale : 1f);
    }
}
