// SPDX-License-Identifier: MIT
Shader "Gaussian Splatting/Render Splats"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }

        Pass
        {
            ZWrite Off
            Blend OneMinusDstAlpha One
            Cull Off
			ZTest On
            
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma require compute

#include "UnityCG.cginc"
#include "GaussianSplatting.hlsl"

StructuredBuffer<uint> _OrderBuffer;

struct v2f
{
    half4 col : COLOR0;
    float2 pos : TEXCOORD0;
    float4 vertex : SV_POSITION;
};

StructuredBuffer<SplatViewData> _SplatViewData;
int _SplatCount;

v2f vert (uint vtxID : SV_VertexID, uint instID : SV_InstanceID)
{
	v2f o = (v2f)0;
    instID = _OrderBuffer[instID];

	uint splatID = instID;

	#if defined(UNITY_SINGLE_PASS_STEREO)
		if(unity_StereoEyeIndex == 1)
			splatID += _SplatCount;
    #endif

	SplatViewData view = _SplatViewData[splatID];

	float4 centerClipPos = view.pos;

	bool behindCam = centerClipPos.w <= 0;
	if (behindCam)
	{
		o.vertex = asfloat(0x7fc00000); // NaN discards the primitive
	}
	else
	{
		o.col.r = f16tof32(view.color.x >> 16);
		o.col.g = f16tof32(view.color.x);
		o.col.b = f16tof32(view.color.y >> 16);
		o.col.a = f16tof32(view.color.y);

		uint idx = vtxID;

		if(idx == 3)
			idx = 1;
		else if(idx == 4)
			idx = 3;
		else if(idx == 5)
			idx = 2;

		float2 quadPos = float2(idx&1, (idx>>1)&1) * 2.0 - 1.0;
		quadPos *= 2;

		o.pos = quadPos;

		float2 deltaScreenPos = (quadPos.x * view.axis1 + quadPos.y * view.axis2) * 2 / _ScreenParams.xy;
		o.vertex = centerClipPos;
		o.vertex.xy += deltaScreenPos * centerClipPos.w;
	}
    return o;
}

half4 frag (v2f i) : SV_Target
{
	float power = -dot(i.pos, i.pos);
	half alpha = exp(power);

	alpha = saturate(alpha * i.col.a);
	
    if (alpha < 1.0/255.0)
        discard;

    half4 res = half4(i.col.rgb * alpha, alpha);
    return res;
}

ENDCG
        }
    }
}
