// PassthroughUIPreviewOverlay.cs  (patched to follow head/camera each frame)
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PassthroughCameraSamples;

[DisallowMultipleComponent]
public class PassthroughUIPreviewOverlay : MonoBehaviour
{
    [Header("Passthrough")]
    public Texture passthroughTexture;                 // or leave null and use webCamTextureManager
    public WebCamTextureManager webCamTextureManager;  // optional
    public Camera passthroughProjectionCamera;         // MUST match passthrough FOV/pose

    [Header("(Optional) Auto-pull points from your plane")]
    public PlaneFromFourClicks_External planeClicker;  // NEW: if set, we read points every frame

    [Header("UI")]
    public RawImage preview;                           // RawImage that shows the passthrough
    public RectTransform overlayParent;                // child rect over the RawImage (full stretch)
    public GameObject markerUIPrefab;                  // tiny UI Image (circle), ~8x8, centered pivot
    public Vector2 markerSize = new Vector2(10,10);
    public Color markerColor = Color.red;

    [Header("Layout / Aspect")]
    [Tooltip("Treat the RawImage as preserving aspect even if your Unity version lacks RawImage.preserveAspect.")]
    public bool previewPreserveAspect = true;

    [Header("Orientation")]
    public bool autoOrientFromWebCam = true;
    public bool flipX = false, flipY = false, rotate90CW = false;

    [Header("Projection Mapping")]
    [Tooltip("Flip Y when converting viewport -> image (toggle if vertically inverted).")]
    public bool flipYForProjection = true;             // NEW: was hardcoded before

    private readonly List<Vector3> _worldPts = new();  // live world points
    private readonly List<RectTransform> _markers = new();

    private Texture _activeTex;
    private int _rotCCW;
    private bool _vertMirror;

    // ---- public API (still works if you prefer manual control) ----
    public void SetSourceTexture(Texture t){ passthroughTexture = t; RefreshActive(); RedrawAll(); }

    public void ClearMarkers()
    {
        _worldPts.Clear();
        EnsureMarkerCount(0);
    }

    public void AddMarkerForWorldPoint(Vector3 worldPoint)
    {
        _worldPts.Add(worldPoint);
        EnsureMarkerCount(_worldPts.Count);
        UpdateMarker(_worldPts.Count - 1);
    }

    void Awake()
    {
        if (overlayParent == null && preview != null) overlayParent = preview.rectTransform;
        RefreshActive(); 
        ApplyToRawImage();
    }

    // Update: keep source/orientation in sync
    void Update()
    {
        if (passthroughTexture == null && webCamTextureManager && webCamTextureManager.WebCamTexture)
        {
            var w = webCamTextureManager.WebCamTexture;
            if (_activeTex != w || _rotCCW != w.videoRotationAngle || _vertMirror != w.videoVerticallyMirrored)
            { 
                RefreshActive(); 
                ApplyToRawImage(); 
            }
        }
    }

    // LateUpdate: re-project every frame so markers follow head motion
    void LateUpdate()
    {
        // If a planeClicker is provided, mirror its points each frame
        if (planeClicker != null)
        {
            var pts = planeClicker.CurrentQuadWorldPoints;
            if (pts == null || pts.Count == 0)
            {
                EnsureMarkerCount(0);
                return;
            }

            // Keep our local list equal to the plane's list (no GC churn)
            SyncWorldPointsFromPlane(pts);
        }

        // Reposition markers for current camera pose
        RedrawAll();
    }

    // ---- internals ----

    void RefreshActive()
    {
        _activeTex = passthroughTexture;
        _rotCCW = 0; _vertMirror = false;

        if (_activeTex == null && webCamTextureManager && webCamTextureManager.WebCamTexture)
        {
            var w = webCamTextureManager.WebCamTexture;
            _activeTex = w;
            if (autoOrientFromWebCam){ _rotCCW = w.videoRotationAngle; _vertMirror = w.videoVerticallyMirrored; }
        }
    }

    void ApplyToRawImage(){ if (preview) preview.texture = _activeTex; }

    void EnsureMarkerCount(int n)
    {
        while (_markers.Count < n)
        {
            if (!markerUIPrefab || !overlayParent) return;
            var go = Instantiate(markerUIPrefab, overlayParent);
            var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
            rt.sizeDelta = markerSize;
            var img = go.GetComponent<Image>(); if (img) img.color = markerColor;
            _markers.Add(rt);
        }
        while (_markers.Count > n)
        {
            var i = _markers.Count - 1;
            Destroy(_markers[i].gameObject);
            _markers.RemoveAt(i);
        }
    }

    // keep _worldPts identical to plane's list without re-allocating each frame
    void SyncWorldPointsFromPlane(IReadOnlyList<Vector3> src)
    {
        // resize our list if needed
        int needed = src.Count;
        if (_worldPts.Count != needed)
        {
            if (_worldPts.Count < needed)
            {
                for (int i = _worldPts.Count; i < needed; i++) _worldPts.Add(src[i]);
            }
            else
            {
                _worldPts.RemoveRange(needed, _worldPts.Count - needed);
            }
            EnsureMarkerCount(needed);
        }
        // copy values
        for (int i = 0; i < needed; i++) _worldPts[i] = src[i];
    }

    void RedrawAll()
    {
        if (!_activeTex || !preview || !overlayParent || !passthroughProjectionCamera) return;

        int n = _worldPts.Count;
        EnsureMarkerCount(n);
        for (int i = 0; i < _markers.Count; i++)
        {
            if (i < n)
            {
                UpdateMarker(i);
                _markers[i].gameObject.SetActive(true);
            }
            else
            {
                _markers[i].gameObject.SetActive(false);
            }
        }
    }

    void UpdateMarker(int i)
    {
        if (i < 0 || i >= _worldPts.Count) return;

        // 1) world -> viewport (0..1)
        var v = passthroughProjectionCamera.WorldToViewportPoint(_worldPts[i]);
        float u = Mathf.Clamp01(v.x), t = Mathf.Clamp01(v.y);
        float projV = flipYForProjection ? (1f - t) : t;

        // 2) viewport -> source UV (apply device orientation + manual toggles)
        Vector2 uv = new Vector2(u, projV);
        int a = ((_rotCCW % 360) + 360) % 360; // CCW from WebCamTexture
        if (a == 90)      uv = new Vector2(1f - uv.y, u);
        else if (a == 180)uv = new Vector2(1f - u, 1f - projV);
        else if (a == 270)uv = new Vector2(projV, 1f - u);
        if (_vertMirror) uv.y = 1f - uv.y;

        if (rotate90CW){ var x=uv.x; var y=uv.y; uv = new Vector2(y, 1f - x); }
        if (flipX) uv.x = 1f - uv.x;
        if (flipY) uv.y = 1f - uv.y;

        // 3) UV -> anchored UI position inside the RawImage rect (respect aspect fit)
        _markers[i].anchoredPosition = UVToLocal(preview, uv, previewPreserveAspect);
    }

    // No RawImage.preserveAspect; use AspectRatioFitter OR the previewPreserveAspect toggle
    static Vector2 UVToLocal(RawImage ri, Vector2 uv, bool preserveAspectToggle)
    {
        var rt = ri.rectTransform; var rect = rt.rect;
        float W = rect.width, H = rect.height;
        var tex = ri.texture; if (!tex || W<=0 || H<=0) return Vector2.zero;

        float texAspect = (float)tex.width / Mathf.Max(1, tex.height);
        float rectAspect = W / Mathf.Max(1, H);

        var fitter = ri.GetComponent<AspectRatioFitter>();
        bool preserve = preserveAspectToggle || (fitter && fitter.aspectMode != AspectRatioFitter.AspectMode.None);

        float drawW = W, drawH = H;
        if (preserve)
        {
            if (texAspect > rectAspect){ drawW = W; drawH = W / texAspect; }
            else { drawH = H; drawW = H * texAspect; }
        }

        float offX = (W - drawW)*0.5f, offY = (H - drawH)*0.5f;
        float px = uv.x * drawW + offX, py = uv.y * drawH + offY;

        return new Vector2(px - W*0.5f, py - H*0.5f); // anchored (centered)
    }
}
