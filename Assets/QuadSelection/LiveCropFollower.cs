using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PassthroughCameraSamples; // if you use WebCamTextureManager from Meta samples

[DisallowMultipleComponent]
public class LiveCropFollower : MonoBehaviour
{
    [Header("Sources")]
    public PlaneFromFourClicks_External planeClicker;   // your 4 picked world points
    public Camera passthroughProjectionCamera;          // camera that matches passthrough FOV/pose

    [Tooltip("If set, this texture is used as the passthrough source. If null, we try WebCamTextureManager.WebCamTexture.")]
    public Texture passthroughTexture;                  // Texture/RenderTexture

    [Tooltip("Optional provider (Meta samples). If passthroughTexture is null, we read WebCamTexture from here.")]
    public WebCamTextureManager webCamTextureManager;

    [Header("Warp Reference")]
    public Shader warpShader;       // drag QuadHomographyWarp.shader here
    public Material warpMaterial;   // OR a material that uses that shader

    [Header("Output")]
    public int outputWidth  = 512;
    public int outputHeight = 512;

    [Tooltip("Apply the live RT to your generated plane (so the crop on the plane follows head motion).")]
    public bool applyToPlane = true;

    [Tooltip("Optional UI RawImage to preview the live crop.")]
    public RawImage preview;        // optional; will show the RT if assigned

    [Header("Orientation & Ordering")]
    [Tooltip("Honor WebCamTexture rotation/mirror (if provider is used).")]
    public bool autoOrientFromWebCam = true;

    [Tooltip("Extra manual orientation (applied after device flags).")]
    public bool flipX = false, flipY = false, rotate90CW = false;

    [Tooltip("Flip Y when converting viewport->image (toggle this if crop is vertically inverted).")]
    public bool flipYForProjection = true;

    [Tooltip("Order corners by screen-space TL,TR,BR,BL before mapping (usually best on HMDs).")]
    public bool orderByScreenSpace = true;

    // runtime
    private RenderTexture _outRT;
    private Material _mat;

    void Awake()
    {
        // 1) create output RT once
        _outRT = new RenderTexture(outputWidth, outputHeight, 0, RenderTextureFormat.ARGB32)
        {
            filterMode = FilterMode.Bilinear
        };
        _outRT.Create();

        // 2) create a warp material from a direct reference (avoid Shader.Find on device)
        if (warpMaterial != null)      _mat = new Material(warpMaterial);
        else if (warpShader != null)   _mat = new Material(warpShader);
        else
        {
            var sh = Shader.Find("Hidden/QuadHomographyWarp");
            if (!sh) Debug.LogError("LiveCropFollower: Assign warpShader or warpMaterial (QuadHomographyWarp).");
            else     _mat = new Material(sh);
        }

        if (preview) preview.texture = _outRT; // show live result in UI if provided
    }

    void OnDestroy()
    {
        if (_outRT) _outRT.Release();
        if (_outRT) Destroy(_outRT);
        if (_mat) Destroy(_mat);
    }

    void LateUpdate()
    {
        if (!planeClicker || !passthroughProjectionCamera || _mat == null || _outRT == null) return;

        var pts = planeClicker.CurrentQuadWorldPoints;
        if (pts == null || pts.Count != 4) return;

        // resolve source texture each frame (live feed)
        var src = passthroughTexture;
        if (src == null && webCamTextureManager && webCamTextureManager.WebCamTexture)
            src = webCamTextureManager.WebCamTexture;
        if (src == null) return;

        int w = src.width, h = src.height;
        if (w < 2 || h < 2) return;

        // 1) project world points -> image pixel coords TL,TR,BR,BL for current camera pose
        Vector2Int[] px = orderByScreenSpace
            ? OrderByScreenThenToPixels(passthroughProjectionCamera, pts, w, h, flipYForProjection)
            : ToPixelsAndOrderInImage(passthroughProjectionCamera, pts, w, h, flipYForProjection);

        // 2) pixels -> normalized UVs
        var uv = new Vector2[4];
        for (int i = 0; i < 4; i++)
        {
            uv[i] = new Vector2(
                Mathf.InverseLerp(0, w - 1, px[i].x),
                Mathf.InverseLerp(0, h - 1, px[i].y)
            );
        }

        // 3) apply orientation (webcam + manual)
        ApplyOrientationArray(uv);

        // 4) set corners into the shader TL,TR,BR,BL and blit
        _mat.SetTexture("_MainTex", src);
        _mat.SetVector("_P0", new Vector4(uv[0].x, uv[0].y, 0, 0)); // TL
        _mat.SetVector("_P1", new Vector4(uv[1].x, uv[1].y, 0, 0)); // TR
        _mat.SetVector("_P2", new Vector4(uv[2].x, uv[2].y, 0, 0)); // BR
        _mat.SetVector("_P3", new Vector4(uv[3].x, uv[3].y, 0, 0)); // BL

        Graphics.Blit(null, _outRT, _mat);

        // 5) drive your plane with the live RT
        if (applyToPlane)
            planeClicker.ApplyTexture(_outRT, createMaterialIfMissing: true);
    }

    // ---------- orientation helpers ----------
    void ApplyOrientationArray(Vector2[] uv)
    {
        int rotCCW = 0;
        bool vertMirror = false;

        if (autoOrientFromWebCam && webCamTextureManager && webCamTextureManager.WebCamTexture)
        {
            var wct = webCamTextureManager.WebCamTexture;
            rotCCW = ((wct.videoRotationAngle % 360) + 360) % 360; // 0/90/180/270 CCW
            vertMirror = wct.videoVerticallyMirrored;
        }

        for (int i = 0; i < uv.Length; i++)
        {
            // device rotation (CCW) mapped into UV transforms
            if (rotCCW == 90)       uv[i] = new Vector2(1f - uv[i].y, uv[i].x);
            else if (rotCCW == 180) uv[i] = new Vector2(1f - uv[i].x, 1f - uv[i].y);
            else if (rotCCW == 270) uv[i] = new Vector2(uv[i].y, 1f - uv[i].x);

            if (vertMirror) uv[i].y = 1f - uv[i].y; // device vertical mirror

            if (rotate90CW) { var x = uv[i].x; var y = uv[i].y; uv[i] = new Vector2(y, 1f - x); }
            if (flipX) uv[i].x = 1f - uv[i].x;
            if (flipY) uv[i].y = 1f - uv[i].y;
        }
    }

    // ---------- projection helpers ----------
    static Vector2Int[] OrderByScreenThenToPixels(Camera cam, IReadOnlyList<Vector3> worldPts, int w, int h, bool flipY)
    {
        var screen = new System.Tuple<Vector3,int>[4];
        for (int i = 0; i < 4; i++) screen[i] = new System.Tuple<Vector3,int>(cam.WorldToScreenPoint(worldPts[i]), i);

        // Unity screen origin is bottom-left → sort by y desc (top first)
        System.Array.Sort(screen, (a, b) => b.Item1.y.CompareTo(a.Item1.y));
        var top2 = new[] { screen[0], screen[1] };
        var bot2 = new[] { screen[2], screen[3] };
        System.Array.Sort(top2, (a, b) => a.Item1.x.CompareTo(b.Item1.x)); // left→right
        System.Array.Sort(bot2, (a, b) => a.Item1.x.CompareTo(b.Item1.x)); // left→right

        int[] orderIdx = { top2[0].Item2, top2[1].Item2, bot2[1].Item2, bot2[0].Item2 }; // TL,TR,BR,BL
        var outPix = new Vector2Int[4];

        for (int o = 0; o < 4; o++)
        {
            int i = orderIdx[o];
            var v = cam.WorldToViewportPoint(worldPts[i]);
            float U = Mathf.Clamp01(v.x);
            float V = Mathf.Clamp01(v.y);
            if (flipY) V = 1f - V;
            outPix[o] = new Vector2Int(Mathf.RoundToInt(U * (w - 1)), Mathf.RoundToInt(V * (h - 1)));
        }
        return outPix;
    }

    static Vector2Int[] ToPixelsAndOrderInImage(Camera cam, IReadOnlyList<Vector3> worldPts, int w, int h, bool flipY)
    {
        var px = new Vector2Int[4];
        for (int i = 0; i < 4; i++)
        {
            var v = cam.WorldToViewportPoint(worldPts[i]);
            float U = Mathf.Clamp01(v.x);
            float V = Mathf.Clamp01(v.y);
            if (flipY) V = 1f - V;
            px[i] = new Vector2Int(Mathf.RoundToInt(U * (w - 1)), Mathf.RoundToInt(V * (h - 1)));
        }
        // TL,TR,BR,BL by image space
        System.Array.Sort(px, (a, b) => a.y.CompareTo(b.y)); // small y = top (after optional flipY)
        var top2 = new[] { px[0], px[1] };
        var bot2 = new[] { px[2], px[3] };
        System.Array.Sort(top2, (a, b) => a.x.CompareTo(b.x));
        System.Array.Sort(bot2, (a, b) => a.x.CompareTo(b.x));
        return new[] { top2[0], top2[1], bot2[1], bot2[0] };
    }
}
