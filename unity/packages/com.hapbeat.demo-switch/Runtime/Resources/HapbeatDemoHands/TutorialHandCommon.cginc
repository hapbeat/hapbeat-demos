#ifndef HAPBEAT_DEMO_TUTORIAL_HAND_COMMON_INCLUDED
#define HAPBEAT_DEMO_TUTORIAL_HAND_COMMON_INCLUDED

// Shared by the tutorial demonstration hand's hand and forearm shaders (DemoTutorialHand: Ghost / Skin / Outline): the looks of the
// DemoHands hand shaders (Hand*.shader beside these, Hidden/Hapbeat/DemoHand*, same property names, so a copy
// takes the shared hands' material values), for DemoTutorialHand's forearm and its copy of Meta's hand
// mesh. Differences: culling is a property (the hand: off; the closed forearm: back faces, so its inner side
// never shows), and the depth pre-pass writes only where the surface is visible enough (_DepthClip): the
// forearm's faded end hides nothing behind it, and the hand (pre-pass, fill and outline alike) ends where it
// is opaque, just past the wrist, where the forearm takes over. _WristPosition /
// _ArmDirection (world, toward the elbow) are set per frame through a MaterialPropertyBlock.

#include "UnityCG.cginc"

float _FadeStart;
float _FadeLength;
float _DepthClip;
float4 _WristPosition;
float4 _ArmDirection;

// How far past the wrist along the arm (m); linear in position, so it interpolates exactly.
float DemoTutorialHandPast(float3 positionWS)
{
    float3 arm = _ArmDirection.xyz;
    arm = dot(arm, arm) > 1e-8 ? normalize(arm) : float3(0, 0, -1);
    return dot(positionWS - _WristPosition.xyz, arm);
}

// 1 up to _FadeStart past the wrist, easing (smoothstep) to 0 over _FadeLength (as DemoHands' wrist fade).
float DemoTutorialHandFade(float past)
{
    float t = saturate((past - _FadeStart) / max(_FadeLength, 1e-4));
    return 1.0 - t * t * (3.0 - 2.0 * t);
}

struct DemoTutorialHandAttributes
{
    float4 vertex : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

#endif
