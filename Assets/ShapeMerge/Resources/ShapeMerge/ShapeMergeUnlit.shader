// Everything in Shape Merge is drawn with this one shader: vertex-coloured triangles with
// premultiplied alpha, so a single mesh can mix normal blending (alpha > 0) and additive
// glow (alpha == 0). Colours may exceed 1 to feed bloom. Soft sprites pass UVs in [-1, 1]
// for a round falloff; solid geometry leaves UVs at 0.
Shader "ShapeMerge/Unlit Premultiplied"
{
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" "PreviewType" = "Plane" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float falloff = saturate(1.0 - dot(input.uv, input.uv));
                return input.color * (falloff * falloff);
            }
            ENDHLSL
        }
    }
}
