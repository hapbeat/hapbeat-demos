// Translucent rim-lit "ghost" surface for DemoGhostHands. Written against UnityCG only (no lights,
// no LightMode tag), so the built-in pipeline draws it as a forward pass and URP as SRPDefaultUnlit.
// Two materials share it: a depth-only pre-pass (ColorMask 0, ZWrite On, queue 2999) and the colour
// pass (ZWrite Off, ZTest LEqual, queue 3000) so overlapping joint spheres and bone tubes blend once.
Shader "Hidden/Hapbeat/DemoGhostHand"
{
    Properties
    {
        _Color ("Body", Color) = (0.8, 0.9, 1, 0.22)
        _RimColor ("Rim", Color) = (0.9, 0.98, 1, 0.95)
        _RimPower ("Rim power", Range(0.5, 8)) = 2.5
        [Enum(UnityEngine.Rendering.ColorWriteMask)] _ColorMask ("Color mask", Float) = 15
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_ZWrite]
            ZTest LEqual
            ColorMask [_ColorMask]
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;
            fixed4 _RimColor;
            half _RimPower;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                half rim : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 normal = UnityObjectToWorldNormal(v.normal);
                float3 view = normalize(_WorldSpaceCameraPos.xyz - world);
                o.rim = pow(1 - saturate(abs(dot(normal, view))), _RimPower);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return lerp(_Color, _RimColor, saturate(i.rim));
            }
            ENDCG
        }
    }
    Fallback Off
}
