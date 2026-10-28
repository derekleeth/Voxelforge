// Biome tint (Roadmap 2.6), shared by VoxelBlock.shader and Foliage.shader.
// Each biome has a grass and a foliage colour. It only goes on the green parts of a texture, so the dirt under a
// grass block's side, tree bark and flower petals keep their own colours.
#ifndef VOXELFORGE_BIOME_TINT_INCLUDED
#define VOXELFORGE_BIOME_TINT_INCLUDED

// 0 for anything not green (brown, sand, snow, grey), rising to 1 once green clearly leads red and blue.
half BiomeTintMask(half3 albedo)
{
    return smoothstep(0.0, 0.06, albedo.g - max(albedo.r, albedo.b));
}

half3 ApplyBiomeTint(half3 albedo, half3 tint)
{
    return albedo * lerp(half3(1, 1, 1), tint, BiomeTintMask(albedo));
}

#endif // VOXELFORGE_BIOME_TINT_INCLUDED
