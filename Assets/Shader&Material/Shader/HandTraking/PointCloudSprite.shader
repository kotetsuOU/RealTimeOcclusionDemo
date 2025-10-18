Shader "Custom/PointCloudSprite"
{
    Properties
    {
        _PointSize("Point Size", Float) = 4.0
        _Color("Point Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            StructuredBuffer<float3> _Vertices;

            float _PointSize;
            float4 _Color;

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
                float  psize : PSIZE;
            };

            v2f vert (uint id : SV_VertexID)
            {
                v2f o;
                float3 worldPos = _Vertices[id];
                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                o.color = float4(1,1,1,1);
                o.psize = _PointSize;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                if (i.color.a < 0.5) discard;

                return i.color * _Color;
            }
            ENDCG
        }
    }
}