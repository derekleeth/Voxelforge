using UnityEngine;

namespace Voxelforge.Voxels
{
    /// <summary>
    /// One painted surface a block face can show, e.g. "GrassTop" or "Stone". Blocks pick one for their top,
    /// sides and bottom on their <see cref="BlockDefinition"/>.
    /// <para>
    /// <b>The asset's name is the link to its art:</b> the Blender baker (Tools/Blender/bake_block_textures.py) writes
    /// <c>&lt;Name&gt;_Albedo/_Height/_Normal/_Mask.png</c> into Art/Textures/Blocks, and
    /// <i>Voxelforge &gt; Rebuild Block Textures</i> packs them into the texture arrays the block shader reads.
    /// It holds no texture references, so the 512 px source images never end up in game builds.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Voxelforge/Block Texture", fileName = "NewBlockTexture")]
    public sealed class BlockTexture : ScriptableObject
    {
    }
}
