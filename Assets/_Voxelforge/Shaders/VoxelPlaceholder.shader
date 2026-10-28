// Placeholder chunk shader until the Roadmap 1.5 PBR block materials arrive.
// Colors each block with its vertex color (BlockDefinition.MapColor), lights it with the main
// light + shadows + ambient, and draws faint grid lines so you can still see individual blocks
// on big greedy-merged quads (mesh UVs are measured in blocks).
Shader "Voxelforge/Voxel Placeholder"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _GridStrength ("Block Grid Strength", Range(0, 1)) = 0.18
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // Every pass shares one material buffer so the SRP Batcher can batch all chunks together.
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            half _GridStrength;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                half4  color      : COLOR;
                half   fogFactor  : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.color = input.color * _Tint;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 direct = mainLight.color * (saturate(dot(normalWS, mainLight.direction))
                    * mainLight.shadowAttenuation * mainLight.distanceAttenuation);
                half3 ambient = SampleSH(normalWS);

                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(input.positionCS));
                    direct *= ao.directAmbientOcclusion;
                    ambient *= ao.indirectAmbientOcclusion;
                #endif

                // Darken a thin, anti-aliased line along each block edge.
                float2 toEdge = min(frac(input.uv), 1.0 - frac(input.uv));
                float2 lineWidth = fwidth(input.uv) * 1.5;
                float2 notLine = smoothstep(0.0, lineWidth, toEdge);
                half grid = 1.0 - (1.0 - min(notLine.x, notLine.y)) * _GridStrength;

                half3 color = input.color.rgb * (direct + ambient) * grid;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // Set by URP for the light currently rendering shadows.
            float3 _LightDirection;
            float3 _LightPosition;

            float4 ShadowVert(float3 positionOS : POSITION, float3 normalOS : NORMAL) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(positionOS);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);
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

            float4 DepthVert(float3 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS);
            }

            half DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        // Used by SSAO (enabled on the PC renderer) to read depth and normals.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
            };

            Varyings DepthNormalsVert(float3 positionOS : POSITION, float3 normalOS : NORMAL)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(positionOS);
                output.normalWS = TransformObjectToWorldNormal(normalOS);
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
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
