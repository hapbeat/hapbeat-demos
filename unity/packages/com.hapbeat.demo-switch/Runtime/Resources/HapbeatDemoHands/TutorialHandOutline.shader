// Tutorial demonstration hand / forearm outline: the DemoHands outline (the mesh pushed out along its normals,
// back faces only, after the depth pre-pass: a rim outside the silhouette), fading out along the arm, and
// fading in from _InStart over _InLength (the forearm's, as the hand's own outline fades out over the cuff:
// one continuous rim; the hand's starts at once). It is gone where the fade falls to _DepthClip, the mark
// below which the depth pre-pass writes nothing: there its far side would show. See TutorialHandCommon.cginc.
Shader "Hidden/Hapbeat/DemoTutorialHandOutline"
{
    Properties
    {
        _OutlineColor ("Outline", Color) = (0.87, 0.88, 0.9, 1)
        _OutlineWidth ("Width (m)", Float) = 0.0018
        _OutlineOpacity ("Opacity", Range(0, 1)) = 0.85
        _FadeStart ("Fade start (m toward the elbow)", Float) = 0.07
        _FadeLength ("Fade length (m)", Float) = 0.07
        _InStart ("Fade-in start (m toward the elbow)", Float) = -1
        _InLength ("Fade-in length (m)", Float) = 0.001
        _DepthClip ("Gone at this fade (the depth pre-pass's mark)", Range(0, 1)) = 0
        [HideInInspector] _WristPosition ("Wrist (world)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _ArmDirection ("Toward the elbow (world)", Vector) = (0, 0, -1, 0)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+1" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Front
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "TutorialHandCommon.cginc"

            fixed4 _OutlineColor;
            float _OutlineWidth;
            half _OutlineOpacity;
            float _InStart;
            float _InLength;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float past : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(DemoTutorialHandAttributes v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 normal = normalize(UnityObjectToWorldNormal(v.normal));
                o.past = DemoTutorialHandPast(world);
                o.pos = UnityWorldToClipPos(world + normal * _OutlineWidth);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float fadeIn = saturate((i.past - _InStart) / max(_InLength, 1e-4));
                fadeIn = fadeIn * fadeIn * (3.0 - 2.0 * fadeIn);
                float fade = saturate((DemoTutorialHandFade(i.past) - _DepthClip) / max(1.0 - _DepthClip, 1e-3));
                return fixed4(_OutlineColor.rgb, _OutlineOpacity * fade * fadeIn);
            }
            ENDCG
        }
    }
    Fallback Off
}
