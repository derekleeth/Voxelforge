// Decoration shader (Roadmap 2.6): grass, flowers, bushes and trees drawn by DecorationRenderer with
// Graphics.RenderMeshIndirect. Each instance comes from a GPU buffer (see Decoration.hlsl), so there are no
// GameObjects. URP physically based lighting like the blocks, alpha cut-out, both sides drawn, wind sway and a
// dithered distance fade that also applies to shadows.
//
// Mesh contract (Tools/Blender/build_foliage.py): UV0 = albedo, vertex colour RGB = painted shading multiplier,
// vertex colour A = how much the vertex sways (0 at the root, 1 at the tips). Origin at the bottom centre, +Y up.
Shader "Voxelforge/Foliage"
{
    Properties
    {
        _BaseMap ("Albedo (RGB) + Cut-out (A)", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Cut-off", Range(0, 1)) = 0.5
        _Smoothness ("Smoothness", Range(0, 1)) = 0.1
        _NormalUp ("Normals Point Up", Range(0, 1)) = 0.5
        _BackFaceFlip ("Flip Back-Face Normals", Range(0, 1)) = 1
        _WindScale ("Wind Scale", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }
        Cull Off

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Decoration.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff;
            half _Smoothness;
            half _NormalUp;
            half _BackFaceFlip;
            half _WindScale;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        struct FoliageAttributes
        {
            float3 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            half4  color      : COLOR;
            float2 uv         : TEXCOORD0;
            uint   instanceID : SV_InstanceID;
        };

        half SampleCutout(float2 uv)
        {
            return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a * _BaseColor.a;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                half4  color      : TEXCOORD3; // rgb = painted shading x instance tint, a = distance fade.
                half   fogFactor  : TEXCOORD4;
            };

            Varyings Vert(FoliageAttributes input)
            {
                DecorationInstance d = LoadDecoration(input.instanceID);
                Varyings output;
                output.positionWS = DecorationToWorld(input.positionOS, input.color.a, d, _WindScale);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                float3 normalWS = DecorationRotateY(input.normalOS, d.yaw);
                output.normalWS = SafeNormalize(lerp(normalWS, float3(0, 1, 0), _NormalUp));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = half4(input.color.rgb * DecorationUnpackTint(d.tint), DecorationFade(d));
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                clip(albedo.a - _Cutoff);
                DecorationDitherClip(input.color.a, input.positionCS);

                // The back of a card is lit as if it faced the viewer: its sideways part is mirrored, its upward part
                // kept. Canopies set _BackFaceFlip to 0: their normals point out of the whole crown on both sides.
                // Never blend a normal toward its own opposite: halfway is a zero vector, normalize() gives NaN,
                // and bloom spreads every NaN pixel into a bright white orb.
                float3 normalWS = input.normalWS;
                if (!isFrontFace)
                    normalWS.xz = lerp(normalWS.xz, -normalWS.xz, _BackFaceFlip);
                normalWS = SafeNormalize(normalWS);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo.rgb * input.color.rgb;
                surface.alpha = 1.0;
                surface.smoothness = _Smoothness;
                surface.occlusion = 1.0;
                surface.normalTS = half3(0, 0, 1);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(normalWS);
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half   fade       : TEXCOORD1;
            };

            ShadowVaryings ShadowVert(FoliageAttributes input)
            {
                DecorationInstance d = LoadDecoration(input.instanceID);
                float3 positionWS = DecorationToWorld(input.positionOS, input.color.a, d, _WindScale);
                float3 normalWS = DecorationRotateY(input.normalOS, d.yaw);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                ShadowVaryings output;
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fade = DecorationFade(d);
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                clip(SampleCutout(input.uv) - _Cutoff);
                DecorationDitherClip(input.fade, input.positionCS);
                return 0;
            }
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

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half   fade       : TEXCOORD1;
            };

            DepthVaryings DepthVert(FoliageAttributes input)
            {
                DecorationInstance d = LoadDecoration(input.instanceID);
                DepthVaryings output;
                output.positionCS = TransformWorldToHClip(DecorationToWorld(input.positionOS, input.color.a, d, _WindScale));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fade = DecorationFade(d);
                return output;
            }

            half DepthFrag(DepthVaryings input) : SV_Target
            {
                clip(SampleCutout(input.uv) - _Cutoff);
                DecorationDitherClip(input.fade, input.positionCS);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
