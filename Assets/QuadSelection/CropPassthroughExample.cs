using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PassthroughCameraSamples; // <-- keep this if you use the Meta sample passthrough provider

public class CropPassthroughExample : MonoBehaviour
{
    [Header("Refs (assign in Inspector)")]
    public PlaneFromFourClicks_External planeClicker;   // your 4-point picker
    public Camera passthroughProjectionCamera;          // camera matching passthrough FOV/pose
    [Tooltip("If left empty, it will try webCamTextureManager.WebCamTexture")]
    public Texture passthroughTexture;                  // optional static texture

    [Header("Passthrough Provider (optional)")]
    public WebCamTextureManager webCamTextureManager;   // <-- restored

    [Header("Warp Reference (Inspector)")]
    public Shader warpShader;       // drag QuadHomographyWarp.shader here
    public Material warpMaterial;   // OR drag a material that uses that shader

    [Header("UI Preview (optional)")]
    public RawImage preview;
    public bool previewSetNativeSize = true;
    public bool previewKeepAspect = true;

    [Header("Output Size")]
    public int outputWidth  = 512;
    public int outputHeight = 512;

    [Header("Behavior")]
    public bool autoCreateCropperIfMissing = true;

    private PassthroughCropper_External _cropper;

    void Awake()
    {
        // create the cropper with a direct reference to shader/material
        if (warpMaterial != null)
            _cropper = new PassthroughCropper_External(outputWidth, outputHeight, warpMaterial);
        else if (warpShader != null)
            _cropper = new PassthroughCropper_External(outputWidth, outputHeight, warpShader);
        else
        {
            var sh = Shader.Find("Hidden/QuadHomographyWarp");
            if (!sh)
            {
                Debug.LogError("Assign 'warpShader' or 'warpMaterial' in the Inspector. Shader.Find fallback may fail on device.");
            }
            else _cropper = new PassthroughCropper_External(outputWidth, outputHeight, sh);
        }

        // optional aspect-fit UI setup
        if (preview != null && previewKeepAspect)
        {
            var fitter = preview.GetComponent<AspectRatioFitter>();
            if (fitter == null) fitter = preview.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        }
    }

    void OnDestroy()
    {
        _cropper?.Dispose();
        _cropper = null;
    }

    [ContextMenu("Crop Current Quad From Passthrough")]
    public void CropNow()
    {
        if (planeClicker == null) { Debug.LogError("CropNow: 'planeClicker' not assigned."); return; }
        if (passthroughProjectionCamera == null) { Debug.LogError("CropNow: 'passthroughProjectionCamera' not assigned."); return; }

        var pts = planeClicker.CurrentQuadWorldPoints;
        if (pts == null || pts.Count != 4)
        {
            Debug.LogWarning($"CropNow: need 4 points first (have {(pts == null ? 0 : pts.Count)}).");
            return;
        }

        if (_cropper == null)
        {
            if (!autoCreateCropperIfMissing) { Debug.LogError("CropNow: _cropper is NULL."); return; }
            if (warpMaterial != null)
                _cropper = new PassthroughCropper_External(outputWidth, outputHeight, warpMaterial);
            else if (warpShader != null)
                _cropper = new PassthroughCropper_External(outputWidth, outputHeight, warpShader);
            else
            {
                var sh = Shader.Find("Hidden/QuadHomographyWarp");
                if (!sh) { Debug.LogError("CropNow: Assign warpShader or warpMaterial in Inspector."); return; }
                _cropper = new PassthroughCropper_External(outputWidth, outputHeight, sh);
            }
        }

        // pick whichever texture source is valid
        Texture source = passthroughTexture;
        if (source == null && webCamTextureManager != null)
            source = webCamTextureManager.WebCamTexture;

        if (source == null)
        {
            Debug.LogError("CropNow: no passthrough source. Assign 'passthroughTexture' or running 'webCamTextureManager'.");
            return;
        }

        // crop it
        var cropped = _cropper.Crop(passthroughProjectionCamera, pts, source, flipY: true);
        if (cropped == null)
        {
            Debug.LogError("CropNow: crop failed.");
            return;
        }

        // apply to generated plane
        planeClicker.ApplyTexture(cropped, createMaterialIfMissing: true);

        // preview in UI
        if (preview != null)
        {
            preview.texture = cropped;
            var fitter = preview.GetComponent<AspectRatioFitter>();
            if (fitter != null && previewKeepAspect)
                fitter.aspectRatio = (float)cropped.width / cropped.height;
            if (previewSetNativeSize)
                preview.SetNativeSize();
        }

        Debug.Log("CropNow: OK → applied to plane and preview.");
    }
}
