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

            float _PointSize;
            float4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
                float  psize : PSIZE;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = mul(UNITY_MATRIX_VP, v.vertex);
                o.color = v.color;
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