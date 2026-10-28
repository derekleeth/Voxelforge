using System;
using UnityEngine;
using Voxelforge.Voxels;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// How much a tool multiplies mining power for each <see cref="HarvestKind"/> (Roadmap 3.5). Bare hands are 1 for
    /// everything, so a pickaxe at 3 breaks stone three times as fast.
    /// </summary>
    [Serializable]
    public struct ToolPower
    {
        [Tooltip("Stone, ore and rock.")]
        [Min(0f)] public float pick;

        [Tooltip("Trees, logs and workbenches.")]
        [Min(0f)] public float axe;

        [Tooltip("Soft blocks: dirt, grass, sand, snow.")]
        [Min(0f)] public float any;

        public ToolPower(float pick, float axe, float any)
        {
            this.pick = pick;
            this.axe = axe;
            this.any = any;
        }

        /// <summary>Bare hands, or holding something that isn't a tool.</summary>
        public static ToolPower Hands => new ToolPower(1f, 1f, 1f);

        public float For(HarvestKind kind)
        {
            switch (kind)
            {
                case HarvestKind.Pick:
                    return pick;
                case HarvestKind.Axe:
                    return axe;
                default:
                    return any;
            }
        }
    }
}
