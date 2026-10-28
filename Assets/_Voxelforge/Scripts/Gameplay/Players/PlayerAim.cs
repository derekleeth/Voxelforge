using System;
using Unity.Mathematics;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// Which block a player is aiming at (Roadmap 3.1/3.2, D-014/D-015): a voxel ray from the hero's eyes in the
    /// direction their camera faces. The owning PC uses it for the outline and the reticle, and the host uses the
    /// same settings with the controls it received to decide what gets mined, so the host never trusts a block
    /// position sent by a PC. One per hero, on <see cref="PlayerController.Aim"/>.
    /// </summary>
    [Serializable]
    public sealed class PlayerAim
    {
        [Tooltip("How far the aim ray reaches from the hero's eyes, in metres.")]
        [SerializeField, Range(1f, 8f)] float reach = 5f;

        [Tooltip("Height of the hero's eyes above their feet, where the aim ray starts.")]
        [SerializeField, Range(0.5f, 2f)] float eyeHeight = 1.5f;

        [Tooltip("Aim pitch = camera pitch x this + offset. The orbit camera only looks 10-70 degrees down, so this " +
                 "stretches it to reach walls and ceilings as well as the floor.")]
        [SerializeField, Range(0.5f, 3f)] float pitchScale = 1.5f;

        [Tooltip("Degrees added to the scaled camera pitch. Negative aims higher.")]
        [SerializeField, Range(-60f, 30f)] float pitchOffset = -30f;

        [Tooltip("How far from the hero's chest the host lets a block be edited, in metres.")]
        [SerializeField, Min(1f)] float maxReach = 8f;

        const float ChestHeight = 1.2f;

        public float Reach => reach;

        public float3 Eyes(float3 feet) => feet + new float3(0f, eyeHeight, 0f);

        public float3 Direction(PlayerInputFrame input)
        {
            float pitch = math.clamp(input.ViewPitch * pitchScale + pitchOffset, -89f, 89f);
            return Quaternion.Euler(pitch, input.ViewYaw, 0f) * Vector3.forward;
        }

        /// <summary>The solid block a hero standing at <paramref name="feet"/> aims at with <paramref name="input"/>.</summary>
        public bool TryAim(VoxelWorld world, float3 feet, PlayerInputFrame input, out VoxelHit hit)
        {
            hit = default;
            return world != null && VoxelQueries.Raycast(world, Eyes(feet), Direction(input), reach, out hit);
        }

        /// <summary>Host check: is <paramref name="block"/> close enough to a hero standing at <paramref name="feet"/> to edit?</summary>
        public bool InReach(float3 feet, int3 block) =>
            math.distance(feet + new float3(0f, ChestHeight, 0f), (float3)block + 0.5f) <= maxReach;
    }
}
