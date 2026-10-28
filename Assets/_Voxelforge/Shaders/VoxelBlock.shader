// Block terrain shader (Roadmap 1.5). Physically based lighting from URP (sun + shadows, Forward+ point/spot lights
// with shadows, SSAO, fog, ambient) on hand-painted block textures with normal maps, parallax depth and glowing ore.
//
// Textures come from three Texture2DArrays built by Voxelforge > Rebuild Block Textures. Each vertex carries its
// face's array layer in UV1.x (-1 = no texture: the block's flat MapColor vertex color is used instead), and UVs are
// measured in blocks, so with Repeat wrapping every block shows one whole copy of its texture, even on merged quads.
//
// Vertex colour (Roadmap 2.6, GreedyMesher): RGB = biome tint for the green parts of textured faces (or the flat
// MapColor of untextured ones), A = corner ambient occlusion, 0 (tucked into a corner) to 1 (open).
Shader "Voxelforge/Voxel Block"
{
    Properties
    {
        [NoScaleOffset] _AlbedoHeightArray ("Albedo (RGB) + Height (A)", 2DArray) = "" {}
        [NoScaleOffset] _NormalArray ("Normal (RG)", 2DArray) = "" {}
        [NoScaleOffset] _MaskArray ("Occlusion (R) Smoothness (G) Emission (B)", 2DArray) = "" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _NormalStrength ("Normal Strength", Range(0, 2)) = 0.8
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 1
        _ParallaxDepth ("Parallax Depth (blocks)", Range(0, 0.2)) = 0.03
        _ParallaxFadeDistance ("Parallax Fade Distance", Range(4, 128)) = 32
        _EmissionStrength ("Emission Strength", Range(0, 20)) = 4
        _VertexAOStrength ("Corner Shading Strength", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "BiomeTint.hlsl"

        // Every pass shares one material buffer so the SRP Batcher can batch all chunks together.
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            half _NormalStrength;
            half _OcclusionStrength;
            float _ParallaxDepth;
            float _ParallaxFadeDistance;
            half _EmissionStrength;
            half _VertexAOStrength;
        CBUFFER_END

        TEXTURE2D_ARRAY(_AlbedoHeightArray);
        TEXTURE2D_ARRAY(_NormalArray);
        TEXTURE2D_ARRAY(_MaskArray);
        // One shared inline sampler for all three arrays: smooth (trilinear), tiling (repeat), sharp at grazing angles.
        SAMPLER(sampler_trilinear_repeat_aniso8);

        // Chunk faces are axis-aligned and the mesher lays UVs along fixed axes per facing (GreedyMesher.EmitQuad):
        // X faces: u = +Z, v = +Y.  Y faces: u = +X, v = +Z.  Z faces: u = +X, v = +Y.
        // So the tangent frame follows from the normal alone, with no tangents stored in the mesh.
        // Chunk objects are never rotated, so object and world axes match.
        void BlockTangentFrame(float3 normalWS, out float3 tangentWS, out float3 bitangentWS)
        {
            float3 n = abs(normalWS);
            if (n.x > 0.5)
            {
                tangentWS = float3(0, 0, 1);
                bitangentWS = float3(0, 1, 0);
            }
            else if (n.y > 0.5)
            {
                tangentWS = float3(1, 0, 0);
                bitangentWS = float3(0, 0, 1);
            }
            else
            {
                tangentWS = float3(1, 0, 0);
                bitangentWS = float3(0, 1, 0);
            }
        }

        // BC5 stores only X and Y; rebuild Z.
        half3 SampleBlockNormalTS(float2 uv, float layer)
        {
            half2 xy = SAMPLE_TEXTURE2D_ARRAY(_NormalArray, sampler_trilinear_repeat_aniso8, uv, layer).rg * 2.0 - 1.0;
            xy *= _NormalStrength;
            return half3(xy, sqrt(saturate(1.0 - dot(xy, xy))));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            // GPU Resident Drawer (D-010): chunks are drawn as DOTS instances, so every pass needs these.
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                float  layer      : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                nointerpolation float layer : TEXCOORD3;
                half   fogFactor  : TEXCOORD4;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID // Light layers read the renderer's rendering layer in the fragment.
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.layer = input.layer;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                output.color = input.color;
                return output;
            }

            // Parallax occlusion mapping: marches the view ray down into the height map so bumps and cracks
            // look carved in, not painted on. Uses the original UV gradients so mip selection stays stable.
            float2 ParallaxOcclusion(float2 uv, float layer, float3 viewTS, float depth, float2 dx, float2 dy)
            {
                int steps = (int)lerp(16.0, 6.0, saturate(viewTS.z));
                float stepDepth = 1.0 / steps;
                // Fade out at grazing angles, where the effect would smear the texture sideways.
                depth *= saturate(viewTS.z * 2.0);
                float2 stepUV = viewTS.xy / max(viewTS.z, 0.4) * depth / steps;

                float rayDepth = 0.0;
                float2 current = uv;
                float surfaceDepth = 1.0 - SAMPLE_TEXTURE2D_ARRAY_GRAD(_AlbedoHeightArray, sampler_trilinear_repeat_aniso8, current, layer, dx, dy).a;
                [loop]
                for (int i = 0; i < steps && rayDepth < surfaceDepth; i++)
                {
                    current -= stepUV;
                    rayDepth += stepDepth;
                    surfaceDepth = 1.0 - SAMPLE_TEXTURE2D_ARRAY_GRAD(_AlbedoHeightArray, sampler_trilinear_repeat_aniso8, current, layer, dx, dy).a;
                }

                // Blend between the last two samples for a smooth result instead of visible steps.
                float2 previous = current + stepUV;
                float after = surfaceDepth - rayDepth;
                float before = (1.0 - SAMPLE_TEXTURE2D_ARRAY_GRAD(_AlbedoHeightArray, sampler_trilinear_repeat_aniso8, previous, layer, dx, dy).a)
                    - rayDepth + stepDepth;
                float weight = after / (after - before + 1e-5);
                return lerp(current, previous, saturate(weight));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 normalWS = normalize(input.normalWS);
                float3 tangentWS, bitangentWS;
                BlockTangentFrame(normalWS, tangentWS, bitangentWS);
                half3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                SurfaceData surface = (SurfaceData)0;
                surface.alpha = 1.0;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1.0;

                float layer = input.layer;
                if (layer < 0.0)
                {
                    surface.albedo = input.color.rgb * _Tint.rgb;
                    surface.smoothness = 0.1;
                }
                else
                {
                    float2 uv = input.uv;
                    float fade = saturate(1.0 - distance(input.positionWS, GetCameraPositionWS()) / _ParallaxFadeDistance);
                    if (fade > 0.0 && _ParallaxDepth > 0.0)
                    {
                        float3 viewTS = float3(dot(viewWS, tangentWS), dot(viewWS, bitangentWS), dot(viewWS, normalWS));
                        uv = ParallaxOcclusion(uv, layer, viewTS, _ParallaxDepth * fade, ddx(input.uv), ddy(input.uv));
                    }

                    half4 albedoHeight = SAMPLE_TEXTURE2D_ARRAY(_AlbedoHeightArray, sampler_trilinear_repeat_aniso8, uv, layer);
                    half4 mask = SAMPLE_TEXTURE2D_ARRAY(_MaskArray, sampler_trilinear_repeat_aniso8, uv, layer);
                    surface.albedo = ApplyBiomeTint(albedoHeight.rgb, input.color.rgb) * _Tint.rgb;
                    surface.normalTS = SampleBlockNormalTS(uv, layer);
                    surface.smoothness = mask.g;
                    surface.occlusion = lerp(1.0, mask.r, _OcclusionStrength);
                    surface.emission = albedoHeight.rgb * mask.b * _EmissionStrength;
                }

                // Corner shading darkens the surface itself, not just ambient light, for the classic soft-block look.
                surface.albedo *= 1.0 - _VertexAOStrength * (1.0 - input.color.a);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.tangentToWorld = half3x3(tangentWS, bitangentWS, normalWS);
                inputData.normalWS = normalize(TransformTangentToWorld(surface.normalTS, inputData.tangentToWorld));
                inputData.viewDirectionWS = viewWS;
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // Set by URP for the light currently rendering shadows.
            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 ShadowVert(ShadowAttributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
            }

            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            struct DepthAttributes
            {
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 DepthVert(DepthAttributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformObjectToHClip(input.positionOS);
            }

            half DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        // Used by SSAO (enabled on the PC renderer). Writes normal-mapped normals so SSAO sees surface detail.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float  layer      : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                nointerpolation float layer : TEXCOORD2;
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.layer = input.layer;
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                if (input.layer >= 0.0)
                {
                    float3 tangentWS, bitangentWS;
                    BlockTangentFrame(normalWS, tangentWS, bitangentWS);
                    half3 normalTS = SampleBlockNormalTS(input.uv, input.layer);
                    normalWS = normalize(normalTS.x * tangentWS + normalTS.y * bitangentWS + normalTS.z * normalWS);
                }

                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                    float2 remapped = saturate(octNormalWS * 0.5 + 0.5);
                    return half4(PackFloat2To888(remapped), 0.0);
                #else
                    return half4(normalWS, 0.0);
                #endif
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
