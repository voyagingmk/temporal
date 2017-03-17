// Copyright (c) <2015> <Playdead>
// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE.TXT)
// AUTHOR: Lasse Jon Fuglsang Pedersen <lasse@playdead.com>

Shader "Playdead/Post/VelocityBuffer"
{
	CGINCLUDE
	//--- program begin

	#pragma multi_compile CAMERA_PERSPECTIVE CAMERA_ORTHOGRAPHIC
	#pragma multi_compile __ TILESIZE_10 TILESIZE_20 TILESIZE_40

	#include "UnityCG.cginc"
	#include "IncDepth.cginc"

#if UNITY_VERSION < 540
	uniform float4x4 _CameraToWorld;// UNITY_SHADER_NO_UPGRADE
#endif

	uniform sampler2D_half _VelocityTex;
	uniform float4 _VelocityTex_TexelSize;

	uniform float4 _ProjectionExtents;// xy = frustum extents at distance 1, zw = jitter at distance 1

	uniform float4x4 _CurrV;
	uniform float4x4 _CurrVP;
	uniform float4x4 _CurrM;

	uniform float4x4 _PrevVP;
	uniform float4x4 _PrevVP_NoFlip;
	uniform float4x4 _PrevM;

	struct blit_v2f
	{
		float4 cs_pos : SV_POSITION;
		float2 ss_txc : TEXCOORD0;
		float2 vs_ray : TEXCOORD1;
	};

	blit_v2f blit_vert(appdata_img IN)
	{
		blit_v2f OUT;

	#if UNITY_VERSION < 540
		OUT.cs_pos = mul(UNITY_MATRIX_MVP, IN.vertex);
	#else
		OUT.cs_pos = UnityObjectToClipPos(IN.vertex);
	#endif
		OUT.ss_txc = IN.texcoord.xy;
		OUT.vs_ray = (2.0 * IN.texcoord.xy - 1.0) * _ProjectionExtents.xy + _ProjectionExtents.zw;

		return OUT;
	}

	float4 blit_frag_prepass(blit_v2f IN) : SV_Target
	{
		// reconstruct world position
		float vs_dist = depth_sample_linear(IN.ss_txc);
	#if CAMERA_PERSPECTIVE
		float3 vs_pos = float3(IN.vs_ray, 1.0) * vs_dist;
	#elif CAMERA_ORTHOGRAPHIC
		float3 vs_pos = float3(IN.vs_ray, vs_dist);
	#else
		#error "missing keyword CAMERA_..."
	#endif

	#if UNITY_VERSION < 540
		float4 ws_pos = mul(_CameraToWorld, float4(vs_pos, 1.0));// UNITY_SHADER_NO_UPGRADE
	#else
		float4 ws_pos = mul(unity_CameraToWorld, float4(vs_pos, 1.0));
	#endif

		//// NOTE: world space debug at 3D crane
		//return 0.1 * float4(ws_pos.xy - float2(595.0, -215.0), 0.0, 0.0);

		// reproject into previous frame
		float4 rp_cs_pos = mul(_PrevVP_NoFlip, ws_pos);
		float2 rp_ss_ndc = rp_cs_pos.xy / rp_cs_pos.w;
		float2 rp_ss_txc = 0.5 * rp_ss_ndc + 0.5;
		
		// estimate velocity
		float2 ss_vel = IN.ss_txc - rp_ss_txc;

		// output
		return float4(ss_vel, 0.0, 0.0);
	}

	float4 blit_frag_tilemax(blit_v2f IN) : SV_Target
	{
	#if TILESIZE_10
		const int support = 10;
	#elif TILESIZE_20
		const int support = 20;
	#elif TILESIZE_40
		const int support = 40;
	#else
		const int support = 1;
	#endif

		const float2 step = _VelocityTex_TexelSize.xy;
		const float2 base = IN.ss_txc + (0.5 - 0.5 * support) * step;
		const float2 du = float2(_VelocityTex_TexelSize.x, 0.0);
		const float2 dv = float2(0.0, _VelocityTex_TexelSize.y);

		float2 mv = 0.0;
		float rmv = 0.0;

		[unroll(10)]
		for (int i = 0; i != support; i++)
		{
			[unroll(10)]
			for (int j = 0; j != support; j++)
			{
				float2 v = tex2D(_VelocityTex, base + i * dv + j * du).xy;
				float rv = dot(v, v);
				if (rv > rmv)
				{
					mv = v;
					rmv = rv;
				}
			}
		}

		return float4(mv, 0.0, 0.0);
	}

	float4 blit_frag_neighbormax(blit_v2f IN) : SV_Target
	{
		const float2 du = float2(_VelocityTex_TexelSize.x, 0.0);
		const float2 dv = float2(0.0, _VelocityTex_TexelSize.y);

		float2 mv = 0.0;
		float dmv = 0.0;

		[unroll]
		for (int i = -1; i <= 1; i++)
		{
			[unroll]
			for (int j = -1; j <= 1; j++)
			{
				float2 v = tex2D(_VelocityTex, IN.ss_txc + i * dv + j * du).xy;
				float dv = dot(v, v);
				if (dv > dmv)
				{
					mv = v;
					dmv = dv;
				}
			}
		}

		return float4(mv, 0.0, 0.0);
	}

	struct v2f
	{
		float4 cs_pos : SV_POSITION;
		float4 ss_pos : TEXCOORD0;
		float3 cs_xy_curr : TEXCOORD1;
		float3 cs_xy_prev : TEXCOORD2;
	};

	v2f process_vertex(float4 ws_pos_curr, float4 ws_pos_prev)
	{
		v2f OUT;

		const float occlusion_bias = 0.03;

		OUT.cs_pos = mul(mul(_CurrVP, _CurrM), ws_pos_curr);
		OUT.ss_pos = ComputeScreenPos(OUT.cs_pos);
		OUT.ss_pos.z = -mul(mul(_CurrV, _CurrM), ws_pos_curr).z - occlusion_bias;// COMPUTE_EYEDEPTH
		OUT.cs_xy_curr = OUT.cs_pos.xyw;
		OUT.cs_xy_prev = mul(mul(_PrevVP, _PrevM), ws_pos_prev).xyw;

	#if UNITY_UV_STARTS_AT_TOP
		OUT.cs_xy_curr.y = -OUT.cs_xy_curr.y;
		OUT.cs_xy_prev.y = -OUT.cs_xy_prev.y;
	#endif

		return OUT;
	}

	v2f vert(appdata_base IN)
	{
		return process_vertex(IN.vertex, IN.vertex);
	}

	v2f vert_skinned(appdata_base IN)
	{
		return process_vertex(IN.vertex, float4(IN.normal, 1.0));// previous frame positions stored in normal data
	}

	float4 frag(v2f IN) : SV_Target
	{
		float2 ss_txc = IN.ss_pos.xy / IN.ss_pos.w;
		float scene_d = depth_sample_linear(ss_txc);

		// discard if occluded
		clip(scene_d - IN.ss_pos.z);

		// compute velocity in ndc
		float2 ndc_curr = IN.cs_xy_curr.xy / IN.cs_xy_curr.z;
		float2 ndc_prev = IN.cs_xy_prev.xy / IN.cs_xy_prev.z;

		// compute screen space velocity [0,1;0,1]
		return float4(0.5 * (ndc_curr - ndc_prev), 0.0, 0.0);
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

			#pragma vertex blit_vert
			#pragma fragment blit_frag_prepass
			#pragma only_renderers ps4 xboxone d3d11 d3d9 xbox360 opengl glcore gles3 metal vulkan
			#pragma target 3.0

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
			#pragma only_renderers ps4 xboxone d3d11 d3d9 xbox360 opengl glcore gles3 metal vulkan
			#pragma target 3.0

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
			#pragma only_renderers ps4 xboxone d3d11 d3d9 xbox360 opengl glcore gles3 metal vulkan
			#pragma target 3.0

			ENDCG
		}

		// 3: tilemax
		Pass
		{
			ZTest Always Cull Off ZWrite Off
			Fog { Mode Off }

			CGPROGRAM

			#pragma vertex blit_vert
			#pragma fragment blit_frag_tilemax
			#pragma only_renderers ps4 xboxone d3d11 d3d9 xbox360 opengl glcore gles3 metal vulkan
			#pragma target 3.0

			ENDCG
		}

		// 4: neighbormax
		Pass
		{
			ZTest Always Cull Off ZWrite Off
			Fog { Mode Off }

			CGPROGRAM

			#pragma vertex blit_vert
			#pragma fragment blit_frag_neighbormax
			#pragma only_renderers ps4 xboxone d3d11 d3d9 xbox360 opengl glcore gles3 metal vulkan
			#pragma target 3.0

			ENDCG
		}
	}

	Fallback Off
}