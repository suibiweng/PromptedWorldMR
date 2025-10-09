// Assets/DepthMesh/Scripts/DepthRaycastFallback.cs
using UnityEngine;

public static class DepthRaycastFallback
{
    public static bool Raycast(QuestDepthProvider_Oculus_Meta provider, Ray worldRay, float maxDistance, out Vector3 hitPosW)
    {
        hitPosW = default;
        if (provider == null || provider.depthMeters == null || provider.width == 0) return false;

        var C_W = provider.WorldFromCamera().inverse;
        int W = provider.width, H = provider.height;
        float fx = provider.fx, fy = provider.fy, cx = provider.cx, cy = provider.cy;
        var D = provider.depthMeters;

        const int steps = 64;
        float step = maxDistance / steps;

        for (int s = 1; s <= steps; s++)
        {
            float z = s * step;
            Vector3 Pw = worldRay.origin + worldRay.direction * z;
            Vector3 Pc = C_W.MultiplyPoint3x4(Pw);
            if (Pc.z <= 0.05f) continue;

            int u = Mathf.RoundToInt((Pc.x * fx / Pc.z) + cx);
            int v = Mathf.RoundToInt((-Pc.y * fy / Pc.z) + cy);
            if ((uint)u >= (uint)W || (uint)v >= (uint)H) continue;

            float dz = D[v * W + u];
            if (dz <= 0.2f || dz > maxDistance) continue;

            if (Mathf.Abs(dz - Pc.z) < 0.03f) // ~3cm tolerance
            {
                hitPosW = Pw;
                return true;
            }
        }
        return false;
    }
}
