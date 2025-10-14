using System.Collections.Generic;
using UnityEngine;
using Meta.XR; // EnvironmentRaycastManager, EnvironmentRaycastHit

/// OVRInput-only segmenter: hold index trigger to grow a patch and draw a mesh overlay.
[DefaultExecutionOrder(50)]
public class SurfaceSegmenterOVR : MonoBehaviour
{
    [Header("Assign")]
    public Transform rayOrigin;                 // e.g., RightHandAnchor/Pointer
    public Material highlightMaterial;          // transparent highlight shader
    public float maxRayDistance = 5f;

    [Header("OVRInput")]
    public bool useRightHand = true;            // true: Right trigger, false: Left trigger
    [Range(0f, 1f)] public float triggerThreshold = 0.5f;

    [Header("Segmentation Tuning")]
    [Range(4, 64)] public int raysPerRing = 32;
    [Range(0, 6)] public int ringCount = 2;
    [Tooltip("Random angular jitter in degrees per sample.")]
    public float ringAngularJitter = 0.0f;
    [Tooltip("Start radius of the sampling ring on the tangent plane (meters).")]
    public float ringRadiusStart = 0.01f;
    [Tooltip("Step between rings (meters).")]
    public float ringRadiusStep  = 0.02f;
    [Tooltip("Accept samples whose depth differs from seed by at most this (meters).")]
    public float maxSurfaceStep  = 0.03f;
    [Tooltip("Max angle (deg) between sample normal and seed normal.")]
    public float maxNormalAngleDeg = 22.5f;
    [Tooltip("Ignore hits with normalConfidence below this.")]
    [Range(0f,1f)] public float hitConfidenceMin = 0.2f;

    [Header("Overlay")]
    public float inflateBorder = 0.002f;
    public bool drawWhileHeld = true;

    private EnvironmentRaycastManager _rayMgr;
    private SegmentedOverlay _overlay;
    private readonly List<Vector3> _hitsWorld = new();
    private readonly List<Vector3> _hitsOnPlane = new();

    void Awake()
    {
        _rayMgr = FindAnyObjectByType<EnvironmentRaycastManager>(FindObjectsInactive.Include);
        if (_rayMgr == null)
        {
            Debug.LogError("SurfaceSegmenterOVR: No EnvironmentRaycastManager in scene.");
            enabled = false;
            return;
        }
        _overlay = new GameObject("SegmentedOverlay").AddComponent<SegmentedOverlay>();
        _overlay.Initialize(highlightMaterial);

        if (rayOrigin == null)
            Debug.LogWarning("SurfaceSegmenterOVR: rayOrigin is not assigned.");
    }

    void Update()
    {
        bool pressed = ReadOVRTriggerPressed();

        if (!pressed && !drawWhileHeld) return;
        if (!pressed && drawWhileHeld) { _overlay.SetActive(false); return; }

        if (!TrySeed(out EnvironmentRaycastHit seed)) { _overlay.SetActive(false); return; }

        // Build tangent frame at seed
        Vector3 n = seed.normal.normalized;
        Vector3 x = Vector3.Normalize(Vector3.Cross(Vector3.up, n));
        if (x.sqrMagnitude < 1e-6f) x = Vector3.Normalize(Vector3.Cross(Vector3.right, n));
        Vector3 y = Vector3.Cross(n, x);

        _hitsWorld.Clear();
        _hitsOnPlane.Clear();
        _hitsWorld.Add(seed.point);
        _hitsOnPlane.Add(Vector3.zero);

        float seedDepth = (seed.point - rayOrigin.position).magnitude;
        float cosThresh = Mathf.Cos(maxNormalAngleDeg * Mathf.Deg2Rad);

        int raysPerRingClamped = Mathf.Max(6, raysPerRing);
        for (int ring = 0; ring < Mathf.Max(1, ringCount); ring++)
        {
            float radius = ringRadiusStart + ring * ringRadiusStep;
            int samples = raysPerRingClamped;

            for (int i = 0; i < samples; i++)
            {
                float ang = (i / (float)samples) * Mathf.PI * 2f;
                if (ringAngularJitter > 0f)
                    ang += Random.Range(-ringAngularJitter, ringAngularJitter) * Mathf.Deg2Rad;

                Vector3 offset = x * Mathf.Cos(ang) * radius + y * Mathf.Sin(ang) * radius;

                // Cast from slightly in front of the plane, into the plane
                Vector3 origin = seed.point - n * 0.01f + offset;
                Vector3 dir = n;

                var ray = new Ray(origin, dir);
                if (_rayMgr.Raycast(ray, out var hit, maxRayDistance))
                {
                    if (hit.normalConfidence < hitConfidenceMin) continue;

                    float depthDelta = Mathf.Abs((hit.point - rayOrigin.position).magnitude - seedDepth);
                    if (depthDelta > maxSurfaceStep) continue;

                    float cosang = Vector3.Dot(hit.normal.normalized, n);
                    if (cosang < cosThresh) continue;

                    _hitsWorld.Add(hit.point);

                    Vector3 v = hit.point - seed.point;
                    float px = Vector3.Dot(v, x);
                    float py = Vector3.Dot(v, y);
                    _hitsOnPlane.Add(new Vector3(px, py, 0));
                }
            }
        }

        if (_hitsWorld.Count >= 3)
        {
            _overlay.SetActive(true);
            _overlay.UpdateMesh(seed.point, n, x, y, _hitsOnPlane, _hitsWorld, inflateBorder);
        }
        else
        {
            _overlay.SetActive(false);
        }
    }

    bool TrySeed(out EnvironmentRaycastHit seed)
    {
        if (rayOrigin == null) { seed = default; return false; }
        var ray = new Ray(rayOrigin.position, rayOrigin.forward);
        if (_rayMgr.Raycast(ray, out var hit, maxRayDistance))
        {
            seed = hit;
            return true;
        }
        seed = default;
        return false;
    }

    bool ReadOVRTriggerPressed()
    {
        // Right or Left index trigger analog value
        float value = useRightHand
            ? OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger)
            : OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger); // Left

        return value > triggerThreshold;
    }
}
