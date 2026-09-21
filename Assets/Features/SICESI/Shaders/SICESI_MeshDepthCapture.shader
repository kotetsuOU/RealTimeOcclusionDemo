Shader "Hidden/SICESI/MeshDepthCapture"
{
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "MeshDepthCapturePass"
            ZWrite On
            ZTest GEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                // SV_POSITION.z はスクリーン/NDC空間深度
                float depthNDC = input.positionCS.z;

                #if UNITY_REVERSED_Z
                // Reversed-Z (DirectX): 1.0=近, 0.0=遠
                float depth01 = depthNDC;
                #else
                // Standard Z (OpenGL): 0.0=近, 1.0=遠 -> 1.0=近, 0.0=遠 に統一
                float depth01 = 1.0 - depthNDC;
                #endif

                return float4(depth01, depth01, depth01, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
