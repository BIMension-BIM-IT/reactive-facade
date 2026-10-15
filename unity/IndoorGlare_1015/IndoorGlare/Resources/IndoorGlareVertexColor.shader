// 실내 눈부심 센서 쐐기 표시용: 정점 색 그대로, 조명 없이, 반투명, 양면. (반사광 GlareSim 셰이더와 이름이 달라요)
// 이름이 Resources인 폴더(IndoorGlare/Resources) 안에 있으면 WebGL 빌드에도 자동으로 들어가요.
// Built-in과 URP 모두에서 돼요(HDRP는 안 돼요).
Shader "IndoorGlare/VertexColorUnlit"
{
    Properties
    {
        _Opacity ("Opacity", Range(0, 1)) = 1
        // 4 = 보통(가려지면 안 보임), 8 = 항상 보임(벽 뒤 실내 센서도 보이게). 스크립트가 Show Through Walls로 바꿔요
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 8
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off
            Offset -1, -1

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _Opacity;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = i.color;
                c.a *= _Opacity;
                return c;
            }
            ENDCG
        }
    }
}
