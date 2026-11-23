// Rounded rectangle with a border, evaluated analytically per pixel so pills, cards, discs and rings stay crisp
// at any size or distance. Drawn on a unit quad; _Size carries the real dimensions in metres.
Shader "Spatail/Panel"
{
    Properties
    {
        _Color ("Fill", Color) = (1, 1, 1, 1)
        _BorderColor ("Border", Color) = (0.42, 0.36, 0.9, 1)
        _Size ("Size (m)", Vector) = (0.2, 0.1, 0, 0)
        _Radius ("Corner Radius (m)", Float) = 0.02
        _Border ("Border Width (m)", Float) = 0.002
        _Opacity ("Opacity", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        ZWrite On
        Cull Off

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            fixed4 _BorderColor;
            float4 _Size;
            float _Radius;
            float _Border;
            half _Opacity;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float roundBox(float2 p, float2 halfSize, float r)
            {
                float2 q = abs(p) - halfSize + r;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 halfSize = _Size.xy * 0.5;
                float2 p = (i.uv - 0.5) * _Size.xy;
                float r = min(_Radius, min(halfSize.x, halfSize.y));
                float d = roundBox(p, halfSize, r);
                float aa = max(fwidth(d), 1e-5);
                float shape = 1.0 - smoothstep(-aa, aa, d);
                float border = _Border > 0.0 ? smoothstep(-_Border - aa, -_Border + aa, d) : 0.0;
                fixed4 col = lerp(_Color, _BorderColor, border);
                col.a *= shape * _Opacity;
                clip(col.a - 0.004);
                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
