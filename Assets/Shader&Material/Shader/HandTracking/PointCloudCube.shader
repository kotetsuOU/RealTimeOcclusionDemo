Shader "Custom/PointCloudCube"
{
    Properties
    {
        _CubeSize("Cube Size", Float) = 0.002
        _Color("Point Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag

            #include "UnityCG.cginc"

            StructuredBuffer<float3> _Vertices;
            float _CubeSize;
            float4 _Color;

            struct v2g
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
            };

            struct g2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
            };

            // Vertex Shader
            v2g vert (uint id : SV_VertexID)
            {
                v2g o;
                o.pos = float4(_Vertices[id], 1.0);
                o.color = _Color;
                return o;
            }

            // Geometry Shader
            [maxvertexcount(36)] // 6 faces * 2 triangles * 3 vertices = 36
            void geom(point v2g p[1], inout TriangleStream<g2f> triStream)
            {
                if (p[0].color.a < 0.5) return;
                
                float3 center = p[0].pos.xyz;
                float halfSize = _CubeSize * 0.5;

                float3 v[8];
                v[0] = center + float3(-halfSize, -halfSize, -halfSize); // FBL
                v[1] = center + float3( halfSize, -halfSize, -halfSize); // FBR
                v[2] = center + float3( halfSize,  halfSize, -halfSize); // FTR
                v[3] = center + float3(-halfSize,  halfSize, -halfSize); // FTL
                v[4] = center + float3(-halfSize, -halfSize,  halfSize); // BBL
                v[5] = center + float3( halfSize, -halfSize,  halfSize); // BBR
                v[6] = center + float3( halfSize,  halfSize,  halfSize); // BTR
                v[7] = center + float3(-halfSize,  halfSize,  halfSize); // BTL

                g2f o;
                o.color = p[0].color;
                
                
                // Front face
                o.pos = mul(UNITY_MATRIX_VP, float4(v[0], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[1], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[2], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[0], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[2], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[3], 1.0)); triStream.Append(o);
                triStream.RestartStrip();

                // Back face
                o.pos = mul(UNITY_MATRIX_VP, float4(v[5], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[4], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[7], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[5], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[7], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[6], 1.0)); triStream.Append(o);
                triStream.RestartStrip();

                // Right face
                o.pos = mul(UNITY_MATRIX_VP, float4(v[1], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[5], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[6], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[1], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[6], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[2], 1.0)); triStream.Append(o);
                triStream.RestartStrip();

                // Left face
                o.pos = mul(UNITY_MATRIX_VP, float4(v[4], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[0], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[3], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[4], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[3], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[7], 1.0)); triStream.Append(o);
                triStream.RestartStrip();

                // Top face
                o.pos = mul(UNITY_MATRIX_VP, float4(v[3], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[2], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[6], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[3], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[6], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[7], 1.0)); triStream.Append(o);
                triStream.RestartStrip();

                // Bottom face
                o.pos = mul(UNITY_MATRIX_VP, float4(v[4], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[5], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[1], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[4], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[1], 1.0)); triStream.Append(o);
                o.pos = mul(UNITY_MATRIX_VP, float4(v[0], 1.0)); triStream.Append(o);
                triStream.RestartStrip();
            }

            // Fragment Shader
            fixed4 frag(g2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}