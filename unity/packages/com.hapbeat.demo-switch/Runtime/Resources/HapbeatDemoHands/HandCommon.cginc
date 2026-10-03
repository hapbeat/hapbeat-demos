#ifndef HAPBEAT_DEMO_HAND_COMMON_INCLUDED
#define HAPBEAT_DEMO_HAND_COMMON_INCLUDED

// Shared by the DemoHands shaders (Ghost / Skin / Outline). UnityCG only and no LightMode tag, so the
// built-in pipeline draws them as forward passes and URP as SRPDefaultUnlit. Ported from Energy
// Duel's URP hand shaders (after Safety Mill VR's hand materials): every look fades out along the
// forearm past the wrist. _WristPosition / _ArmDirection (world, toward the elbow) are set per frame
// per hand by DemoHands through a MaterialPropertyBlock.

#include "UnityCG.cginc"

float _FadeStart;
float _FadeLength;
float4 _WristPosition;
float4 _ArmDirection;

// 1 over the hand, easing (smoothstep) to 0 over _FadeLength metres from _FadeStart along the arm.
float DemoHandWristFade(float3 positionWS)
{
    float3 arm = _ArmDirection.xyz;
    arm = dot(arm, arm) > 1e-8 ? normalize(arm) : float3(0, 0, -1);
    float past = dot(positionWS - _WristPosition.xyz, arm) - _FadeStart;
    float t = saturate(past / max(_FadeLength, 1e-4));
    return 1.0 - t * t * (3.0 - 2.0 * t);
}

struct DemoHandAttributes
{
    float4 vertex : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

#endif
