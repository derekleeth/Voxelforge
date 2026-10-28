// Shared by Foliage.shader and DecorationCull.compute (Roadmap 2.6).
// Decorations are drawn with Graphics.RenderMeshIndirect: every instance lives in _Instances, and each camera's cull
// pass writes the indices it can see into _Visible, so SV_InstanceID -> _Visible -> _Instances.
#ifndef VOXELFORGE_DECORATION_INCLUDED
#define VOXELFORGE_DECORATION_INCLUDED

// Must match DecorationInstance in DecorationInfo.cs (32 bytes).
struct DecorationInstance
{
    float3 position; // World position of the bottom centre.
    float  yaw;      // Radians around +Y.
    float  scale;    // 0 = empty slot.
    uint   type;
    uint   tint;     // RGBA8, R in the low byte.
    uint   random;
};

float3 DecorationRotateY(float3 v, float yaw)
{
    float s, c;
    sincos(yaw, s, c);
    return float3(c * v.x + s * v.z, v.y, -s * v.x + c * v.z);
}

half3 DecorationUnpackTint(uint tint)
{
    return half3(tint & 255u, (tint >> 8) & 255u, (tint >> 16) & 255u) * (1.0 / 255.0);
}

#ifndef DECORATION_COMPUTE

StructuredBuffer<DecorationInstance> _Instances;
StructuredBuffer<uint> _Visible;

// Set per draw by DecorationRenderer (not in UnityPerMaterial: every camera and type gets its own values).
float3 _ViewerPosition; // The camera this draw was culled for. Also right in shadow passes, where the "camera" is the light.
float  _FadeStart;
float  _FadeEnd;

// Global wind, set every frame by DecorationRenderer. xy = direction on the ground (x, z), z = strength (metres), w = speed.
float4 _VF_Wind;

DecorationInstance LoadDecoration(uint instanceID)
{
    return _Instances[_Visible[instanceID]];
}

// Places a mesh vertex in the world and bends it in the wind. sway is painted into the mesh (vertex colour alpha):
// 0 where the plant is rooted, 1 at its tips. windScale lets stiff trees move less than grass.
float3 DecorationToWorld(float3 positionOS, half sway, DecorationInstance d, half windScale)
{
    float3 positionWS = DecorationRotateY(positionOS * d.scale, d.yaw) + d.position;

    // Two waves out of step, plus a phase per plant and per place, so a field ripples instead of moving as one.
    float phase = (d.random & 0xFFFFu) * (6.2831853 / 65536.0) + dot(d.position.xz, float2(0.13, 0.09));
    float t = _Time.y * _VF_Wind.w;
    float gust = sin(t + phase) * 0.65 + sin(t * 2.3 + phase * 1.7) * 0.35;
    float bend = sway * _VF_Wind.z * windScale * (0.55 + 0.45 * gust);
    positionWS.xz += _VF_Wind.xy * bend;
    positionWS.y -= bend * bend * 0.2; // Tips dip a little as they lean, so they don't stretch.
    return positionWS;
}

// 1 up close, falling to 0 between _FadeStart and _FadeEnd.
half DecorationFade(DecorationInstance d)
{
    float distanceToViewer = distance(d.position, _ViewerPosition);
    return saturate((_FadeEnd - distanceToViewer) / max(_FadeEnd - _FadeStart, 0.01));
}

// Screen-door fade: drops a growing share of pixels in a stable pattern, so it works in opaque and shadow passes.
void DecorationDitherClip(half fade, float4 positionCS)
{
    float2 pixel = floor(positionCS.xy);
    float dither = frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
    if (fade < 1.0) // Fully faded in keeps every pixel, even where the dither pattern is close to 1.
        clip(fade - dither - 0.0001);
}

#endif // DECORATION_COMPUTE
#endif // VOXELFORGE_DECORATION_INCLUDED
