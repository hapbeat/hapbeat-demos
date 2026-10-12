// Tutorial demonstration hand / forearm, Ghost look: the DemoHands Ghost fill (flat dark fill, two greys
// blended by fresnel, slightly transparent, fading out along the arm). Also their depth pre-pass
// (_ColorMask 0, _ZWrite 1): writing only where the fade is above _DepthClip. See TutorialHandCommon.cginc.
Shader "Hidden/Hapbeat/DemoTutorialHandGhost"
{
    Properties
    {
        _ColorTop ("Fill facing the eye", Color) = (0.196, 0.2, 0.21, 1)
        _ColorBottom ("Fill at the silhouette", Color) = (0.122, 0.126, 0.13, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.62
        _FadeStart ("Fade start (m toward the elbow)", Float) = 0.07
        _FadeLength ("Fade length (m)", Float) = 0.07
        _DepthClip ("No depth below this fade", Range(0, 1)) = 0
        [HideInInspector] _WristPosition ("Wrist (world)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ArmDirection ("Toward the elbow (world)", Vector) = (0, 0, -1, 0)
        [Enum(UnityEngine.Rendering.ColorWriteMask)] _ColorMask ("Color mask", Float) = 15
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            ZWrite [_ZWrite]
            ZTest LEqual
            ColorMask [_ColorMask]
            Cull [_Cull]
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "TutorialHandCommon.cginc"

            fixed4 _ColorTop;
            fixed4 _ColorBottom;
            half _Opacity;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float past : TEXCOORD2;
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
                o.past = DemoTutorialHandPast(o.world);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float fade = DemoTutorialHandFade(i.past);
                clip(fade - _DepthClip);
                float3 view = normalize(_WorldSpaceCameraPos.xyz - i.world);
                // Facing from |N.V|: robust to the mesh's winding and to its far side.
                float facing = abs(dot(normalize(i.normal), view));
                float f = pow(saturate(1.0 - facing), 0.16);
                fixed3 color = lerp(_ColorTop.rgb, _ColorBottom.rgb, f);
                half alpha = _Opacity * fade * saturate(facing * 4.0 + 0.6);
                return fixed4(color, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
