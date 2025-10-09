// Assets/DepthMesh/Scripts/QuestDepthProvider_Oculus_Meta.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;
using XR = Unity.XR.Oculus;                 // low-level depth control
using Meta.XR.EnvironmentDepth;             // for project reference / manager (shader path)

[DisallowMultipleComponent]
public class QuestDepthProvider_Oculus_Meta : MonoBehaviour
{
    [Header("Camera")]
    public Camera xrCamera;

    [Header("Downsample (higher = faster, lower = sharper)")]
    [Range(1, 8)] public int downsample = 4;

    [Header("Source format")]
    [Tooltip("If env depth is R16 in millimeters, keep true (scale by 0.001). If the runtime already outputs meters, set false.")]
    public bool sourceIsMillimeters = true;

    [Header("Runtime (read-only)")]
    public bool depthSupported;
    public bool depthRenderingEnabled;
    public int width, height;         // downsampled
    public float fx, fy, cx, cy;      // intrinsics approx
    public float[] depthMeters;       // CPU buffer (row-major)

    // GPU
    RenderTexture _rtDownsampledMeters;
    Material _copyDepthMat;

    // XR display to resolve tex ID -> RenderTexture
    XRDisplaySubsystem _xrDisplay;

    static readonly int _MetersMul = Shader.PropertyToID("_MetersMul");

    void Awake()
    {
        if (!xrCamera) xrCamera = Camera.main;

        // Support check
        depthSupported = XR.Utils.GetEnvironmentDepthSupported();
        if (!depthSupported)
        {
            Debug.LogWarning("[DepthProvider] Environment Depth not supported on this device/runtime.");
            enabled = false; return;
        }

        // Enable depth rendering (Meta XR EnvironmentDepthManager can also be in scene for shaders)
        var create = new XR.Utils.EnvironmentDepthCreateParams { removeHands = false };
        XR.Utils.SetupEnvironmentDepth(create);
        XR.Utils.SetEnvironmentDepthRendering(true);
        depthRenderingEnabled = true;

        // Cache XRDisplaySubsystem
        var displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        if (displays.Count > 0) _xrDisplay = displays[0];

        _copyDepthMat = new Material(Shader.Find("Hidden/CopyDepthToMeters"));
    }

    void OnDisable()
    {
        if (depthRenderingEnabled)
        {
            XR.Utils.SetEnvironmentDepthRendering(false);
            XR.Utils.ShutdownEnvironmentDepth();
            depthRenderingEnabled = false;
        }
        if (_rtDownsampledMeters) { _rtDownsampledMeters.Release(); Destroy(_rtDownsampledMeters); }
        if (_copyDepthMat) Destroy(_copyDepthMat);
    }

    void Update()
    {
        if (!depthRenderingEnabled || _xrDisplay == null || xrCamera == null) return;

        // Get texture ID for this frame
        uint texId = 0;
        if (!XR.Utils.GetEnvironmentDepthTextureId(ref texId)) return;

        // Resolve to a RenderTexture
        var srcRT = _xrDisplay.GetRenderTexture(texId);
        if (!srcRT) return;

        // Downsample RT
        int outW = Mathf.Max(1, srcRT.width / downsample);
        int outH = Mathf.Max(1, srcRT.height / downsample);
        var rtFmt = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat)
                    ? RenderTextureFormat.RFloat : RenderTextureFormat.RHalf;

        if (_rtDownsampledMeters == null || _rtDownsampledMeters.width != outW || _rtDownsampledMeters.height != outH || _rtDownsampledMeters.format != rtFmt)
        {
            if (_rtDownsampledMeters) { _rtDownsampledMeters.Release(); Destroy(_rtDownsampledMeters); }
            _rtDownsampledMeters = new RenderTexture(outW, outH, 0, rtFmt)
            {
                filterMode = FilterMode.Point,
                useMipMap = false
            };
            _rtDownsampledMeters.Create();
        }

        // Convert to meters (millimeters * 0.001 if needed)
        _copyDepthMat.SetFloat(_MetersMul, sourceIsMillimeters ? 0.001f : 1.0f);
        Graphics.Blit(srcRT, _rtDownsampledMeters, _copyDepthMat);

        // GPU → CPU readback
        AsyncGPUReadback.Request(_rtDownsampledMeters, 0, request =>
        {
            if (request.hasError) return;
            var data = request.GetData<float>();
            if (depthMeters == null || depthMeters.Length != outW * outH)
                depthMeters = new float[outW * outH];
            for (int i = 0; i < depthMeters.Length; i++) depthMeters[i] = data[i];

            width = outW; height = outH;

            // Intrinsics approximation from projection at this resolution
            var P = xrCamera.projectionMatrix;
            fx = P[0,0] * (width * 0.5f);
            fy = P[1,1] * (height * 0.5f);
            cx = width * 0.5f;
            cy = height * 0.5f;
        });
    }

    public Matrix4x4 WorldFromCamera() =>
        xrCamera ? xrCamera.transform.localToWorldMatrix : Matrix4x4.identity;
}
