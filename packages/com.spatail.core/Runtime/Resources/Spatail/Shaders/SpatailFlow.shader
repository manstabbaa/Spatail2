// Moving chevrons along a tube. UV.x is distance along the path in metres, UV.y runs around the tube.
Shader "Spatail/Flow"
{
    Properties
    {
        _Color ("Color", Color) = (0.42, 0.36, 0.9, 1)
        _Base ("Base Opacity", Range(0, 1)) = 0.18
        _Density ("Chevrons per metre", Float) = 7
        _Speed ("Speed (m/s)", Float) = 0.35
        _Opacity ("Opacity", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+5" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        ZWrite Off
        Cull Back

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
            half _Base;
            float _Density;
            float _Speed;
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

            fixed4 frag(v2f i) : SV_Target
            {
                // Fold the around-the-tube coordinate so each band bends into a chevron pointing downstream.
                float fold = abs(frac(i.uv.y * 2.0) * 2.0 - 1.0);
                float t = frac((i.uv.x - _Time.y * _Speed) * _Density + fold * 0.45);
                float band = smoothstep(0.0, 0.1, t) * (1.0 - smoothstep(0.4, 0.5, t));
                fixed4 col = _Color;
                col.a *= lerp(_Base, 1.0, band) * _Opacity;
                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
