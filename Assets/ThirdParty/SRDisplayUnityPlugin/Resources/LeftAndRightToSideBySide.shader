Shader "Custom/LeftAndRightToSideBySide" {
  Properties {
    _MainTex("Left", 2D) = "white" {}
    _RightTex("Right", 2D) = "white" {}
    [Toggle] _FlipX("Flip RenderTexture X", Float) = 0
    [Toggle] _SwapEyes("Swap Left and Right Eyes", Float) = 0
  }

  CGINCLUDE

#include "UnityCG.cginc"

  struct appdata
  {
      float4 vertex : POSITION;
      float2 uv : TEXCOORD0;
  };

  struct vert_to_frag {
    float4 vertex : POSITION;
    float2 uv : TEXCOORD0;
  };

  sampler2D _MainTex;
  sampler2D _RightTex;
  float _FlipX;
  float _SwapEyes;

  vert_to_frag vert(appdata v) {
    vert_to_frag output;
    output.uv = v.uv;
    output.vertex = UnityObjectToClipPos(v.vertex);
    return output;
  }

  fixed4 frag_left_and_right_to_side_by_side(vert_to_frag input) : SV_Target {
    bool isLeftDisplay = (input.uv.x < 0.5);

    float rawUvX = isLeftDisplay ? (input.uv.x * 2.0) : ((input.uv.x - 0.5) * 2.0);

    if (_FlipX > 0.5) {
      rawUvX = 1.0 - rawUvX;
    }

    fixed2 uv = fixed2(rawUvX, input.uv.y);

    bool useLeftTex = isLeftDisplay;
    if (_SwapEyes > 0.5) {
      useLeftTex = !useLeftTex;
    }

    return useLeftTex ? tex2D(_MainTex, uv) : tex2D(_RightTex, uv);
  }

  ENDCG

  SubShader {
    Blend Off ZTest Always ZWrite Off Cull Off Lighting Off

    Pass {
      CGPROGRAM
#pragma vertex vert
#pragma fragment frag_left_and_right_to_side_by_side
      ENDCG
    }
  }
}
