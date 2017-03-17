// Copyright (c) <2015> <Playdead>
// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE.TXT)
// AUTHOR: Lasse Jon Fuglsang Pedersen <lasse@playdead.com>

using UnityEngine;

[ExecuteInEditMode]
[RequireComponent(typeof(Camera), typeof(FrustumJitter), typeof(VelocityBuffer))]
[AddComponentMenu("Playdead/TemporalReprojection")]
public class TemporalReprojection : EffectBase
{
    private static RenderBuffer[] mrt = new RenderBuffer[2];

    private Camera _camera;
    private FrustumJitter _frustumJitter;
    private VelocityBuffer _velocityBuffer;

    public Shader reprojectionShader;
    private Material reprojectionMaterial;
    private RenderTexture[] reprojectionBuffer;
    private int reprojectionIndex = 0;

    public enum Neighborhood
    {
        MinMax3x3,
        MinMax3x3Rounded,
        MinMax4TapVarying,
    };

    public Neighborhood neighborhood = Neighborhood.MinMax3x3Rounded;
    public bool unjitterColorSamples = true;
    public bool unjitterNeighborhood = false;
    public bool unjitterReprojection = false;
    public bool useYCoCg = false;
    public bool useClipping = true;
    public bool useDilation = true;
    public bool useMotionBlur = true;
    public bool useOptimizations = true;

    [Range(0f, 1f)] public float feedbackMin = 0.88f;
    [Range(0f, 1f)] public float feedbackMax = 0.97f;

    public float motionBlurStrength = 1f;
    public bool motionBlurIgnoreFF = false;

    void Reset()
    {
        _camera = GetComponent<Camera>();
        _frustumJitter = GetComponent<FrustumJitter>();
        _velocityBuffer = GetComponent<VelocityBuffer>();
    }

    void Clear()
    {
        reprojectionIndex = -1;
    }

    void Awake()
    {
        Reset();
        Clear();
    }

    void Resolve(RenderTexture source, RenderTexture destination)
    {
        EnsureMaterial(ref reprojectionMaterial, reprojectionShader);

        if (reprojectionMaterial == null)
        {
            Graphics.Blit(source, destination);
            return;
        }

        if (reprojectionBuffer == null || reprojectionBuffer.Length != 2)
            reprojectionBuffer = new RenderTexture[2];

        int bufferW = source.width;
        int bufferH = source.height;

        if (EnsureRenderTarget(ref reprojectionBuffer[0], bufferW, bufferH, RenderTextureFormat.ARGB32, FilterMode.Bilinear, antiAliasing: source.antiAliasing))
            Clear();
        if (EnsureRenderTarget(ref reprojectionBuffer[1], bufferW, bufferH, RenderTextureFormat.ARGB32, FilterMode.Bilinear, antiAliasing: source.antiAliasing))
            Clear();

        EnsureKeyword(reprojectionMaterial, "CAMERA_PERSPECTIVE", !_camera.orthographic);
        EnsureKeyword(reprojectionMaterial, "CAMERA_ORTHOGRAPHIC", _camera.orthographic);

        EnsureKeyword(reprojectionMaterial, "MINMAX_3X3", neighborhood == Neighborhood.MinMax3x3);
        EnsureKeyword(reprojectionMaterial, "MINMAX_3X3_ROUNDED", neighborhood == Neighborhood.MinMax3x3Rounded);
        EnsureKeyword(reprojectionMaterial, "MINMAX_4TAP_VARYING", neighborhood == Neighborhood.MinMax4TapVarying);
        EnsureKeyword(reprojectionMaterial, "UNJITTER_COLORSAMPLES", unjitterColorSamples);
        EnsureKeyword(reprojectionMaterial, "UNJITTER_NEIGHBORHOOD", unjitterNeighborhood);
        EnsureKeyword(reprojectionMaterial, "UNJITTER_REPROJECTION", unjitterReprojection);
        EnsureKeyword(reprojectionMaterial, "USE_YCOCG", useYCoCg);
        EnsureKeyword(reprojectionMaterial, "USE_CLIPPING", useClipping);
        EnsureKeyword(reprojectionMaterial, "USE_DILATION", useDilation);
#if UNITY_EDITOR
        EnsureKeyword(reprojectionMaterial, "USE_MOTION_BLUR", Application.isPlaying ? useMotionBlur : false);
#else
        EnsureKeyword(reprojectionMaterial, "USE_MOTION_BLUR", useMotionBlur);
#endif
        EnsureKeyword(reprojectionMaterial, "USE_MOTION_BLUR_NEIGHBORMAX", _velocityBuffer.velocityNeighborMax != null);
        EnsureKeyword(reprojectionMaterial, "USE_OPTIMIZATIONS", useOptimizations);

        if (reprojectionIndex == -1)// bootstrap
        {
            reprojectionIndex = 0;
            reprojectionBuffer[reprojectionIndex].DiscardContents();
            Graphics.Blit(source, reprojectionBuffer[reprojectionIndex]);
        }

        int indexRead = reprojectionIndex;
        int indexWrite = (reprojectionIndex + 1) % 2;

        Vector4 jitterUV = _frustumJitter.activeSample;
        jitterUV.x /= source.width;
        jitterUV.y /= source.height;
        jitterUV.z /= source.width;
        jitterUV.w /= source.height;

        reprojectionMaterial.SetVector("_JitterUV", jitterUV);
        reprojectionMaterial.SetTexture("_VelocityBuffer", _velocityBuffer.velocityBuffer);
        reprojectionMaterial.SetTexture("_VelocityNeighborMax", _velocityBuffer.velocityNeighborMax);
        reprojectionMaterial.SetTexture("_MainTex", source);
        reprojectionMaterial.SetTexture("_PrevTex", reprojectionBuffer[indexRead]);
        reprojectionMaterial.SetFloat("_FeedbackMin", feedbackMin);
        reprojectionMaterial.SetFloat("_FeedbackMax", feedbackMax);
        reprojectionMaterial.SetFloat("_MotionScale", motionBlurStrength * (motionBlurIgnoreFF ? Mathf.Min(1f, 1f / _velocityBuffer.timeScale) : 1f));

        // reproject frame n-1 into output + history buffer
        {
            mrt[0] = reprojectionBuffer[indexWrite].colorBuffer;
            mrt[1] = destination.colorBuffer;

            Graphics.SetRenderTarget(mrt, source.depthBuffer);
            reprojectionMaterial.SetPass(0);
            reprojectionBuffer[indexWrite].DiscardContents();

            DrawFullscreenQuad();

            reprojectionIndex = indexWrite;
        }
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (destination != null)// resolve without additional blit when not end of chain
        {
            Resolve(source, destination);
        }
        else
        {
            RenderTexture internalDestination = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, source.antiAliasing);
            {
                Resolve(source, internalDestination);
                Graphics.Blit(internalDestination, destination);
            }
            RenderTexture.ReleaseTemporary(internalDestination);
        }
    }

    void OnApplicationQuit()
    {
        if (reprojectionBuffer != null)
        {
            ReleaseRenderTarget(ref reprojectionBuffer[0]);
            ReleaseRenderTarget(ref reprojectionBuffer[1]);
        }
    }
}