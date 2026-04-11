Shader "Custom/RsHandMeshVertexColor"
{
    Properties
    {
        [Toggle] _DoubleSided ("Double Sided", Float) = 1
        _UseVertexColor ("Use Vertex Color", Float) = 1
        _BaseColor ("Base Color", Color) = (0.945, 0.733, 0.576, 1)
        [HideInInspector] _UseProceduralBuffers ("Use Procedural Buffers", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        Cull Off

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _UseVertexColor;
            float4 _BaseColor;
            float _UseProceduralBuffers;
            float4x4 _CustomLocalToWorld;

            struct HandVertex
            {
                float3 pos;
                float3 col;
                float2 uv;
            };

            StructuredBuffer<HandVertex> _VertexBuffer;
            StructuredBuffer<int> _IndexBuffer;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 positionOS = input.positionOS.xyz;
                float4 color = input.color;

                if (_UseProceduralBuffers > 0.5)
                {
                    int index = _IndexBuffer[input.vertexID];
                    HandVertex v = _VertexBuffer[index];
                    positionOS = v.pos;
                    color = float4(v.col, 1.0);
                    
                    // Convert Local to World manually using the correct matrix
                    float3 positionWS = mul(_CustomLocalToWorld, float4(positionOS, 1.0)).xyz;
                    output.positionCS = TransformWorldToHClip(positionWS);
                }
                else
                {
                    output.positionCS = TransformObjectToHClip(positionOS);
                }
                output.color = color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half useVertexColor = _UseVertexColor;
                half3 baseColor = _BaseColor.rgb;
                half3 finalColor = lerp(baseColor, input.color.rgb, useVertexColor);
                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
        
        // DepthOnly pass for shadows/depth
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            
            ZWrite On
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _UseProceduralBuffers;
            float4x4 _CustomLocalToWorld;

            struct HandVertex
            {
                float3 pos;
                float3 col;
                float2 uv;
            };

            StructuredBuffer<HandVertex> _VertexBuffer;
            StructuredBuffer<int> _IndexBuffer;

            struct Attributes
            {
                float4 positionOS : POSITION;
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 positionOS = input.positionOS.xyz;
                if (_UseProceduralBuffers > 0.5)
                {
                    int index = _IndexBuffer[input.vertexID];
                    HandVertex v = _VertexBuffer[index];
                    positionOS = v.pos;
                    
                    float3 positionWS = mul(_CustomLocalToWorld, float4(positionOS, 1.0)).xyz;
                    output.positionCS = TransformWorldToHClip(positionWS);
                }
                else
                {
                    output.positionCS = TransformObjectToHClip(positionOS);
                }

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
