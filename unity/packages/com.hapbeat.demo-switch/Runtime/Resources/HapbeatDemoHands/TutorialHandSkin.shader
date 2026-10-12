// Tutorial demonstration forearm, Skin look: the DemoHands Skin shading (the baked skin texture under one fixed
// unlit lighting function), fading out along the arm. Front faces only (the forearm culls back faces, after its
// depth pre-pass: only the nearest surface is blended). Where it fades it carries no shade: its colour eases from
// the shaded one to the skin lit as it faces the eye (no silhouette darkening, no shaded underside), so it only
// thins out toward what is behind it and never shows a darker band first. Also the hand's fill (culling off), drawn only
// where its fade is above _DepthClip, where its depth pre-pass writes: the nearest surface alone, opaque. See
// TutorialHandCommon.cginc.
Shader "Hidden/Hapbeat/DemoTutorialHandSkin"
{
    Properties
    {
        _MainTex ("Skin (baked)", 2D) = "white" {}
        _FadeStart ("Fade start (m toward the elbow)", Float) = 0.07
        _FadeLength ("Fade length (m)", Float) = 0.07
        _DepthClip ("Not drawn below this fade", Range(0, 1)) = 0
        [HideInInspector] _WristPosition ("Wrist (world)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ArmDirection ("Toward the elbow (world)", Vector) = (0, 0, -1, 0)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull [_Cull]
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "TutorialHandCommon.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float past : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(DemoTutorialHandAttributes v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityWorldToClipPos(o.world);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.past = DemoTutorialHandPast(o.world);
                return o;
            }

            // The DemoHands Skin lighting of a surface facing n, seen along v.
            float3 SkinLight(float3 albedo, float3 n, float3 v)
            {
                float3 l = normalize(float3(-0.4, 1.0, 0.3));
                float ndl = dot(n, l);
                float wrap = saturate((ndl + 0.5) / 1.5);
                float hemi = 0.5 + 0.5 * n.y;
                float3 light = lerp(float3(0.34, 0.31, 0.30), float3(0.58, 0.58, 0.62), hemi) + wrap * float3(0.62, 0.60, 0.56);
                float3 sss = float3(0.45, 0.12, 0.06) * saturate(1.0 - abs(ndl) * 2.0) * 0.5;
                float3 h = normalize(l + v);
                float spec = pow(saturate(dot(n, h)), 10.0) * 0.04;
                float edge = pow(1.0 - saturate(dot(n, v)), 3.0);
                return albedo * (light + sss) * (1.0 - 0.18 * edge) + spec;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 albedo = tex2D(_MainTex, i.uv).rgb;
                float3 v = normalize(_WorldSpaceCameraPos.xyz - i.world);
                float fade = DemoTutorialHandFade(i.past);
                clip(fade - _DepthClip);
                float3 shaded = SkinLight(albedo, normalize(i.normal), v);
                float3 facing = SkinLight(albedo, v, v);
                return fixed4(lerp(facing, shaded, fade), fade);
            }
            ENDCG
        }
    }
    Fallback Off
}
