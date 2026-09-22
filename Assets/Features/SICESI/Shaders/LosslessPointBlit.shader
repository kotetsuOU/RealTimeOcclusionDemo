Shader "Hidden/SICESI/LosslessPointBlit"
{
    Properties
    {
        _BlitTexture ("Blit Texture", 2D) = "white" {}
        _FlipX ("Flip X", Float) = 0.0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        LOD 100
        ZTest Always ZWrite Off Cull Off Blend Off

        Pass
        {
            Name "LosslessPointBlitPass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            TEXTURE2D(_BlitTexture);
            float _FlipX;

            float4 Frag(Varyings input) : SV_Target
            {
                // SV_POSITION (positionCS.xy) はピクセル中心 (x + 0.5, y + 0.5)
                // 整数化 (キャスト) により 0, 1, 2, ... の整数ピクセル座標を厳密に取得
                int2 pixelCoord = int2(input.positionCS.xy);
                int screenW = (int)_ScreenParams.x;
                int screenH = (int)_ScreenParams.y;

                if (_FlipX > 0.5)
                {
                    pixelCoord.x = screenW - 1 - pixelCoord.x;
                }

                pixelCoord.x = clamp(pixelCoord.x, 0, screenW - 1);
                pixelCoord.y = clamp(pixelCoord.y, 0, screenH - 1);

                // Texture2D.Load による完全1:1整数画素読み出し (バイリニア補間・混色・ブレンド完全ゼロ)
                return _BlitTexture.Load(int3(pixelCoord, 0));
            }
            ENDHLSL
        }
    }
}
