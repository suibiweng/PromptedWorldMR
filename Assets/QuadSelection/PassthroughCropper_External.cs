using System.Collections.Generic;
using UnityEngine;

public class PassthroughCropper_External : System.IDisposable
{
    // removed 'readonly' so we can assign in Init()
    private Material _warpMat;
    private RenderTexture _outRT;

    public int OutputWidth  { get; private set; }
    public int OutputHeight { get; private set; }

    // Original ctor (kept, but throws if shader not found)
    public PassthroughCropper_External(int outW = 512, int outH = 512)
    {
        var sh = Shader.Find("Hidden/QuadHomographyWarp");
        if (sh == null)
            throw new System.ArgumentException("Warp shader not found. Assign a Shader/Material via the other constructor, or ensure 'Hidden/QuadHomographyWarp' exists.");
        Init(outW, outH, new Material(sh));
    }

    // Preferred: pass a Shader from the Inspector
    public PassthroughCropper_External(int outW, int outH, Shader warpShader)
    {
        if (warpShader == null) throw new System.ArgumentNullException(nameof(warpShader));
        Init(outW, outH, new Material(warpShader));
    }

    // Or pass a Material that already uses the shader
    public PassthroughCropper_External(int outW, int outH, Material warpMaterial)
    {
        if (warpMaterial == null) throw new System.ArgumentNullException(nameof(warpMaterial));
        Init(outW, outH, new Material(warpMaterial)); // instance
    }

    private void Init(int outW, int outH, Material mat)
    {
        OutputWidth  = outW;
        OutputHeight = outH;

        _warpMat = mat;

        _outRT = new RenderTexture(outW, outH, 0, RenderTextureFormat.ARGB32)
        {
            filterMode = FilterMode.Bilinear
        };
        _outRT.Create();
    }

    public Texture2D Crop(Camera cam, IReadOnlyList<Vector3> worldQuad, Texture passthroughTex, bool flipY = true)
    {
        if (cam == null || worldQuad == null || worldQuad.Count != 4 || passthroughTex == null || _warpMat == null)
        {
            Debug.LogWarning("[PassthroughCropper_External] Invalid inputs or warp material missing.");
            return null;
        }

        // Readable CPU texture for sampling
        var readable = (passthroughTex is Texture2D t2d && t2d.isReadable) ? t2d : TextureReadback.ToReadable(passthroughTex);

        // Project world → image pixels
        var px = WorldPointsToImagePixels(cam, worldQuad, readable.width, readable.height, flipY);

        // Order TL,TR,BR,BL in image space
        var ordered = OrderImageQuadTLTRBRBL(px);

        // Set normalized UV corners
        Vector2[] uv = new Vector2[4];
        for (int i = 0; i < 4; i++)
        {
            uv[i] = new Vector2(
                Mathf.InverseLerp(0, readable.width  - 1, ordered[i].x),
                Mathf.InverseLerp(0, readable.height - 1, ordered[i].y)
            );
        }

        _warpMat.SetTexture("_MainTex", readable);
        _warpMat.SetVector("_P0", new Vector4(uv[0].x, uv[0].y, 0, 0));
        _warpMat.SetVector("_P1", new Vector4(uv[1].x, uv[1].y, 0, 0));
        _warpMat.SetVector("_P2", new Vector4(uv[2].x, uv[2].y, 0, 0));
        _warpMat.SetVector("_P3", new Vector4(uv[3].x, uv[3].y, 0, 0));

        // GPU warp → RT
        Graphics.Blit(null, _outRT, _warpMat);

        // Readback to Texture2D
        var prev = RenderTexture.active;
        RenderTexture.active = _outRT;
        var outTex = new Texture2D(OutputWidth, OutputHeight, TextureFormat.RGBA32, false, false);
        outTex.ReadPixels(new Rect(0, 0, OutputWidth, OutputHeight), 0, 0);
        outTex.Apply(false, false);
        RenderTexture.active = prev;

        return outTex;
    }

    public void Dispose()
    {
        if (_outRT) _outRT.Release();
        if (_outRT) Object.Destroy(_outRT);
        if (_warpMat) Object.Destroy(_warpMat);
    }

    // ---------- helpers ----------
    private static Vector2Int[] WorldPointsToImagePixels(Camera cam, IReadOnlyList<Vector3> worldPts, int w, int h, bool flipY)
    {
        var outPix = new Vector2Int[worldPts.Count];
        for (int i = 0; i < worldPts.Count; i++)
        {
            Vector3 v = cam.WorldToViewportPoint(worldPts[i]);
            float u = Mathf.Clamp01(v.x);
            float vv = Mathf.Clamp01(v.y);
            if (flipY) vv = 1f - vv;

            outPix[i] = new Vector2Int(
                Mathf.RoundToInt(u  * (w - 1)),
                Mathf.RoundToInt(vv * (h - 1))
            );
        }
        return outPix;
    }

    public static Vector2Int[] OrderImageQuadTLTRBRBL(Vector2Int[] quadPx)
    {
        var arr = (Vector2Int[])quadPx.Clone();
        System.Array.Sort(arr, (a, b) => a.y.CompareTo(b.y)); // small y = top
        var top2 = new[] { arr[0], arr[1] };
        var bot2 = new[] { arr[2], arr[3] };
        System.Array.Sort(top2, (a, b) => a.x.CompareTo(b.x)); // left→right
        System.Array.Sort(bot2, (a, b) => a.x.CompareTo(b.x));
        return new[] { top2[0], top2[1], bot2[1], bot2[0] };   // TL, TR, BR, BL
    }
}
