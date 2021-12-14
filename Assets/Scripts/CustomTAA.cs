using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class CustomTAA : MonoBehaviour
{
    private static RenderBuffer[] mrt = new RenderBuffer[3];
    private Material gBufferMaterial;
    public Shader gBufferShader;
    [Range(0.0001f, 0.9999f)]
    public float blendAlpha = 0.9f;
    [Range(0.0f, 1.0f)] public float feedbackMin = 0.88f;
    [Range(0.0f, 1.0f)] public float feedbackMax = 0.97f;
    RenderTexture[] history_rt;
    RenderTexture debug_rt;
    int idx_read = -1, idx_write = -1;
    RenderTexture gbuffer_rt;
    bool firstFrame = true;
    uint frame_index = 0;
    [SerializeField]
    Material mat;
    Matrix4x4 previousProjectionMatrix;
    Matrix4x4 previousViewProjectionMatrix;
    private Camera _camera;
    public Vector4 activeSample = Vector4.zero;// xy = current sample, zw = previous sample
    [Range(0.0f, 10.0f)]
    public float jitterScale = 1.0f;
    public Pattern pattern = Pattern.Uniform4_Helix;
    public float patternScale = 1.0f;
    public Vector4 activeSample2 = Vector4.zero;// xy = current sample, zw = previous sample
    public int activeIndex = -2;
    
    public float sx, sy;

    #region Static point data
    private static float[] points_Uniform4_Helix = new float[] {
        -0.25f, -0.25f,//ll  3  1
	     0.25f,  0.25f,//ur   \/|
         0.25f, -0.25f,//lr   /\|
        -0.25f,  0.25f,//ul  0  2
    };
    #endregion
    #region Static point data accessors
    public enum Pattern
    {
        Uniform4_Helix,
    };
    private static float[] AccessPointData(Pattern pattern)
    {
        switch (pattern)
        {
            case Pattern.Uniform4_Helix:
                return points_Uniform4_Helix;
            default:
                Debug.LogError("missing point distribution");
                return points_Uniform4_Helix;
        }
    }

    public static int AccessLength(Pattern pattern)
    {
        return AccessPointData(pattern).Length / 2;
    }

    public Vector2 Sample(Pattern pattern, int index)
    {
        float[] points = AccessPointData(pattern);
        int n = points.Length / 2;
        int i = index % n;

        float x = patternScale * points[2 * i + 0];
        float y = patternScale * points[2 * i + 1];

        return new Vector2(x, y);
    }
    #endregion


    float Halton(uint Index, uint Base)
    {
        float Result = 0.0f;
        float InvBase = 1.0f / Base;
        float Fraction = InvBase;
        while (Index > 0)
        {
            Result += (Index % Base) * Fraction;
            Index /= Base;
            Fraction *= InvBase;
        }
        return Result;
    }

    private void OnPreCull()
    {
        float u1 = Halton(frame_index + 1, 2);
        float u2 = Halton(frame_index + 1, 3);
        frame_index += 1;
        float Sigma = 0.47f;

        float OutWindow = 0.5f;
        float InWindow = (float)Math.Exp(-0.5 * Math.Pow(OutWindow / Sigma, 2));

        float Theta = 2.0f * (float)Math.PI * u2;
        float r = Sigma * (float)Math.Sqrt((float)(-2.0f * Math.Log((float)((1.0f - u1) * InWindow + u1))));  // r < 0.5

        float SampleX = r * (float)Math.Cos((double)Theta);
        float SampleY = r * (float)Math.Sin((double)Theta);


        //float x = jitterScale * SampleX * 2.0f / Camera.main.pixelWidth;
        //float y = jitterScale * SampleY * 2.0f / Camera.main.pixelHeight;
        float x = jitterScale * SampleX * 2.0f / Screen.width;
        float y = jitterScale * SampleY * 2.0f / Screen.height;


        Camera.main.ResetProjectionMatrix();
        var m = Camera.main.projectionMatrix;
        m.m02 += x;
        m.m12 += y;
        Camera.main.projectionMatrix = m;

        sx = x;
        sy = y;

        activeSample.z = activeSample.x;
        activeSample.w = activeSample.y;
        activeSample.x = SampleX;
        activeSample.y = SampleY;
        
        /*
        if (activeIndex == -2)
        {
            activeSample2 = Vector4.zero;
            activeIndex += 1;
            Camera.main.ResetProjectionMatrix();
        }
        else
        {
            activeIndex += 1;
            activeIndex %= AccessLength(pattern);
            Vector2 sample = jitterScale * Sample(pattern, activeIndex);

            float oneExtentY = Mathf.Tan(0.5f * Mathf.Deg2Rad * Camera.main.fieldOfView);
            float oneExtentX = oneExtentY * Camera.main.aspect;
            float texelSizeX = oneExtentX / (0.5f * Screen.width);
            float texelSizeY = oneExtentY / (0.5f * Screen.height);
            float oneJitterX = texelSizeX * sample.x;
            float oneJitterY = texelSizeY * sample.y;
            float left = oneJitterX - oneExtentX;
            float right = oneJitterX + oneExtentX;
            float bottom = oneJitterY - oneExtentY;
            float top = oneJitterY + oneExtentY;
            float a = (right + left) / (right - left);
            float b = (top + bottom) / (top - bottom);


            activeSample2.z = activeSample2.x;
            activeSample2.w = activeSample2.y;
            activeSample2.x = sample.x;
            activeSample2.y = sample.y;
            
            Camera.main.ResetProjectionMatrix();
            m = Camera.main.projectionMatrix;
            m.m02 += a;
            m.m12 += b;
            Camera.main.projectionMatrix = m;
        }
         */
        
    }

    void Reset()
    {
        _camera = GetComponent<Camera>();
    }

    void Clear()
    {
        activeSample = Vector4.zero;
        _camera.ResetProjectionMatrix();
        activeSample2 = Vector4.zero;
        activeIndex = -2;
    }

    void Awake()
    {
        Reset();
        Clear();
    }

    void OnDisable()
    {
        Clear();
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (destination != null && source.antiAliasing == destination.antiAliasing)// resolve without additional blit when not end of chain
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

    void Resolve(RenderTexture source, RenderTexture destination)
    {
        if (firstFrame)
        {
            if (gBufferMaterial == null)
                gBufferMaterial = new Material(gBufferShader);
            gbuffer_rt = new RenderTexture(Screen.width, Screen.height, 16, RenderTextureFormat.ARGBFloat);
            var format = source.format;
            format = RenderTextureFormat.ARGB32;
            history_rt = new RenderTexture[2];
            history_rt[0] = RenderTexture.GetTemporary(Screen.width, Screen.height, 0, format, RenderTextureReadWrite.Default, source.antiAliasing);
            history_rt[0].filterMode = FilterMode.Bilinear;
            history_rt[0].wrapMode = TextureWrapMode.Clamp;
            history_rt[1] = RenderTexture.GetTemporary(Screen.width, Screen.height, 0, format, RenderTextureReadWrite.Default, source.antiAliasing);
            history_rt[1].filterMode = FilterMode.Bilinear;
            history_rt[1].wrapMode = TextureWrapMode.Clamp;
            debug_rt = RenderTexture.GetTemporary(Screen.width, Screen.height, 0, format, RenderTextureReadWrite.Default, source.antiAliasing);
            debug_rt.filterMode = FilterMode.Bilinear;
            debug_rt.wrapMode = TextureWrapMode.Clamp;
            idx_write = 0;
            idx_read = 1;
            Graphics.Blit(source, history_rt[idx_write]);
            Graphics.Blit(source, destination);
            //previousViewProjectionMatrix = Camera.main.projectionMatrix * Camera.main.worldToCameraMatrix;
            previousViewProjectionMatrix = GL.GetGPUProjectionMatrix(Camera.main.projectionMatrix, false) * Camera.main.worldToCameraMatrix;
            firstFrame = false;
            idx_write += 1;
            idx_write %= 2;
            idx_read += 1;
            idx_read %= 2;
        }
        else
        {
            if (mat == null)
                Graphics.Blit(source, destination);
            else
            {
                
                RenderTexture activeRT = RenderTexture.active;
                RenderTexture.active = gbuffer_rt;
                GL.Clear(true, true, Color.black);
                const int kVertices = 1;
                const int kVerticesSkinned = 2;
                var obs = GBufferTag.activeObjects;
                gBufferMaterial.SetMatrix("_CurrV", Camera.main.worldToCameraMatrix);
                gBufferMaterial.SetMatrix("_CurrVP", GL.GetGPUProjectionMatrix(Camera.main.projectionMatrix, false) * Camera.main.worldToCameraMatrix);
                gBufferMaterial.SetMatrix("_CurrP", GL.GetGPUProjectionMatrix(Camera.main.projectionMatrix, false));
                for (int i = 0, n = obs.Count; i != n; i++)
                {
                    var ob = obs[i];
                    if (ob != null && ob.rendering && ob.mesh != null)
                    {
                        gBufferMaterial.SetMatrix("_CurrM", ob.transform.localToWorldMatrix);
                        gBufferMaterial.SetPass(ob.meshSmrActive ? kVerticesSkinned : kVertices);

                        for (int j = 0; j != ob.mesh.subMeshCount; j++)
                        {
                            Graphics.DrawMeshNow(ob.mesh, Matrix4x4.identity, j);
                        }
                    }
                }
                RenderTexture.active = activeRT;
                
                Vector4 jitterUV = activeSample;

                jitterUV.x /= source.width;
                jitterUV.y /= source.height;
                jitterUV.z /= source.width;
                jitterUV.w /= source.height;
                mat.SetVector("_JitterUV", jitterUV);
                mat.SetTexture("_CurrentTex", source);
                mat.SetTexture("_HistoryTex", history_rt[idx_read]);
                mat.SetTexture("_GBufferTex", gbuffer_rt);
                mat.SetFloat("_BlendAlpha", blendAlpha);
                mat.SetFloat("_FeedbackMin", feedbackMin);
                mat.SetFloat("_FeedbackMax", feedbackMax);
                mat.SetMatrix("_PreviousViewProjection", previousViewProjectionMatrix);
                mat.SetMatrix("_InverseView", Camera.main.cameraToWorldMatrix);
                var cur = GL.GetGPUProjectionMatrix(Camera.main.projectionMatrix, false) * Camera.main.worldToCameraMatrix;
                var curP = GL.GetGPUProjectionMatrix(Camera.main.projectionMatrix, false);
                var curV = Camera.main.worldToCameraMatrix;




                Matrix4x4 invViewProjMatrix = (GL.GetGPUProjectionMatrix(Camera.main.projectionMatrix, false) * Camera.main.worldToCameraMatrix).inverse;
                mat.SetMatrix("_InvViewProjMatrix", invViewProjMatrix);
                mat.SetMatrix("_CameraInverseProjection", Camera.main.projectionMatrix.inverse);
                mat.SetVector("_WorldCameraPos", Camera.main.transform.position);
                mat.SetMatrix("_InverseView", Camera.main.worldToCameraMatrix.inverse);
                history_rt[idx_write].DiscardContents();
                debug_rt.DiscardContents();
                mrt[0] = history_rt[idx_write].colorBuffer;
                mrt[1] = destination.colorBuffer;
                mrt[2] = debug_rt.colorBuffer;
                Graphics.SetRenderTarget(mrt, source.depthBuffer);
                mat.SetPass(0);
                DrawFullscreenQuad();
                previousViewProjectionMatrix = GL.GetGPUProjectionMatrix(Camera.main.projectionMatrix, false) * Camera.main.worldToCameraMatrix;
                previousProjectionMatrix = GL.GetGPUProjectionMatrix(Camera.main.projectionMatrix, false);
                idx_write += 1;
                idx_write %= 2;
                idx_read += 1;
                idx_read %= 2;

            }
        }
    }

    public void DrawFullscreenQuad()
    {
        GL.PushMatrix();
        GL.LoadOrtho();
        GL.Begin(GL.QUADS);
        {
            GL.MultiTexCoord2(0, 0.0f, 0.0f);
            GL.Vertex3(0.0f, 0.0f, 0.0f); // BL

            GL.MultiTexCoord2(0, 1.0f, 0.0f);
            GL.Vertex3(1.0f, 0.0f, 0.0f); // BR

            GL.MultiTexCoord2(0, 1.0f, 1.0f);
            GL.Vertex3(1.0f, 1.0f, 0.0f); // TR

            GL.MultiTexCoord2(0, 0.0f, 1.0f);
            GL.Vertex3(0.0f, 1.0f, 0.0f); // TL
        }
        GL.End();
        GL.PopMatrix();
    }

    void OnPostRender()
    {

    }
}
