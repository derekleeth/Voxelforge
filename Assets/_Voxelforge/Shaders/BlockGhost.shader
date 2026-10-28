// The see-through preview of the block a player is about to place (Roadmap 3.3, D-016). It draws a block item mesh
// (BlockItemMesh) with the real block textures, see-through, gently pulsing, with a rim in the player's team colour.
// _Invalid turns it red where the block isn't allowed (inside a character). Unlit and tiny, like Block Crack.
// _PROP_MODEL (Roadmap 3.5): the mesh is a prop model (a torch) coloured from a plain texture, _PropMap, instead.
Shader "Voxelforge/Block Ghost"
{
    Properties
    {
        [NoScaleOffset] _AlbedoHeightArray ("Albedo (RGB) + Height (A)", 2DArray) = "" {}
        [NoScaleOffset] _PropMap ("Prop Model Albedo (RGB) + Cut-out (A)", 2D) = "white" {}
        _BaseColor ("Team Colour", Color) = (1, 1, 1, 1)
        _InvalidColor ("Not Allowed Colour", Color) = (1, 0.15, 0.1, 1)
        _Invalid ("Not Allowed", Range(0, 1)) = 0
        _Alpha ("Opacity", Range(0, 1)) = 0.45
        _Pulse ("Pulse Amount", Range(0, 0.5)) = 0.12
        _PulseSpeed ("Pulse Speed", Float) = 4
        _RimPower ("Rim Sharpness", Range(0.5, 8)) = 2.5
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.8
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "Ghost"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile_local _ _PROP_MODEL

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _InvalidColor;
                half _Invalid;
                half _Alpha;
                half _Pulse;
                float _PulseSpeed;
                half _RimPower;
                half _RimStrength;
            CBUFFER_END

            TEXTURE2D_ARRAY(_AlbedoHeightArray);
            SAMPLER(sampler_AlbedoHeightArray);
            TEXTURE2D(_PropMap);
            SAMPLER(sampler_PropMap);

            // Same inputs as the chunk mesh (ChunkVertex): the texture layer rides in TEXCOORD1.
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float layer : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                nointerpolation float layer : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.layer = input.layer;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
            #if defined(_PROP_MODEL)
                half4 prop = SAMPLE_TEXTURE2D(_PropMap, sampler_PropMap, input.uv);
                clip(prop.a - 0.5h);
                half3 albedo = prop.rgb;
            #else
                half3 albedo = SAMPLE_TEXTURE2D_ARRAY(_AlbedoHeightArray, sampler_AlbedoHeightArray, input.uv, input.layer).rgb;
            #endif
                half4 tint = lerp(_BaseColor, _InvalidColor, _Invalid);
                // Brighten the block a little so it reads as a preview, and wash it red when it isn't allowed.
                half3 color = lerp(albedo * 1.15h, tint.rgb, 0.2h + 0.5h * _Invalid);

                half3 view = normalize(GetWorldSpaceViewDir(input.positionWS));
                half rim = pow(1.0h - saturate(dot(normalize(input.normalWS), view)), _RimPower) * _RimStrength;
                color += tint.rgb * rim;

                half pulse = 1.0h + _Pulse * sin(_Time.y * _PulseSpeed);
                half alpha = saturate((_Alpha + rim * 0.5h) * pulse);

                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
