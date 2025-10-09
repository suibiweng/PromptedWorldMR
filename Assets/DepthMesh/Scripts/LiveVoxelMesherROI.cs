// Assets/DepthMesh/Scripts/LiveVoxelMesherROI.cs
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class LiveVoxelMesherROI : MonoBehaviour
{
    [Header("Depth input")]
    public QuestDepthProvider_Oculus_Meta depthProvider;
    public float maxDepthMeters = 6f;

    [Header("Voxel volume (world)")]
    public float voxelSize = 0.03f;
    public Vector3 volumeCenterOffset = new(0, 0, 1.2f);
    public Vector3Int volumeVoxels = new(128, 96, 128);

    [Header("Update cadence")]
    public int integrateEveryNFrames = 2;
    public int remeshEveryNIntegrations = 4;

    [Header("Render")]
    public Material meshMaterial;

    HashSet<Vector3Int> solid = new();
    HashSet<Vector3Int> roi = new();

    MeshFilter mf; MeshRenderer mr;
    int frameCount, integratesSinceRemesh;

    void Start()
    {
        mf = gameObject.AddComponent<MeshFilter>();
        mr = gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = meshMaterial ? meshMaterial : new Material(Shader.Find("Universal Render Pipeline/Lit"));
    }

    public void ClearAll() { solid.Clear(); roi.Clear(); mf.sharedMesh = null; }

    public void PaintSphere(Vector3 worldCenter, float radius, bool add = true)
    {
        VolumeWorld(out var volMinW, out _);
        int rx = Mathf.CeilToInt(radius / voxelSize);
        var baseIdx = WorldToIndex(worldCenter, volMinW);

        for (int z = -rx; z <= rx; z++)
        for (int y = -rx; y <= rx; y++)
        for (int x = -rx; x <= rx; x++)
        {
            var vi = new Vector3Int(baseIdx.x + x, baseIdx.y + y, baseIdx.z + z);
            if (!InBounds(vi)) continue;
            Vector3 pW = IndexToWorld(vi, volMinW);
            if ((pW - worldCenter).sqrMagnitude <= radius * radius)
            {
                if (add) roi.Add(vi); else roi.Remove(vi);
            }
        }
    }

    void Update()
    {
        if (!depthProvider || depthProvider.depthMeters == null || depthProvider.width == 0) return;
        if (roi.Count == 0) return;

        frameCount++;
        if (frameCount % integrateEveryNFrames == 0)
        {
            IntegrateDepthInROI();
            integratesSinceRemesh++;
            if (integratesSinceRemesh >= remeshEveryNIntegrations)
            {
                RebuildMesh();
                integratesSinceRemesh = 0;
            }
        }
    }

    void IntegrateDepthInROI()
    {
        VolumeWorld(out var volMinW, out _);
        int W = depthProvider.width, H = depthProvider.height;
        var D = depthProvider.depthMeters;
        float fx = depthProvider.fx, fy = depthProvider.fy, cx = depthProvider.cx, cy = depthProvider.cy;
        var W_C = depthProvider.WorldFromCamera();

        int stride = 2;
        for (int v = 0; v < H; v += stride)
        for (int u = 0; u < W; u += stride)
        {
            float z = D[v * W + u];
            if (z <= 0.2f || z > maxDepthMeters) continue;

            float x = (u - cx) * z / fx;
            float y = (v - cy) * z / fy;
            Vector3 Pc = new(x, -y, z);
            Vector3 Pw = W_C.MultiplyPoint3x4(Pc);

            var vi = WorldToIndex(Pw, volMinW);
            if (!InBounds(vi)) continue;
            if (roi.Contains(vi)) solid.Add(vi);
        }
    }

    void RebuildMesh()
    {
        var verts = new List<Vector3>();
        var tris  = new List<int>();

        VolumeWorld(out var volMinW, out _);

        Vector3Int[] N = { new(1,0,0), new(-1,0,0), new(0,1,0), new(0,-1,0), new(0,0,1), new(0,0,-1) };
        Vector3[][] F = {
            new[]{ new Vector3(1,0,0), new(1,0,1), new(1,1,1), new(1,1,0) }, // +X
            new[]{ new Vector3(0,0,0), new(0,1,0), new(0,1,1), new(0,0,1) }, // -X
            new[]{ new Vector3(0,1,0), new(0,1,1), new(1,1,1), new(1,1,0) }, // +Y
            new[]{ new Vector3(0,0,0), new(1,0,0), new(1,0,1), new(0,0,1) }, // -Y
            new[]{ new Vector3(0,0,1), new(0,1,1), new(1,1,1), new(1,0,1) }, // +Z
            new[]{ new Vector3(0,0,0), new(1,0,0), new(1,1,0), new(0,1,0) }  // -Z
        };

        foreach (var v in solid)
        {
            for (int d = 0; d < 6; d++)
            {
                var nb = v + N[d];
                if (solid.Contains(nb)) continue;

                Vector3 baseW = volMinW + new Vector3(v.x, v.y, v.z) * voxelSize;
                int s = verts.Count;
                verts.Add(baseW + F[d][0]*voxelSize);
                verts.Add(baseW + F[d][1]*voxelSize);
                verts.Add(baseW + F[d][2]*voxelSize);
                verts.Add(baseW + F[d][3]*voxelSize);
                tris.Add(s+0); tris.Add(s+1); tris.Add(s+2);
                tris.Add(s+0); tris.Add(s+2); tris.Add(s+3);
            }
        }

        var m = new Mesh();
        m.indexFormat = (verts.Count > 65000) ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        m.SetVertices(verts);
        m.SetTriangles(tris, 0, true);
        m.RecalculateNormals(); m.RecalculateBounds();
        mf.sharedMesh = m;
    }

    // helpers
    void VolumeWorld(out Vector3 volMinW, out Vector3 volCenterW)
    {
        var W_C = depthProvider.WorldFromCamera();
        volCenterW = W_C.MultiplyPoint3x4(Vector3.zero)
                   + W_C.MultiplyVector(Vector3.right)   * volumeCenterOffset.x
                   + W_C.MultiplyVector(Vector3.up)      * volumeCenterOffset.y
                   + W_C.MultiplyVector(Vector3.forward) * volumeCenterOffset.z;

        volMinW = volCenterW - new Vector3(volumeVoxels.x, volumeVoxels.y, volumeVoxels.z) * (voxelSize * 0.5f);
    }

    Vector3Int WorldToIndex(Vector3 Pw, Vector3 volMinW) =>
        new(Mathf.FloorToInt((Pw.x - volMinW.x) / voxelSize),
            Mathf.FloorToInt((Pw.y - volMinW.y) / voxelSize),
            Mathf.FloorToInt((Pw.z - volMinW.z) / voxelSize));

    Vector3 IndexToWorld(Vector3Int vi, Vector3 volMinW) =>
        volMinW + new Vector3(vi.x, vi.y, vi.z) * voxelSize;

    bool InBounds(Vector3Int vi) =>
        vi.x >= 0 && vi.y >= 0 && vi.z >= 0 &&
        vi.x < volumeVoxels.x && vi.y < volumeVoxels.y && vi.z < volumeVoxels.z;
}
