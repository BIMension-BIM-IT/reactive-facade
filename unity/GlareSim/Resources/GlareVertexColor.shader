// 반사광 히트맵·광선 표시용: 정점 색 그대로, 조명 없이, 반투명, 양면.
// 이름이 Resources인 폴더(GlareSim/Resources) 안에 있으면 WebGL 빌드에도 자동으로 들어가요.
// Built-in과 URP 모두에서 돼요(HDRP는 안 돼요).
Shader "Glare/VertexColorUnlit"
{
    Properties
    {
        _Opacity ("Opacity", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
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
