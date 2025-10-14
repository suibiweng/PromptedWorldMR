using UnityEngine;
using UnityEngine.Rendering; // for TextureDimension

/// Pulls the system Environment Depth from the global shader texture "_EnvironmentDepthTexture"
/// (a Texture2DArray with 2 slices: left/right), and exposes a 2D RFloat RT for CPU ROI reads.
[DefaultExecutionOrder(-200)]
public class EnvironmentDepthBinderEDM : MonoBehaviour
{
    public enum EyeSource { Left = 0, Right = 1, CenterAverage = 2 }

    [Header("Source (global)")]
    [Tooltip("Global shader property name set by EnvironmentDepthManager")]
    public string globalDepthProperty = "_EnvironmentDepthTexture";

    [Header("Options")]
    public EyeSource eyeSource = EyeSource.CenterAverage;

    [Header("Output (assign to DepthSegmentationOverlay.depthRT, or let overlay auto-find)")]
    public RenderTexture outputRFloatRT; // 2D RFloat, meters in R

    [Header("Debug")]
    public bool logBindOnce = true;
    public bool logResize = true;
    public bool logMissingTexture = false;

    Material _copyMat;
    static readonly int _PropID = Shader.PropertyToID("_EnvironmentDepthTexture");
    static readonly int _SourceTexID = Shader.PropertyToID("_Source");
    static readonly int _SliceID     = Shader.PropertyToID("_Slice");
    static readonly int _SliceBID    = Shader.PropertyToID("_SliceB");

    void Awake()
    {
        // Fallback to default name if user changed the string field to empty
        if (string.IsNullOrWhiteSpace(globalDepthProperty))
            globalDepthProperty = "_EnvironmentDepthTexture";

        var sh = Shader.Find("Hidden/CopyDepthArraySliceToRFloat");
        if (!sh)
        {
            Debug.LogError("[EDM Binder] Missing shader 'Hidden/CopyDepthArraySliceToRFloat'.");
        }
        else
        {
            _copyMat = new Material(sh);
        }
    }

    void OnDestroy()
    {
        if (_copyMat) Destroy(_copyMat);
        if (outputRFloatRT) { outputRFloatRT.Release(); Destroy(outputRFloatRT); }
    }

    void LateUpdate()
    {
        if (_copyMat == null) return;

        // Get the global depth texture set by EnvironmentDepthManager
        // Note: using property name string so it matches your project setting;
        // fallback ID is kept for convenience.
        Texture tex = Shader.GetGlobalTexture(globalDepthProperty);
        if (!tex) tex = Shader.GetGlobalTexture(_PropID);
        if (!tex)
        {
            if (logMissingTexture)
                Debug.Log("[EDM Binder] Global _EnvironmentDepthTexture not set yet (waiting for EnvironmentDepthManager).");
            return;
        }

        // Must be a Texture2DArray RenderTexture (two slices: L/R)
        if (tex is not RenderTexture rtArr || rtArr.dimension != TextureDimension.Tex2DArray)
        {
            Debug.LogWarning($"[EDM Binder] Global depth is not a Texture2DArray RenderTexture (got {tex.GetType().Name}).");
            return;
        }
        if (!rtArr.IsCreated()) return; // can be not ready for a few frames

        int w = rtArr.width;
        int h = rtArr.height;
        EnsureOutput(w, h);

        _copyMat.SetTexture(_SourceTexID, rtArr);

        if (eyeSource == EyeSource.CenterAverage)
        {
            // Average left(0) and right(1) slices
            _copyMat.SetFloat(_SliceID, 0f);
            _copyMat.SetFloat(_SliceBID, 1f);
            Graphics.Blit(null, outputRFloatRT, _copyMat, 1); // pass 1 = average
        }
        else
        {
            int slice = (eyeSource == EyeSource.Left) ? 0 : 1;
            _copyMat.SetFloat(_SliceID, slice);
            Graphics.Blit(null, outputRFloatRT, _copyMat, 0); // pass 0 = single slice copy
        }

        if (logBindOnce)
        {
            logBindOnce = false;
            Debug.Log($"[EDM Binder] Bound global depth: Tex2DArray {w}x{h}x2 → 2D RFloat {outputRFloatRT.width}x{outputRFloatRT.height} ({eyeSource})");
        }
    }

    void EnsureOutput(int w, int h)
    {
        if (outputRFloatRT && outputRFloatRT.width == w && outputRFloatRT.height == h) return;

        if (outputRFloatRT)
        {
            outputRFloatRT.Release();
            Destroy(outputRFloatRT);
        }
        outputRFloatRT = new RenderTexture(w, h, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
        {
            name = "EnvDepth_RFloat_2D",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        outputRFloatRT.Create();

        if (logResize)
            Debug.Log($"[EDM Binder] Output resized to {w}x{h}");
    }
}
