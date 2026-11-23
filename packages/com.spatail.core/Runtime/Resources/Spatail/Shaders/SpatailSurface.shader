// Lit-looking unlit surface used for everything solid Spatail draws: the stand-in twin, pins, arrows, bars,
// highlight volumes. Shading comes from a fixed key light plus a rim term, so content looks the same in any room
// and needs no scene lights. Works in the Built-in pipeline and as an unlit pass in URP.
// Alpha is blended separately so passthrough compositing on headsets gets correct coverage.
Shader "Spatail/Surface"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _RimColor ("Rim Color", Color) = (0.42, 0.36, 0.9, 1)
        _Shade ("Shade Amount", Range(0, 1)) = 0
        _Rim ("Rim Amount", Range(0, 1)) = 0
        _RimPower ("Rim Power", Range(0.5, 8)) = 3
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        ZWrite [_ZWrite]
        ZTest [_ZTest]
        Cull [_Cull]
        ColorMask [_ColorMask]

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
                float3 normal : NORMAL;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float3 worldNormal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            fixed4 _RimColor;
            half _Shade;
            half _Rim;
            half _RimPower;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir = _WorldSpaceCameraPos - worldPos;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                float3 v = normalize(i.viewDir);
                // Fixed key light from above and slightly behind the viewer's left shoulder.
                float3 l = normalize(float3(-0.35, 0.85, -0.4));
                half wrap = saturate(dot(n, l) * 0.5 + 0.5);
                fixed4 col = i.color;
                col.rgb *= lerp(1.0, 0.7 + 0.3 * wrap, _Shade);
                half rim = pow(1.0 - saturate(abs(dot(n, v))), _RimPower) * _Rim;
                col.rgb = lerp(col.rgb, _RimColor.rgb, rim);
                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
