using System.Collections.Generic;
using UnityEngine;

/// Feed 4 world points (from your MRUK ray/selection) to build a quad mesh.
/// ❗ This script does NOT raycast. Call AddPoint(...) from your existing MRUK handler.
/// Controls:
/// - Clear points: call ClearPoints() (or press 'R' if you leave input code in)
[DisallowMultipleComponent]
public class PlaneFromFourClicks_External : MonoBehaviour
{
    [Header("Visualization")]
    public GameObject pointMarkerPrefab;   // optional: tiny sphere to show each picked point
    public Color edgeColor = Color.cyan;
    public float edgeWidth = 0.01f;

    [Header("Generated Mesh")]
    public Material meshMaterial;          // optional: Unlit/Texture etc.
    public Transform meshParent;           // optional parent
    public bool addMeshCollider = true;

    // runtime
    private readonly List<Vector3> _points = new();
    private LineRenderer _lr;
    private MeshRenderer _lastMeshRenderer;
    public GameObject LastPlane { get; private set; }

    public IReadOnlyList<Vector3> CurrentQuadWorldPoints => _points;

    void Awake()
    {
        var lrGo = new GameObject("[PlanePreview]");
        lrGo.transform.SetParent(transform, false);
        _lr = lrGo.AddComponent<LineRenderer>();
        _lr.positionCount = 0;
        _lr.startWidth = edgeWidth;
        _lr.endWidth = edgeWidth;
        _lr.useWorldSpace = true;
        _lr.loop = false;
        _lr.material = new Material(Shader.Find("Unlit/Color"));
        _lr.material.color = edgeColor;
    }

    void Update()
    {
        // optional: keyboard clear while testing in Editor
        if (Input.GetKeyDown(KeyCode.R)) ClearPoints();
        UpdatePreview();
    }

    /// Call this from your MRUK select handler with the hit.point
    public void AddPoint(Vector3 worldPoint)
    {
        if (_points.Count >= 4) ClearPoints();

        _points.Add(worldPoint);
        if (pointMarkerPrefab) Instantiate(pointMarkerPrefab, worldPoint, Quaternion.identity);

        if (_points.Count == 4) GeneratePlane(_points);
    }

    /// Overload if your event provides a RaycastHit
    public void AddPointFromHit(RaycastHit hit) => AddPoint(hit.point);

    public void ClearPoints()
    {
        _points.Clear();
        _lr.positionCount = 0;
    }

    private void UpdatePreview()
    {
        if (_points.Count == 0) { _lr.positionCount = 0; return; }

        if (_points.Count < 4)
        {
            _lr.positionCount = _points.Count;
            for (int i = 0; i < _points.Count; i++) _lr.SetPosition(i, _points[i]);
        }
        else
        {
            var ordered = OrderPointsOnPlane(_points);
            _lr.positionCount = 5;
            for (int i = 0; i < 4; i++) _lr.SetPosition(i, ordered[i]);
            _lr.SetPosition(4, ordered[0]);
        }
    }

    private void GeneratePlane(List<Vector3> pts)
    {
        if (pts.Count != 4) return;

        var ordered = OrderPointsOnPlane(pts);
        if (!IsWindingFacingCamera(ordered)) ordered.Reverse();

        var meshGO = new GameObject("GeneratedPlane");
        if (meshParent) meshGO.transform.SetParent(meshParent, true);
        var mf = meshGO.AddComponent<MeshFilter>();
        var mr = meshGO.AddComponent<MeshRenderer>();

        var mesh = new Mesh { name = "QuadFromClicks" };
        mesh.SetVertices(ordered);
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };

        // normalized UVs so any texture fills the quad
        mesh.uv = new[]
        {
            new Vector2(0,0), new Vector2(1,0),
            new Vector2(1,1), new Vector2(0,1)
        };

        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        mf.sharedMesh = mesh;
        if (meshMaterial) mr.sharedMaterial = meshMaterial;
        if (addMeshCollider) { var mc = meshGO.AddComponent<MeshCollider>(); mc.sharedMesh = mesh; }

        _lastMeshRenderer = mr;
        LastPlane = meshGO;

        Debug.Log("[PlaneFromFourClicks_External] Plane generated.");
    }

    // --- helpers ---
    private static List<Vector3> OrderPointsOnPlane(List<Vector3> pts)
    {
        var p0 = pts[0];
        var n = Vector3.Cross(pts[1] - p0, pts[2] - p0).normalized;
        if (n.sqrMagnitude < 1e-8f) n = Vector3.up;
        var u = (pts[1] - p0); if (u.sqrMagnitude < 1e-12f) u = Vector3.right; else u.Normalize();
        var v = Vector3.Cross(n, u).normalized;

        var centroid = Vector3.zero; foreach (var p in pts) centroid += p; centroid /= pts.Count;

        var withAngles = new List<(Vector3 p, float a)>(4);
        foreach (var p in pts)
        {
            var d = p - centroid;
            float du = Vector3.Dot(d, u);
            float dv = Vector3.Dot(d, v);
            withAngles.Add((p, Mathf.Atan2(dv, du)));
        }
        withAngles.Sort((x, y) => x.a.CompareTo(y.a));

        var ordered = new List<Vector3>(4);
        foreach (var t in withAngles) ordered.Add(t.p);
        return ordered;
    }

    private static bool IsWindingFacingCamera(List<Vector3> ordered)
    {
        var cam = Camera.main; if (!cam) return true;
        var n = Vector3.Cross(ordered[1] - ordered[0], ordered[2] - ordered[0]).normalized;
        var toCam = (cam.transform.position - ordered[0]).normalized;
        return Vector3.Dot(n, toCam) > 0f;
    }

    // Optional: make it easy to texture later
    public void ApplyTexture(Texture tex, bool createMaterialIfMissing = true)
    {
        if (_lastMeshRenderer == null) return;
        var mat = _lastMeshRenderer.sharedMaterial;
        if (mat == null && createMaterialIfMissing)
        {
            mat = new Material(Shader.Find("Unlit/Texture"));
            _lastMeshRenderer.sharedMaterial = mat;
        }
        if (mat != null) mat.mainTexture = tex;
    }
}
