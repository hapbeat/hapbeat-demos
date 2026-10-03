// Skin hand for DemoHands: the baked skin texture (set per hand at runtime) under one fixed, unlit
// lighting function - wrap diffuse, sky / ground ambient, a warm terminator, a soft highlight and a
// slightly darker silhouette - opaque over the hand with a short fade past the wrist (nearest layer
// only, after the HandGhost depth pre-pass). Port of Energy Duel's HandSkin (Safety Mill VR's M_SkinHand).
Shader "Hidden/Hapbeat/DemoHandSkin"
{
    Properties
    {
        _MainTex ("Skin (baked)", 2D) = "white" {}
        _FadeStart ("Wrist fade start (m toward the elbow)", Float) = -0.025
        _FadeLength ("Wrist fade length (m)", Float) = 0.03
        [HideInInspector] _WristPosition ("Wrist (world)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ArmDirection ("Toward the elbow (world)", Vector) = (0, 0, -1, 0)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "HandCommon.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fade : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(DemoHandAttributes v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityWorldToClipPos(o.world);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.fade = DemoHandWristFade(o.world);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 albedo = tex2D(_MainTex, i.uv).rgb;
                float3 v = normalize(_WorldSpaceCameraPos.xyz - i.world);
                float3 n = normalize(i.normal);
                // Front / back from the normal against the view, not the winding.
                float face = dot(n, v) >= -0.5 ? 1.0 : -1.0;
                n *= face;
                float3 l = normalize(float3(-0.4, 1.0, 0.3));
                float ndl = dot(n, l);
                float wrap = saturate((ndl + 0.5) / 1.5);
                float hemi = 0.5 + 0.5 * n.y;
                float3 light = lerp(float3(0.34, 0.31, 0.30), float3(0.58, 0.58, 0.62), hemi) + wrap * float3(0.62, 0.60, 0.56);
                float3 sss = float3(0.45, 0.12, 0.06) * saturate(1.0 - abs(ndl) * 2.0) * 0.5;
                float3 h = normalize(l + v);
                float spec = pow(saturate(dot(n, h)), 10.0) * 0.04;
                float edge = pow(1.0 - saturate(dot(n, v)), 3.0);
                float inside = face < 0 ? 0.85 : 1.0;
                float3 color = (albedo * (light + sss) * (1.0 - 0.18 * edge) + spec) * inside;
                return fixed4(color, i.fade);
            }
            ENDCG
        }
    }
    Fallback Off
}
