// Copyright (c) <2015> <Playdead>
// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE.TXT)
// AUTHOR: Lasse Jon Fuglsang Pedersen <lasse@playdead.com>

using System;
using UnityEngine;

[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
[AddComponentMenu("Playdead/VelocityBuffer")]
public class VelocityBuffer : EffectBase
{
#if UNITY_PS4
    private const RenderTextureFormat velocityFormat = RenderTextureFormat.RGHalf;
#else
    private const RenderTextureFormat velocityFormat = RenderTextureFormat.RGFloat;
#endif

    private Camera _camera;

    public Shader velocityShader;
    private Material velocityMaterial;
    private Matrix4x4? velocityViewMatrix;
    [NonSerialized, HideInInspector] public RenderTexture velocityBuffer;
    [NonSerialized, HideInInspector] public RenderTexture velocityNeighborMax;

    public enum NeighborMaxSupport
    {
        TileSize10,
        TileSize20,
        TileSize40,
    };

    public bool neighborMaxGen = false;
    public NeighborMaxSupport neighborMaxSupport = NeighborMaxSupport.TileSize20;

    private float timeScaleNextFrame;
    public float timeScale { get; private set; }

#if UNITY_EDITOR
    [Header("Stats")]
    public int numResident = 0;
    public int numRendered = 0;
    public int numDrawCalls = 0;
#endif

    void Reset()
    {
        _camera = GetComponent<Camera>();
    }

    void Clear()
    {
        velocityViewMatrix = null;
    }

    void Awake()
    {
        Reset();
        Clear();
    }

    void Start()
    {
        timeScaleNextFrame = Time.timeScale;
    }

    void OnPreRender()
    {
        EnsureDepthTexture(_camera);
    }

    void OnPostRender()
    {
        EnsureMaterial(ref velocityMaterial, velocityShader);

        if (velocityMaterial == null)
            return;

        timeScale = timeScaleNextFrame;
        timeScaleNextFrame = (Time.timeScale == 0f) ? timeScaleNextFrame : Time.timeScale;

        int bufferW = _camera.pixelWidth;
        int bufferH = _camera.pixelHeight;

        if (EnsureRenderTarget(ref velocityBuffer, bufferW, bufferH, velocityFormat, FilterMode.Point, depthBits: 16))
            Clear();

        EnsureKeyword(velocityMaterial, "CAMERA_PERSPECTIVE", !_camera.orthographic);
        EnsureKeyword(velocityMaterial, "CAMERA_ORTHOGRAPHIC", _camera.orthographic);

        EnsureKeyword(velocityMaterial, "TILESIZE_10", neighborMaxSupport == NeighborMaxSupport.TileSize10);
        EnsureKeyword(velocityMaterial, "TILESIZE_20", neighborMaxSupport == NeighborMaxSupport.TileSize20);
        EnsureKeyword(velocityMaterial, "TILESIZE_40", neighborMaxSupport == NeighborMaxSupport.TileSize40);

        Matrix4x4 cameraP = GL.GetGPUProjectionMatrix(_camera.projectionMatrix, true);
        Matrix4x4 cameraP_NoFlip = GL.GetGPUProjectionMatrix(_camera.projectionMatrix, false);
        Matrix4x4 cameraV = _camera.worldToCameraMatrix;
        Matrix4x4 cameraVP = cameraP * cameraV;

        if (velocityViewMatrix == null)
            velocityViewMatrix = cameraV;

        RenderTexture activeRT = RenderTexture.active;
        RenderTexture.active = velocityBuffer;
        {
            GL.Clear(true, true, Color.black);

            const int kPrepass = 0;
            const int kVertices = 1;
            const int kVerticesSkinned = 2;
            const int kTileMax = 3;
            const int kNeighborMax = 4;

            // 0: prepass
            var jitter = GetComponent<FrustumJitter>();
            if (jitter != null)
                velocityMaterial.SetVector("_ProjectionExtents", _camera.GetProjectionExtents(jitter.activeSample.x, jitter.activeSample.y));
            else
                velocityMaterial.SetVector("_ProjectionExtents", _camera.GetProjectionExtents());

            velocityMaterial.SetMatrix("_CurrV", cameraV);
            velocityMaterial.SetMatrix("_CurrVP", cameraVP);
            velocityMaterial.SetMatrix("_PrevVP", cameraP * velocityViewMatrix.Value);
            velocityMaterial.SetMatrix("_PrevVP_NoFlip", cameraP_NoFlip * velocityViewMatrix.Value);
            velocityMaterial.SetPass(kPrepass);
            DrawFullscreenQuad();

            // 1 + 2: vertices + vertices skinned
            var obs = VelocityBufferTag.activeObjects;
#if UNITY_EDITOR
            numResident = obs.Count;
            numRendered = 0;
            numDrawCalls = 0;
#endif
            for (int i = 0, n = obs.Count; i != n; i++)
            {
                var ob = obs[i];
                if (ob != null && ob.rendering && ob.mesh != null)
                {
                    velocityMaterial.SetMatrix("_CurrM", ob.localToWorldCurr);
                    velocityMaterial.SetMatrix("_PrevM", ob.localToWorldPrev);
                    velocityMaterial.SetPass(ob.meshSmrActive ? kVerticesSkinned : kVertices);

                    for (int j = 0; j != ob.mesh.subMeshCount; j++)
                    {
                        Graphics.DrawMeshNow(ob.mesh, Matrix4x4.identity, j);
#if UNITY_EDITOR
                        numDrawCalls++;
#endif
                    }
#if UNITY_EDITOR
                    numRendered++;
#endif
                }
            }

            // 3 + 4: tilemax + neighbormax
            if (neighborMaxGen)
            {
                int tileSize = 1;

                switch (neighborMaxSupport)
                {
                    case NeighborMaxSupport.TileSize10: tileSize = 10; break;
                    case NeighborMaxSupport.TileSize20: tileSize = 20; break;
                    case NeighborMaxSupport.TileSize40: tileSize = 40; break;
                }

                int neighborMaxW = bufferW / tileSize;
                int neighborMaxH = bufferH / tileSize;

                EnsureRenderTarget(ref velocityNeighborMax, neighborMaxW, neighborMaxH, velocityFormat, FilterMode.Bilinear);

                // tilemax
                RenderTexture tileMax = RenderTexture.GetTemporary(neighborMaxW, neighborMaxH, 0, velocityFormat);
                RenderTexture.active = tileMax;
                {
                    velocityMaterial.SetTexture("_VelocityTex", velocityBuffer);
                    velocityMaterial.SetVector("_VelocityTex_TexelSize", new Vector4(1f / bufferW, 1f / bufferH, 0f, 0f));
                    velocityMaterial.SetPass(kTileMax);
                    DrawFullscreenQuad();
                }

                // neighbormax
                RenderTexture.active = velocityNeighborMax;
                {
                    velocityMaterial.SetTexture("_VelocityTex", tileMax);
                    velocityMaterial.SetVector("_VelocityTex_TexelSize", new Vector4(1f / neighborMaxW, 1f / neighborMaxH, 0f, 0f));
                    velocityMaterial.SetPass(kNeighborMax);
                    DrawFullscreenQuad();
                }

                RenderTexture.ReleaseTemporary(tileMax);
            }
            else if (velocityNeighborMax != null)
            {
                RenderTexture.ReleaseTemporary(velocityNeighborMax);
                velocityNeighborMax = null;
            }
        }
        RenderTexture.active = activeRT;

        velocityViewMatrix = cameraV;
    }

    void OnApplicationQuit()
    {
        ReleaseRenderTarget(ref velocityBuffer);
        ReleaseRenderTarget(ref velocityNeighborMax);
    }
}