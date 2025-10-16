using System.Collections.Generic;
using UnityEngine;

public class PassthroughCropper_External : System.IDisposable
{
    readonly Material _warpMat;
    readonly RenderTexture _outRT;

    public int OutputWidth  { get; }
    public int OutputHeight { get; }

    /// outW/outH = resolution of the rectified crop you want (e.g., 512x512)
    public PassthroughCropper_External(int outW = 512, int outH = 512)
    {
        OutputWidth  = outW;
        OutputHeight = outH;
        _warpMat = new Material(Shader.Find("Hidden/QuadHomographyWarp"));
        _outRT   = new RenderTexture(outW, outH, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Bilinear };
        _outRT.Create();
    }

    /// worldQuad: the 4 world points (any order). cam must match your passthrough render pose/FOV.
    /// passthroughTex: the texture you render for passthrough (Texture or RenderTexture).
    public Texture2D Crop(Camera cam, IReadOnlyList<Vector3> worldQuad, Texture passthroughTex, bool flipY = true)
    {
        if (cam == null || worldQuad == null || worldQuad.Count != 4 || passthroughTex == null)
        {
            Debug.LogWarning("[PassthroughCropper_External] Invalid inputs.");
            return null;
        }

        // 1) ensure we have a readable CPU texture of the passthrough frame
        var readable = (passthroughTex is Texture2D t2d && t2d.isReadable) ? t2d : TextureReadback.ToReadable(passthroughTex);

        // 2) project world points -> viewport -> image pixels
        var px = WorldPointsToImagePixels(cam, worldQuad, readable.width, readable.height, flipY);

        // 3) order image-space quad TL,TR,BR,BL
        var ordered = OrderImageQuadTLTRBRBL(px);

        // 4) set shader params as normalized UVs
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

        // 5) GPU warp to RT, then read back to Texture2D (CPU)
        Graphics.Blit(null, _outRT, _warpMat);

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

    static Vector2Int[] WorldPointsToImagePixels(Camera cam, IReadOnlyList<Vector3> worldPts, int w, int h, bool flipY)
    {
        var outPix = new Vector2Int[worldPts.Count];
        for (int i = 0; i < worldPts.Count; i++)
        {
            Vector3 v = cam.WorldToViewportPoint(worldPts[i]);
            float u = Mathf.Clamp01(v.x);
            float vv = Mathf.Clamp01(v.y);
            if (flipY) vv = 1f - vv; // many camera frames appear vertically flipped

            int x = Mathf.RoundToInt(u  * (w - 1));
            int y = Mathf.RoundToInt(vv * (h - 1));
            outPix[i] = new Vector2Int(x, y);
        }
        return outPix;
    }

    static Vector2Int[] OrderImageQuadTLTRBRBL(Vector2Int[] quadPx)
    {
        var arr = (Vector2Int[])quadPx.Clone();
        System.Array.Sort(arr, (a, b) => a.y.CompareTo(b.y)); // top two first (smaller y is top in image space)
        var top2 = new[] { arr[0], arr[1] };
        var bot2 = new[] { arr[2], arr[3] };
        System.Array.Sort(top2, (a, b) => a.x.CompareTo(b.x)); // left-to-right
        System.Array.Sort(bot2, (a, b) => a.x.CompareTo(b.x));
        return new[] { top2[0], top2[1], bot2[1], bot2[0] }; // TL, TR, BR, BL
    }
}
