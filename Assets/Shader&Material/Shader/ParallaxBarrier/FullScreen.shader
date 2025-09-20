Shader "Fullscreen"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "FullscreenPass"
            ZWrite Off Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 _Color;

            struct vf2
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            vf2 Vert(uint vertexID : SV_VertexID)
            {
                vf2 o;
                float2 uv = float2((vertexID << 1) & 2, vertexID & 2);
                o.positionCS = float4(uv * 2.0f - 1.0f, 0.0f, 1.0f);
                o.uv = uv;
                return o;
            }

            half4 Frag(vf2 input) : SV_Target
            {
                if (input.uv.x < 0.5)
                    return _Color;
                return half4(0, 0, 0, 1);
            }

            ENDHLSL
        }
    }
}

