// Hand outline for DemoHands: the mesh pushed out along its normals, back faces only, drawn after the
// hand's depth pre-pass so it shows outside the silhouette only - a crisp rim around the hand, fading
// out past the wrist. Port of Energy Duel's HandOutline (Safety Mill VR's M_GhostOutline: light grey
// for the Ghost look, thin and dark for the Skin look). Rendered after the fill (queue 3001).
Shader "Hidden/Hapbeat/DemoHandOutline"
{
    Properties
    {
        _OutlineColor ("Outline", Color) = (0.87, 0.88, 0.9, 1)
        _OutlineWidth ("Width (m)", Float) = 0.0018
        _OutlineOpacity ("Opacity", Range(0, 1)) = 0.85
        _FadeStart ("Wrist fade start (m toward the elbow)", Float) = -0.03
        _FadeLength ("Wrist fade length (m)", Float) = 0.05
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
            #include "HandCommon.cginc"

            fixed4 _OutlineColor;
            float _OutlineWidth;
            half _OutlineOpacity;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float fade : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(DemoHandAttributes v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 normal = normalize(UnityObjectToWorldNormal(v.normal));
                o.fade = DemoHandWristFade(world);
                o.pos = UnityWorldToClipPos(world + normal * _OutlineWidth);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return fixed4(_OutlineColor.rgb, _OutlineOpacity * i.fade);
            }
            ENDCG
        }
    }
    Fallback Off
}
