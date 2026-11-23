// Signed-distance-field text. The atlas stores distance to the glyph outline (0.5 = on the outline), which keeps
// edges sharp whether the text is a metre away or across the room.
Shader "Spatail/Text"
{
    Properties
    {
        _MainTex ("SDF Atlas", 2D) = "black" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Opacity ("Opacity", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Offset -1, -1

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            half _Opacity;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float sd = tex2D(_MainTex, i.uv).r;
                float w = clamp(fwidth(sd), 1e-4, 0.25);
                fixed4 col = i.color;
                col.a *= smoothstep(0.5 - w, 0.5 + w, sd) * _Opacity;
                clip(col.a - 0.004);
                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
