using System.Collections.Generic;
using UnityEngine;

/// Depth-driven segmentation & overlay mesh.
/// Reads a small ROI from environment depth (RFloat meters),
/// segments by depth continuity, back-projects to world, overlays a mesh.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class DepthSegmentationOverlay : MonoBehaviour
{
    [Header("Inputs")]
    public Camera arCamera;                     // HMD/CenterEye camera
    public RenderTexture depthRT;              // 2D RFloat (meters) provided by binder
    public Material highlightMaterial;         // Transparent overlay material

    [Header("Auto / Fallback")]
    public bool autoFindCamera = true;         // Camera.main if missing
    public bool autoFindBinderEveryCall = true;// Pull from EnvironmentDepthBinderEDM if missing

    [Header("Segmentation Params")]
    public int roiSize = 128;                  // square ROI in screen pixels
    public float depthThreshold = 0.03f;       // meters from seed depth
    public int downsample = 2;                 // ROI downsample for CPU read

    [Header("Post")]
    public float surfaceLift = 0.001f;         // avoid z-fighting along normal

    // runtime
    Mesh _mesh;
    MeshFilter _mf;
    MeshRenderer _mr;
    Texture2D _cpuDepth;                       // RFloat CPU texture
    readonly List<Vector2> _maskPtsPx = new();
    readonly List<Vector3> _loopWS = new();

    void Awake()
    {
        _mf = GetComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>();
        _mesh = new Mesh { name = "DepthSegMesh" };
        _mf.sharedMesh = _mesh;
        if (highlightMaterial) _mr.sharedMaterial = highlightMaterial;
        if (!arCamera && autoFindCamera) arCamera = Camera.main;

        // initial binder grab (optional; also done each call if needed)
        if (!depthRT && autoFindBinderEveryCall)
        {
            var binder = FindObjectOfType<EnvironmentDepthBinderEDM>(true);
            if (binder && binder.outputRFloatRT) depthRT = binder.outputRFloatRT;
        }

        _mr.enabled = false;
    }

    public void Hide()
    {
        if (_mr) _mr.enabled = false;
    }

    public void SegmentAt(Vector3 seedWorld, Vector3 seedNormal)
    {
        if (!arCamera) arCamera = Camera.main;

        // Pull from binder on demand so it works as soon as EDM starts producing frames
        if (!depthRT && autoFindBinderEveryCall)
        {
            var binder = FindObjectOfType<EnvironmentDepthBinderEDM>(true);
            if (binder && binder.outputRFloatRT && binder.outputRFloatRT.width > 0) depthRT = binder.outputRFloatRT;
        }
        if (!depthRT) { /* Debug.LogWarning("[DepthSeg] depthRT not set."); */ return; }

        // 1) Seed to screen
        Vector3 sp = arCamera.WorldToScreenPoint(seedWorld);
        if (sp.z <= 0f) return;

        // 2) ROI (clamped)
        int sz = Mathf.Max(32, roiSize);
        int cx = Mathf.RoundToInt(sp.x);
        int cy = Mathf.RoundToInt(sp.y);
        RectInt roi = new RectInt(cx - sz/2, cy - sz/2, sz, sz);
        ClampROI(ref roi, depthRT.width, depthRT.height);

        // 3) Read ROI into CPU RFloat texture
        if (!ReadDepthROI(depthRT, roi, Mathf.Max(1, downsample), ref _cpuDepth)) return;
        int W = _cpuDepth.width, H = _cpuDepth.height;

        // 4) Seed depth (ROI center)
        int scx = W / 2, scy = H / 2;
        float seedDepth = SampleDepth(_cpuDepth, scx, scy);
        if (seedDepth <= 0f) return;

        // 5) Build mask by depth continuity (sparse to keep hull cheap)
        _maskPtsPx.Clear();
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float d = SampleDepth(_cpuDepth, x, y);
                if (d > 0f && Mathf.Abs(d - seedDepth) <= depthThreshold)
                {
                    if (((x ^ y) & 1) == 0) _maskPtsPx.Add(new Vector2(x, y)); // keep half
                }
            }
        }
        if (_maskPtsPx.Count < 3) { _mr.enabled = false; return; }

        // 6) Hull in pixel space (fast/robust). Swap for marching-squares for concave shapes if needed.
        List<Vector2> hull = ConvexHull(_maskPtsPx);
        if (hull.Count < 3) { _mr.enabled = false; return; }

        // 7) Back-project hull to world
        _loopWS.Clear();
        Vector3 camPos = arCamera.transform.position;
        for (int i = 0; i < hull.Count; i++)
        {
            float sx = roi.x + (hull[i].x + 0.5f) * downsample;
            float sy = roi.y + (hull[i].y + 0.5f) * downsample;

            int rx = Mathf.Clamp(Mathf.RoundToInt(hull[i].x), 0, W - 1);
            int ry = Mathf.Clamp(Mathf.RoundToInt(hull[i].y), 0, H - 1);
            float d = SampleDepth(_cpuDepth, rx, ry);
            if (d <= 0f) d = seedDepth;

            Ray r = arCamera.ScreenPointToRay(new Vector3(sx, sy, 0f));
            Vector3 p = camPos + r.direction.normalized * d;
            p += seedNormal.normalized * surfaceLift;

            _loopWS.Add(p);
        }

        // 8) Mesh (world-space fan)
        BuildFanMeshWorld(_loopWS);
        _mr.enabled = true;
    }

    // --- Helpers ---

    static void ClampROI(ref RectInt roi, int W, int H)
    {
        int x = Mathf.Clamp(roi.x, 0, W - 2);
        int y = Mathf.Clamp(roi.y, 0, H - 2);
        int w = Mathf.Clamp(roi.width, 2, W - x);
        int h = Mathf.Clamp(roi.height, 2, H - y);
        roi = new RectInt(x, y, w, h);
    }

    static bool ReadDepthROI(RenderTexture src, RectInt roi, int ds, ref Texture2D cpuTex)
    {
        int w = Mathf.Max(2, roi.width / ds);
        int h = Mathf.Max(2, roi.height / ds);

        var prev = RenderTexture.active;
        var tmp = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
        tmp.filterMode = FilterMode.Point;
        try
        {
            Graphics.SetRenderTarget(tmp);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, w, 0, h);
            Graphics.DrawTexture(new Rect(0, 0, w, h), src, new Rect(
                (float)roi.x / src.width,
                (float)roi.y / src.height,
                (float)roi.width / src.width,
                (float)roi.height / src.height
            ), 0, 0, 0, 0);
            GL.PopMatrix();

            if (cpuTex == null || cpuTex.width != w || cpuTex.height != h)
                cpuTex = new Texture2D(w, h, TextureFormat.RFloat, false, true);

            cpuTex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            cpuTex.Apply(false, false);
            return true;
        }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(tmp);
        }
    }

    static float SampleDepth(Texture2D tex, int x, int y)
    {
        x = Mathf.Clamp(x, 0, tex.width - 1);
        y = Mathf.Clamp(y, 0, tex.height - 1);
        return tex.GetPixel(x, y).r; // meters in R
    }

    void BuildFanMeshWorld(List<Vector3> loopWS)
    {
        if (loopWS == null || loopWS.Count < 3) return;

        // centroid
        Vector3 c = Vector3.zero;
        for (int i = 0; i < loopWS.Count; i++) c += loopWS[i];
        c /= loopWS.Count;

        // plane basis (for angle sort)
        Vector3 n = Vector3.zero;
        for (int i = 0; i < loopWS.Count; i++)
        {
            Vector3 a = loopWS[i] - c;
            Vector3 b = loopWS[(i + 1) % loopWS.Count] - c;
            n += Vector3.Cross(a, b);
        }
        n.Normalize();
        Vector3 x = Vector3.Normalize(Vector3.Cross(Vector3.up, n));
        if (x.sqrMagnitude < 1e-6f) x = Vector3.Normalize(Vector3.Cross(Vector3.right, n));
        Vector3 y = Vector3.Cross(n, x);

        // angle sort
        var idx = new List<int>(loopWS.Count);
        for (int i = 0; i < loopWS.Count; i++) idx.Add(i);
        idx.Sort((a, b) =>
        {
            Vector3 va = loopWS[a] - c; Vector3 vb = loopWS[b] - c;
            float ax = Vector3.Dot(va, x), ay = Vector3.Dot(va, y);
            float bx = Vector3.Dot(vb, x), by = Vector3.Dot(vb, y);
            float aa = Mathf.Atan2(ay, ax), bb = Mathf.Atan2(by, bx);
            return aa.CompareTo(bb);
        });

        // write mesh: center + loop
        int V = 1 + idx.Count;
        var verts = new Vector3[V];
        var uvs = new Vector2[V];
        verts[0] = c;
        uvs[0] = Vector2.zero;

        for (int k = 0; k < idx.Count; k++)
        {
            Vector3 p = loopWS[idx[k]];
            verts[k + 1] = p;

            Vector3 v = p - c;
            float ux = Vector3.Dot(v, x), uy = Vector3.Dot(v, y);
            uvs[k + 1] = new Vector2(ux, uy);
        }

        // triangles (fan)
        int triCount = idx.Count;
        var tris = new int[triCount * 3];
        for (int t = 0; t < triCount; t++)
        {
            tris[3 * t + 0] = 0;
            tris[3 * t + 1] = 1 + t;
            tris[3 * t + 2] = 1 + ((t + 1) % idx.Count);
        }

        _mesh.Clear();
        _mesh.vertices = verts;
        _mesh.uv = uvs;
        _mesh.triangles = tris;
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();

        // keep identity so verts are interpreted in world space
        transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    // 2D convex hull (monotone chain)
    static List<Vector2> ConvexHull(List<Vector2> pts)
    {
        if (pts.Count <= 1) return new List<Vector2>(pts);
        pts.Sort((a,b) => a.x == b.x ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        List<Vector2> lower = new();
        foreach (var p in pts)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 0) lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }
        List<Vector2> upper = new();
        for (int i = pts.Count - 1; i >= 0; i--)
        {
            var p = pts[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 0) upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;

        static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x)*(c.y - a.y) - (b.y - a.y)*(c.x - a.x);
    }
}
