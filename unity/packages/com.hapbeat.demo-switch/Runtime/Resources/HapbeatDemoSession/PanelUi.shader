// Colours of every DemoSessionPanel (background, buttons, texts, cursors). Like UI/Default, but with
// ZTest Always so a panel is never hidden by the scene's models: it draws over whatever is behind or in
// front of it. The panel's depth layer (PanelDepth.shader, also ZTest Always) then writes the panel plane,
// so the shared hands drawn after the panels still show in front of a panel and stay hidden behind it.
Shader "Hidden/Hapbeat/DemoPanelUi"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Lighting Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            // Set by the canvas for alpha-only font textures (1,1,1,0), as in UI/Default.
            fixed4 _TextureSampleAdd;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return (tex2D(_MainTex, i.uv) + _TextureSampleAdd) * i.color;
            }
            ENDCG
        }
    }
    Fallback Off
}
