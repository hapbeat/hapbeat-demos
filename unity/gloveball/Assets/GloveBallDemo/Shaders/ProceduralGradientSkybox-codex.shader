// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Hapbeat
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

Shader "Hapbeat/Procedural Gradient Skybox"
{
    Properties
    {
        [HDR] _TopColor ("Top Color", Color) = (0.3, 0.65, 0.9, 1)
        [HDR] _HorizonColor ("Horizon Color", Color) = (0.73, 1, 1, 1)
        [HDR] _BottomColor ("Bottom Color", Color) = (0.95, 0.99, 1, 1)
        _TopExponent ("Top Exponent", Float) = 4
        _BottomExponent ("Bottom Exponent", Float) = 8
        _AmplFactor ("Amplification", Float) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half4 _TopColor;
            half4 _HorizonColor;
            half4 _BottomColor;
            float _TopExponent;
            float _BottomExponent;
            half _AmplFactor;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = UnityObjectToClipPos(input.positionOS);
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float height = normalize(input.direction).y;
                float topWeight = pow(saturate(height), max(_TopExponent, 0.0001));
                float bottomWeight = pow(saturate(-height), max(_BottomExponent, 0.0001));
                float horizonWeight = saturate(1.0 - topWeight - bottomWeight);
                return (_TopColor * topWeight + _HorizonColor * horizonWeight +
                        _BottomColor * bottomWeight) * _AmplFactor;
            }
            ENDHLSL
        }
    }
}
