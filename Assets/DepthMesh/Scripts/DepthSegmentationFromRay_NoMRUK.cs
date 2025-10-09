// Assets/DepthMesh/Scripts/DepthSegmentationFromRay_NoMRUK.cs
using UnityEngine;
using static OVRInput;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class DepthSegmentationFromRay_NoMRUK : MonoBehaviour
{
    [Header("Refs")]
    public Transform rayOrigin;                         // controller/hand
    public QuestDepthProvider_Oculus_Meta depthProvider;
    public LiveVoxelMesherROI mesher;

    [Header("Region-grow")]
    public float maxDeltaZ = 0.02f;     // meters
    public float maxNormalAngle = 35f;  // degrees
    public int   maxPixelRadius = 60;   // in downsampled pixels

    [Header("Input")]
    public Controller controller = Controller.RTouch;
    public Button trigger = Button.PrimaryIndexTrigger;

    bool[] visited;
    Queue<int> q = new();

    void Start()
    {
        if (!depthProvider) depthProvider = FindObjectOfType<QuestDepthProvider_Oculus_Meta>(true);
        if (!mesher) mesher = FindObjectOfType<LiveVoxelMesherROI>(true);

            if (!rayOrigin) {
        var rp = FindObjectOfType<RaycastPointer>(true);
        if (rp) rayOrigin = rp.RayOrigin;
    }
    }

    void Update()
    {
        if (!rayOrigin || !depthProvider || !mesher) return;
        if (depthProvider.depthMeters == null || depthProvider.width == 0) return;

        if (GetDown(trigger, controller))
        {
            var ray = new Ray(rayOrigin.position, rayOrigin.forward);
            if (!DepthRaycastFallback.Raycast(depthProvider, ray, mesher.maxDepthMeters, out var hitPosW))
                return;

            // world -> camera -> pixel
            var W_C = depthProvider.WorldFromCamera();
            var C_W = W_C.inverse;
            Vector3 Pc = C_W.MultiplyPoint3x4(hitPosW);
            if (Pc.z <= 0.1f) return;

            int W = depthProvider.width, H = depthProvider.height;
            float fx = depthProvider.fx, fy = depthProvider.fy, cx = depthProvider.cx, cy = depthProvider.cy;
            int u0 = Mathf.RoundToInt((Pc.x * fx / Pc.z) + cx);
            int v0 = Mathf.RoundToInt((-Pc.y * fy / Pc.z) + cy);
            if ((uint)u0 >= (uint)W || (uint)v0 >= (uint)H) return;

            var mask = RegionGrowDepth(u0, v0, maxDeltaZ, maxNormalAngle);
            PaintMaskToROI(mask);
        }
    }

    bool[] RegionGrowDepth(int u0, int v0, float dz, float maxAngleDeg)
    {
        int W = depthProvider.width, H = depthProvider.height;
        var D = depthProvider.depthMeters;
        if (visited == null || visited.Length != W * H) visited = new bool[W * H];

        System.Array.Clear(visited, 0, visited.Length);
        q.Clear();

        int seed = v0 * W + u0;
        float seedZ = D[seed];
        if (seedZ <= 0.2f || seedZ > mesher.maxDepthMeters) return visited;

        visited[seed] = true; q.Enqueue(seed);

        float cosT = Mathf.Cos(maxAngleDeg * Mathf.Deg2Rad);
        int[] du = { 1,-1, 0, 0, 1, 1,-1,-1 };
        int[] dv = { 0, 0, 1,-1, 1,-1, 1,-1 };

        while (q.Count > 0)
        {
            int idx = q.Dequeue();
            int u = idx % W, v = idx / W;
            float zc = D[idx];
            if (zc <= 0.2f || zc > mesher.maxDepthMeters) continue;

            Vector3 nc = EstimateNormal(W, H, D, u, v);

            for (int k = 0; k < du.Length; k++)
            {
                int uu = u + du[k], vv = v + dv[k];
                if ((uint)uu >= (uint)W || (uint)vv >= (uint)H) continue;
                int j = vv * W + uu;
                if (visited[j]) continue;

                float zn = D[j];
                if (zn <= 0.2f || zn > mesher.maxDepthMeters) continue;
                if (Mathf.Abs(zn - zc) > dz) continue;

                Vector3 nn = EstimateNormal(W, H, D, uu, vv);
                if (Vector3.Dot(nc, nn) < cosT) continue;

                if ((uu - u0)*(uu - u0) + (vv - v0)*(vv - v0) > maxPixelRadius*maxPixelRadius) continue;

                visited[j] = true;
                q.Enqueue(j);
            }
        }
        return visited;
    }

    Vector3 EstimateNormal(int W, int H, float[] D, int u, int v)
    {
        int u1 = Mathf.Clamp(u - 1, 0, W - 1), u2 = Mathf.Clamp(u + 1, 0, W - 1);
        int v1 = Mathf.Clamp(v - 1, 0, H - 1), v2 = Mathf.Clamp(v + 1, 0, H - 1);

        float zx = D[v * W + u2] - D[v * W + u1];
        float zy = D[v2 * W + u] - D[v1 * W + u];

        Vector3 tx = new(1, 0, zx);
        Vector3 ty = new(0,-1, zy);
        var n = Vector3.Cross(tx, ty).normalized;
        if (n.z < 0) n = -n;
        return n;
    }

    void PaintMaskToROI(bool[] mask)
    {
        int W = depthProvider.width, H = depthProvider.height;
        var D = depthProvider.depthMeters;
        float fx = depthProvider.fx, fy = depthProvider.fy, cx = depthProvider.cx, cy = depthProvider.cy;
        var W_C = depthProvider.WorldFromCamera();

        float brush = mesher.voxelSize * 1.5f;

        for (int v = 0; v < H; v++)
        for (int u = 0; u < W; u++)
        {
            int i = v * W + u;
            if (!mask[i]) continue;

            float z = D[i];
            if (z <= 0.2f || z > mesher.maxDepthMeters) continue;

            float x = (u - cx) * z / fx;
            float y = (v - cy) * z / fy;
            Vector3 Pc = new(x, -y, z);
            Vector3 Pw = W_C.MultiplyPoint3x4(Pc);

            mesher.PaintSphere(Pw, brush, add:true);
        }
    }
}
