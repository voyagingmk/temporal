Shader "Custom/CustomTAA"
{
	Properties
	{
		_CurrentTex("Base (RGB)", 2D) = "white" {}
	}

	CGINCLUDE
// Upgrade NOTE: excluded shader from DX11; has structs without semantics (struct v2f_ray members ray)
	#pragma exclude_renderers d3d11
	#pragma only_renderers ps4 xboxone d3d11 d3d9 xbox360 opengl glcore gles3 metal vulkan
	#pragma target 3.0
	#pragma enable_d3d11_debug_symbols

	#include "UnityCG.cginc"


    uniform sampler2D _CurrentTex;
	uniform float4 _CurrentTex_TexelSize;
	
    uniform sampler2D _HistoryTex;
	sampler2D_float _GBufferTex;
	uniform float4 _JitterUV;// frustum jitter uv deltas, where xy = current frame, zw = previous
	uniform sampler2D_float _CameraDepthTexture;
	///uniform float4 _CameraDepthTexture_TexelSize;
	//uniform float4 _MainTex_TexelSize;
	uniform float _BlendAlpha;
	uniform float _FeedbackMin;
	uniform float _FeedbackMax;
	uniform float3 _WorldCameraPos;
	float4x4 _CameraInverseProjection;
	float4x4 _InverseView;
	float4x4 _PreviousViewProjection;
	float4x4 _InvViewProjMatrix;       // non-jittered
	
	float4 sample_color(sampler2D tex, float2 uv)
	{
		return tex2D(tex, uv);
	}

	struct v2f_ray
	{
		float4 pos : SV_POSITION;
		float2 uv : TEXCOORD0;
		//float3 ray : TEXCOORD1;
		//float3 worldDirection : TEXCOORD2;
	};

	v2f_ray vert_ray(appdata_full i)
	{
		v2f_ray o;
		o.uv = i.texcoord;
		o.pos = UnityObjectToClipPos(i.vertex);

		/*
		float4 p = float4(i.vertex.x, i.vertex.y, 1, 1);
		p = p * _ProjectionParams.z;
		float3 worldPos = mul(_ViewProjectionInverseMatrix, float4(p.xyzw));
		o.ray = worldPos - _WorldCameraPos;
		o.ray = normalize(o.ray) * length(o.ray) * _ProjectionParams.w;
		*/
		// Set the origin of the ray to the camera origin in world space.
		//float3 origin = mul(unity_CameraToWorld, float4(0.0f, 0.0f, 0.0f, 1.0f)).xyz;

		// Set the direction of the ray by mapping the uv input to the inverse projection matrix, rotating to match world space, and then normalizing it.
		//float3 direction = mul(_CameraInverseProjection, float4(o.uv, 0.0f, 1.0f)).xyz;
		//float3 direction = mul(_CameraInverseProjection, o.pos).xyz;
		//direction = mul(unity_CameraToWorld, float4(direction, 0.0f)).xyz;
		//direction = normalize(direction);
		//o.ray = float3(direction.x, direction.y, direction.z);
		return o;
	}

	/*
	float3 ComputeViewSpacePosition(v2f_ray i, float3 ray, float rawDepth)
	{
# if !defined(EXCLUDE_FAR_PLANE)
		float mask = 1;
# elif defined(UNITY_REVERSED_Z)
		float mask = rawDepth > 0;
# else
		float mask = rawDepth < 1;
# endif

		float3 vposPers =ray * Linear01Depth(rawDepth);
		return vposPers * mask;
	}


	float3 depth2worldPos(v2f_ray i, float2 uv)
	{
		float rawDepth = tex2D(_CameraDepthTexture, uv).x;
		float3 vPos = ComputeViewSpacePosition(i, i.ray, rawDepth);
		float3 wPos = mul(_InverseView, float4(vPos, 1)).xyz;
		return wPos;
	}


	float4 ComputeClipSpacePosition(float2 positionNDC, float deviceDepth)
	{
		float4 positionCS = float4(positionNDC * 2.0 - 1.0, deviceDepth, 1.0);

#if UNITY_UV_STARTS_AT_TOP
		// Our world space, view space, screen space and NDC space are Y-up.
		// Our clip space is flipped upside-down due to poor legacy Unity design.
		// The flip is baked into the projection matrix, so we only have to flip
		// manually when going from CS to NDC and back.
		positionCS.y = -positionCS.y;
#endif

		return positionCS;
	}


	float3 ComputeWorldSpacePosition(float2 positionNDC, float deviceDepth, float4x4 invViewProjMatrix)
	{
		float4 positionCS = ComputeClipSpacePosition(positionNDC, deviceDepth);
		float4 hpositionWS = mul(invViewProjMatrix, positionCS);
		return hpositionWS.xyz / hpositionWS.w;
	}
	*/

	float2 reprojection(float3 worldPos)
	{
		float4 cs = mul(_PreviousViewProjection, float4(worldPos, 1)); // clip space
		float4 tmp = cs / cs.w; // ndc
		return tmp.xy * 0.5 + 0.5; // uv space
	}

	struct f2rt
	{
		float4 buffer : SV_Target0;
		float4 screen : SV_Target1;
		float4 debug : SV_Target2;
	};

	f2rt frag2(v2f_ray i)
	{
		f2rt OUT;

		float4 jitter = _JitterUV;
		float2 iuv = i.uv;
		float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, iuv); // 0 .. 1
		float currDepth = Linear01Depth(rawDepth);
	    // Read depth, linearizing into worldspace units.
	   // float depth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv));

		// Multiply by worldspace direction (no perspective divide needed).
		//float3 worldspace = i.worldDirection * depth + _WorldCameraPos;
		//return float4(rawDepth, rawDepth, rawDepth, 1.0);
		//float4 H = float4(i.uv.x*2.0 - 1.0, (i.uv.y)*2.0 - 1.0, 1 - rawDepth, 1.0);
		float4 H = float4(iuv.x*2.0 - 1.0, (iuv.y)*2.0 - 1.0, rawDepth, 1.0);
		float4 D = mul(_InvViewProjMatrix, H);
		 D /= D.w;
		 float2 uv = reprojection(D.xyz);
		 /*
		 if (uv.y > i.uv.y)
			uv.y -= 0.000998;
		 else
			 uv.y += 0.000998;
		*/

		 float4 debug_D = rawDepth < 1.0 ? float4(frac(D.xyz), 1.0) : float4(0, 0, 0, 1);

		float3 gbuffer_wpos = tex2D(_GBufferTex, float2(iuv.x, 1 - iuv.y)).xyz;
		
		float4 debug_gbuffer_wpos = currDepth < 1.0 ? float4(frac((gbuffer_wpos)) + float3(0, 0, 0.1), 1.0f) : float4(0, 0, 0, 1);

		float l = length(gbuffer_wpos - (currDepth < 1.0 ? D.xyz : float3(0, 0, 0)));
		l = length(uv - i.uv) * 10.0f;
		float4 oc = float4(l, l, l, 1.0);
		oc = float4(abs(uv - i.uv), abs(i.uv - uv));
		oc = float4(uv.x == i.uv.x, uv.y == i.uv.y, 0, 1.0);

		//oc = float4(uv.x, uv.y, 0, 1);
		//oc = debug_gbuffer_wpos;
		//oc = debug_D;
		OUT.buffer = tex2D(_CurrentTex, i.uv);
		OUT.screen = float4(abs(uv.y - i.uv.y) * 100 , sign(uv.y - i.uv.y) ,0, 1);
		OUT.screen = float4(abs(uv.y - i.uv.y) * 100, abs(uv.x - i.uv.x) * 100, 0, 1);
		//OUT.screen = float4(abs(uv.x - i.uv.x) * 100, sign(uv.x - i.uv.x), 0, 1);
		//OUT.screen = oc;
		OUT.debug = float4(uv , i.uv);

		//return OUT;
		//return rawDepth < 1.0 ? float4(frac(D.xyz), 1.0) : float4(0, 0, 0, 1);
		// Draw a worldspace tartan pattern over the scene to demonstrate.  

		//float2 iuv = i.uv - _JitterUV.xy;
		//return float4(currDepth, currDepth, currDepth, 1.0);
		//float3 positionWS = ComputeWorldSpacePosition(iuv, rawDepth, _InvViewProjMatrix);
		//float3 positionWS = ComputeWorldSpacePosition(i.uv, currDepth, _InvViewProjMatrix);
		//fixed3 color = pow(abs(cos(gbuffer_wpos * UNITY_PI * 4)), 20);
		//return float4(color, 1.0);
		//return currDepth < 1.0 ? float4(frac((gbuffer_wpos)) + float3(0, 0, 0.1), 1.0f) : float4(0, 0, 0, 1);
		//return currDepth < 1.0 ? float4(frac((D)) + float3(0, 0, 0.1), 1.0f) : float4(0, 0, 0, 1);


		//return float4(frac(gbuffer_wpos), 1.0);
		//return currDepth < 1.0 ? float4(frac(positionWS), 1.0) : float4(0,0,0,1);

		//float2 uv = reprojection(positionWS);
		//uv.y = 1 - uv.y;

		//uv = reprojection(gbuffer_wpos.xyz);



		//float4 cs = mul(_PreviousViewProjection, float4(positionWS, 1));
		//float4 tmp = cs.xyzw / -cs.w;
		//return float4(tmp.z, tmp.z, tmp.z, 1.0);
		//return float4(uv.x, uv.y, 0, 1.0);
		//return float4(i.uv.x, i.uv.y, 0, 1.0);
		//float l = length(uv - i.uv);
		//return float4(l, l, 0, 1);
		
		//float2 o_uv = i.uv - _JitterUV.xy;
		//float3 wPos = depth2worldPos(i, o_uv);
		//float2 uv = reprojection(wPos);
		//uv -= jitter.zw;
		//jitter.yw =  - jitter.yw;
		//return float4(_CameraDepthTexture_TexelSize.zw * (uv - i.uv), 0, 1);

		//return float4(i.uv.x, i.uv.y, 0.0, 1.0);
		//return float4(uv.x, uv.y, 0.0, 1.0);
		//return float4(uv - i.uv, 0, 1.0);


		float4 c = tex2D(_CurrentTex, i.uv);
		float4 h = tex2D(_HistoryTex, uv + _JitterUV.zw - _JitterUV.xy);
		float4 to_screen = lerp(c, h, _BlendAlpha);
		OUT.buffer = to_screen;
		OUT.screen = to_screen;
		OUT.debug = c - h;

		// done
		return OUT;
	}

	struct v2f
	{
		float4 cs_pos : SV_POSITION;
		float2 uv : TEXCOORD0;
	};

	v2f vert(appdata_img IN)
	{
		v2f OUT;
		OUT.cs_pos = UnityObjectToClipPos(IN.vertex);
		OUT.uv = IN.texcoord.xy;
		return OUT;
	}

	float4 frag(v2f i) : SV_Target
	{
		float2 uv = i.uv;
		float4 c = tex2D(_CurrentTex, uv);
		float4 h = tex2D(_HistoryTex, uv);
		return lerp(c, h, _BlendAlpha);
	}

    ENDCG

	SubShader
	{
		ZTest Always Cull Off ZWrite Off
		Fog{ Mode off }

		Pass
		{
			CGPROGRAM

			#pragma vertex vert_ray
			#pragma fragment frag2

			ENDCG
		}
	}

	Fallback off
}
