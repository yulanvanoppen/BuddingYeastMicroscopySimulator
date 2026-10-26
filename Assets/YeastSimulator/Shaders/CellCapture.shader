// Renders each cell as a flat, exact ID for the label exports.
//   R = cell id & 255, G = cell id >> 8   (exact in an 8-bit UNorm target, read back on the CPU)
//   B = |N·V|, how directly the surface faces the camera (used for the brightfield shading)
// No lighting, no blending, no anti-aliasing: every pixel belongs to exactly one cell (or none).
Shader "YeastSim/CellCapture"
{
    Properties
    {
        _CellId ("Cell Id", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _CellId;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "CellCapture"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Cull Back
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float id = round(_CellId);
                float low = fmod(id, 256.0);
                float high = floor(id / 256.0);
                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float facing = saturate(abs(dot(normalize(input.normalWS), viewDir)));
                return float4(low / 255.0, high / 255.0, facing, 1.0);
            }
            ENDHLSL
        }

        // Needed when the URP renderer uses a depth prepass / depth priming.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half DepthFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
