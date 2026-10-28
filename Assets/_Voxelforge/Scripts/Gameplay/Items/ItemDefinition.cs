using UnityEngine;

namespace Voxelforge.Gameplay
{
    /// <summary>
    /// One item that isn't a plain block (Roadmap 3.5): a tool, or the look of a prop block such as a torch when it's
    /// held or dropped. Create one with Assets &gt; Create &gt; Voxelforge &gt; Item Definition, then add it to the
    /// <see cref="ItemRegistry"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Item Definition", fileName = "NewItem")]
    public sealed class ItemDefinition : ScriptableObject
    {
        [Tooltip("Permanent ID saved in bags and sent over the network. Never change or reuse one. Tools and other items " +
                 "start at 1000 (ItemIds); a block's own ID gives that block a model when held or dropped (torches).")]
        [SerializeField] ushort id = ItemIds.FirstNonBlock;

        [SerializeField] string displayName;

        [Header("Look")]
        [Tooltip("The model. Its origin is where the hand grips it (tools) or its bottom centre (props); +Y is up.")]
        [SerializeField] Mesh mesh;

        [Tooltip("A normal lit material, for the hand and the ground.")]
        [SerializeField] Material material;

        [Tooltip("Where the model sits in the hero's right hand, in the hand bone's space.")]
        [SerializeField] Vector3 heldPosition;

        [SerializeField] Vector3 heldRotation;

        [SerializeField, Min(0.01f)] float heldScale = 1f;

        [Tooltip("How big a dropped one is, compared with a dropped block.")]
        [SerializeField, Min(0.1f)] float dropSize = 2f;

        [Header("Tool")]
        [Tooltip("Mining power multipliers while it's held. All 0 = not a tool (bare-hands power).")]
        [SerializeField] ToolPower toolPower;

        [Header("Ranged")]
        [Tooltip("What it shoots while Mine is held (Roadmap 3.6). Damage 0 = it doesn't shoot.")]
        [SerializeField] ProjectileStats projectile;

        public ushort Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public Mesh Mesh => mesh;
        public Material Material => material;
        public Vector3 HeldPosition => heldPosition;
        public Quaternion HeldRotation => Quaternion.Euler(heldRotation);
        public float HeldScale => heldScale;
        public float DropSize => dropSize;

        /// <summary>True if any of its multipliers is set.</summary>
        public bool IsTool => toolPower.pick > 0f || toolPower.axe > 0f || toolPower.any > 0f;

        /// <summary>Its multipliers, or <see cref="ToolPower.Hands"/> if it isn't a tool.</summary>
        public ToolPower ToolPower => IsTool ? toolPower : ToolPower.Hands;

        /// <summary>True if it shoots (the M.G.R.S.). Holding it, Mine fires instead of mining.</summary>
        public bool IsRanged => projectile.IsRanged;

        public ProjectileStats Projectile => projectile;

        /// <summary>Builds a definition in code, for tests and tools.</summary>
        internal static ItemDefinition Create(ushort id, string displayName, ToolPower toolPower = default,
            ProjectileStats projectile = default)
        {
            var definition = CreateInstance<ItemDefinition>();
            definition.name = displayName;
            definition.id = id;
            definition.displayName = displayName;
            definition.toolPower = toolPower;
            definition.projectile = projectile;
            return definition;
        }
    }
}
