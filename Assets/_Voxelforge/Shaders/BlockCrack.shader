// Cracks drawn over a block that's being mined (Roadmap 3.2, D-015). A cube a hair bigger than the block, multiplied
// over it: white texels leave the block as it is, dark texels darken it. _CrackStrip holds every crack stage side
// by side (stage 0 on the left); each material picks one with _Stage, so BlockCrackRenderer draws one instanced
// batch per stage.
Shader "Voxelforge/Block Crack"
{
    Properties
    {
        [NoScaleOffset] _CrackStrip ("Crack Stages (white = no crack)", 2D) = "white" {}
        _StageCount ("Stage Count", Float) = 8
        _Stage ("Stage", Float) = 0
        _Strength ("Strength", Range(0, 1)) = 0.85
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }

        Pass
        {
            Name "Crack"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend DstColor Zero
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _StageCount;
                float _Stage;
                half _Strength;
            CBUFFER_END

            TEXTURE2D(_CrackStrip);
            SAMPLER(sampler_CrackStrip);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.uv = float2((input.uv.x + _Stage) / max(_StageCount, 1.0), input.uv.y);
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half crack = SAMPLE_TEXTURE2D(_CrackStrip, sampler_CrackStrip, input.uv).r;
                half shade = lerp(1.0h, crack, _Strength);
                // Fade the cracks into the fog with the block (fogging towards white leaves the block untouched).
                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                shade = lerp(1.0h, shade, ComputeFogIntensity(input.fogFactor));
                #endif
                return half4(shade.xxx, 1.0h);
            }
            ENDHLSL
        }
    }
}
