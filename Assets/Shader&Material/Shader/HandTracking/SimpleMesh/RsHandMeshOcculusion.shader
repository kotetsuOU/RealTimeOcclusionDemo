Shader "Custom/HandMeshOcclusion"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        
        Pass
        {
            Name "HandMeshWithOcclusion"
            Tags { "LightMode"="UniversalForward" }
            
            ZWrite On
            ZTest LEqual
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            
            struct Vertex
            {
                float3 pos;
                float3 col;
                float2 uv;
            };
            
            StructuredBuffer<Vertex> _VertexBuffer;
            float4 _Color;
            
            struct v2f
            {
                float4 positionCS : SV_POSITION;
                float3 color : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };
            
            v2f vert(uint vertexID : SV_VertexID)
            {
                v2f o;
                Vertex v = _VertexBuffer[vertexID];
                
                o.positionCS = TransformWorldToHClip(v.pos);
                o.color = v.col;
                o.screenPos = ComputeScreenPos(o.positionCS);
                
                return o;
            }
            
            half4 frag(v2f i) : SV_Target
            {
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                
                float sceneDepth = SampleSceneDepth(screenUV);
                float sceneLinearDepth = LinearEyeDepth(sceneDepth, _ZBufferParams);
                
                float handDepth = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                
                if (handDepth > sceneLinearDepth + 0.001)
                {
                    discard;
                }
                
                return half4(i.color * _Color.rgb, 1.0);
            }
            ENDHLSL
        }
    }
}