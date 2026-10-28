// Voronoi split-screen composite (Roadmap 2.3). Draws on a full-screen quad seen only by the composite camera, an
// overlay camera stacked on top of the first player's camera. The first player's view is already on screen, so this
// only paints the second player's view over their side of the split line, plus a soft divider line.
// SplitScreenDirector sets every property each frame; SplitScreenMath.SideOfSplit does the same side test in C#.
Shader "Voxelforge/Split Screen Composite"
{
    Properties
    {
        [NoScaleOffset] _ViewB ("View B (second player)", 2D) = "black" {}
        _SplitNormal ("Split Normal (xy, screen direction A to B)", Vector) = (1, 0, 0, 0)
        _DividerAlpha ("Divider Alpha", Range(0, 1)) = 1
        _DividerWidth ("Divider Width (pixels)", Float) = 4
        _DividerColor ("Divider Color", Color) = (0.05, 0.04, 0.03, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "Composite"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_ViewB); // Sampled with sampler_LinearClamp from the core library.

            CBUFFER_START(UnityPerMaterial)
                float4 _SplitNormal;
                float _DividerAlpha;
                float _DividerWidth;
                half4 _DividerColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Same units on both axes (screen heights), so the line is a true perpendicular on screen.
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float side = dot((input.uv - 0.5) * float2(aspect, 1.0), _SplitNormal.xy);

                // Divider: a line _DividerWidth pixels wide with a one-pixel soft edge.
                float pixelsFromLine = abs(side) * _ScreenParams.y;
                float halfWidth = _DividerWidth * 0.5;
                float divider = (1.0 - smoothstep(halfWidth - 0.75, halfWidth + 0.75, pixelsFromLine)) * _DividerAlpha * _DividerColor.a;

                // The first player's side, away from the divider: leave their camera's picture showing.
                clip(side > 0.0 || divider > 0.0 ? 1.0 : -1.0);

                // Next to the line on the first player's side, blend the divider over what's already on screen.
                half4 color = side > 0.0 ? SAMPLE_TEXTURE2D(_ViewB, sampler_LinearClamp, input.uv) : half4(_DividerColor.rgb, divider);
                if (side > 0.0)
                    color = half4(lerp(color.rgb, _DividerColor.rgb, divider), 1.0);
                return color;
            }
            ENDHLSL
        }
    }
}
