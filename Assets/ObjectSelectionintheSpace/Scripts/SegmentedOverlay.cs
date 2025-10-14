using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SegmentedOverlay : MonoBehaviour
{
    Mesh _mesh;
    MeshFilter _mf;
    MeshRenderer _mr;

    public void Initialize(Material mat)
    {
        _mf = GetComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>();
        _mesh = new Mesh { name = "SegmentMesh" };
        _mf.sharedMesh = _mesh;
        _mr.sharedMaterial = mat;
        SetActive(false);
    }

    public void SetTransform(Vector3 pos, Quaternion rot)
    {
        transform.SetPositionAndRotation(pos, rot);
    }

    public void SetActive(bool on) { if (_mr) _mr.enabled = on; }

    /// <summary>
    /// Builds a thin mesh in the overlay's LOCAL plane (z ~ 0).
    /// pts2D: (x,y,0) in local plane coords (seed frame).
    /// </summary>
    public void UpdateMeshLocal2D(List<Vector3> pts2D, float inflate = 0f)
    {
        if (pts2D == null || pts2D.Count < 3) { SetActive(false); return; }

        // ---- Convex hull (monotone chain) to avoid bow-ties ----
        var hull = ConvexHull2D(pts2D); // CCW order
        if (hull.Count < 3) { SetActive(false); return; }

        const float lift = 0.001f; // avoid z-fighting

        // centroid for optional inflate
        Vector2 centroid = Vector2.zero;
        foreach (var p in hull) centroid += p;
        centroid /= hull.Count;

        var verts = new Vector3[hull.Count];
        for (int i = 0; i < hull.Count; i++)
        {
            Vector2 p = hull[i];
            if (inflate > 0f)
            {
                var dir = (p - centroid).normalized;
                p += dir * inflate;
            }
            verts[i] = new Vector3(p.x, p.y, lift);
        }

        // triangle fan
        int triCount = hull.Count - 2;
        var tris = new int[triCount * 3];
        for (int t = 0; t < triCount; t++)
        {
            tris[t*3+0] = 0;
            tris[t*3+1] = t+1;
            tris[t*3+2] = t+2;
        }

        // UVs
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        foreach (var p in hull)
        {
            if (p.x < min.x) min.x = p.x;
            if (p.y < min.y) min.y = p.y;
            if (p.x > max.x) max.x = p.x;
            if (p.y > max.y) max.y = p.y;
        }
        Vector2 range = max - min;
        if (range.x < 1e-4f) range.x = 1f;
        if (range.y < 1e-4f) range.y = 1f;

        var uvs = new Vector2[hull.Count];
        for (int i = 0; i < hull.Count; i++)
        {
            var p = hull[i];
            uvs[i] = new Vector2((p.x - min.x)/range.x, (p.y - min.y)/range.y);
        }

        _mesh.Clear();
        _mesh.SetVertices(verts);
        _mesh.SetTriangles(tris, 0, true);
        _mesh.SetUVs(0, new List<Vector2>(uvs));
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
    }

    // === COMPAT SHIM ===
    // Old world-space API used by earlier segmenter scripts.
    // We convert inputs into local plane coords and delegate to UpdateMeshLocal2D.
    public void UpdateMesh(
        Vector3 origin, Vector3 n, Vector3 x, Vector3 y,
        List<Vector3> pts2D, List<Vector3> ptsWorldIgnored, float inflate = 0f)
    {
        // Move overlay to the seed frame (z = n, up = y)
        var rot = Quaternion.LookRotation(n, y);
        SetTransform(origin, rot);

        // pts2D are already plane coords; send straight to local builder
        UpdateMeshLocal2D(pts2D, inflate);
    }

    // --- Helpers ---
    // Monotone chain convex hull in 2D
    static List<Vector2> ConvexHull2D(List<Vector3> pts)
    {
        List<Vector2> p = new List<Vector2>(pts.Count);
        foreach (var v in pts) p.Add(new Vector2(v.x, v.y));
        p.Sort((a,b) => a.x == b.x ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        if (p.Count <= 2) return p;

        List<Vector2> lower = new();
        foreach (var v in p)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], v) <= 0f) lower.RemoveAt(lower.Count-1);
            lower.Add(v);
        }
        List<Vector2> upper = new();
        for (int i = p.Count - 1; i >= 0; i--)
        {
            var v = p[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], v) <= 0f) upper.RemoveAt(upper.Count-1);
            upper.Add(v);
        }
        lower.RemoveAt(lower.Count-1);
        upper.RemoveAt(upper.Count-1);
        lower.AddRange(upper);
        return lower;

        static float Cross(Vector2 a, Vector2 b, Vector2 c)
            => (b.x - a.x)*(c.y - a.y) - (b.y - a.y)*(c.x - a.x);
    }
}
