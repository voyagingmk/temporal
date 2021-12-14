// Copyright (c) <2015> <Playdead>
// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE.TXT)
// AUTHOR: Lasse Jon Fuglsang Pedersen <lasse@playdead.com>

Shader "Playdead/Post/GBuffer"
{
	CGINCLUDE
	//--- program begin

	#pragma only_renderers ps4 xboxone d3d11 d3d9 xbox360 opengl glcore gles3 metal vulkan
	#pragma target 3.0

	#pragma enable_d3d11_debug_symbols
	#pragma multi_compile CAMERA_PERSPECTIVE CAMERA_ORTHOGRAPHIC
	#pragma multi_compile __ TILESIZE_10 TILESIZE_20 TILESIZE_40

	#include "UnityCG.cginc"
	#include "IncDepth.cginc"


	uniform float4x4 _CurrV;
	uniform float4x4 _CurrVP;
	uniform float4x4 _CurrM;
	uniform float4x4 _CurrP;
	

	struct v2f
	{
		float4 cs_pos : SV_POSITION;
		float3 wpos : TEXCOORD0;
	};

	v2f process_vertex(float4 pos_curr)
	{
		#if UNITY_UV_STARTS_AT_TOP
		pos_curr.y = -pos_curr.y;
		#endif
		v2f OUT;
		OUT.wpos = mul(_CurrM, float4(pos_curr.xyz, 1.0)).xyz;
		OUT.cs_pos = mul(_CurrVP, mul(_CurrM, float4(pos_curr.xyz, 1.0)));
		//OUT.cs_pos = mul(_CurrP, mul(_CurrV, mul(_CurrM, float4(ws_pos_curr.xyz, 1.0))));

		return OUT;
	}

	v2f vert(appdata_base IN)
	{
		return process_vertex(IN.vertex);
	}

	v2f vert_skinned(appdata_base IN)
	{
		v2f output = process_vertex(IN.vertex);// previous frame positions stored in normal data
		return output;
	}

	float4 frag(v2f IN) : SV_Target
	{
		return float4(IN.wpos, 1.0);
	}

	//--- program end
	ENDCG

	SubShader
	{
		// 0: prepass
		Pass
		{
			ZTest Always Cull Off ZWrite Off
			Fog { Mode Off }

			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag

			ENDCG
		}

		// 1: vertices
		Pass
		{
			ZTest LEqual Cull Back ZWrite On
			Fog { Mode Off }

			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag

			ENDCG
		}

		// 2: vertices skinned
		Pass
		{
			ZTest LEqual Cull Back ZWrite On
			Fog { Mode Off }

			CGPROGRAM

			#pragma vertex vert_skinned
			#pragma fragment frag

			ENDCG
		}
	}

	Fallback Off
}