using System;
using UnityEngine;

[DisallowMultipleComponent]
public class QRPoseFollower : MonoBehaviour
{
    [Header("Target (set at runtime)")]
    public Transform target;                    // assign: trackable.transform
    public bool followPosition = true;
    public bool followRotation = true;

    [Header("Smoothing (One-Euro)")]
    [Tooltip("Lower = more smoothing. 1.0–2.0 is a good start.")]
    public float minCutoff = 1.5f;
    [Tooltip("Increase to reduce lag during motion. 0.0–0.05 typical.")]
    public float beta = 0.02f;
    [Tooltip("Derivative cutoff (Hz). 1.0 is fine.")]
    public float dCutoff = 1.0f;

    [Tooltip("Extra low-pass on rotation (0–1). 0.15 = gentle smoothing.")]
    [Range(0f, 1f)] public float rotationLerp = 0.15f;

    [Header("Loss Hysteresis")]
    [Tooltip("How many consecutive frames missing before we consider lost.")]
    public int loseAfterFrames = 10;
    [Tooltip("How many consecutive frames seen before we consider found.")]
    public int foundAfterFrames = 3;

    [Header("Stick to Spatial Anchor (optional)")]
    [Tooltip("Turn ON to bake a spatial anchor after pose is stable.")]
    public bool stickWhenStable = false;       // default OFF so it won’t freeze
    [Tooltip("If true, continue following even after anchor bake.")]
    public bool keepFollowingAfterStick = true; // default ON so it never freezes
    [Tooltip("How long (seconds) jitter must stay under thresholds to lock.")]
    public float stickStableSeconds = 0.75f;
    [Tooltip("Max linear (m) + angular (deg) jitter to consider 'stable'.")]
    public float stickMaxLinearJitter = 0.0025f; // 2.5 mm
    public float stickMaxAngularJitter = 0.9f;   // degrees

    // --- internal ---
    OneEuroVector3 _posFilter;
    OneEuroVector3 _velFilter;
    Vector3 _lastRawPos;
    Quaternion _lastRawRot;
    bool _hadFirstPose;

    int _seenFrames, _lostFrames;
    float _stableTimer;
    bool _stuck;

#if META_PLATFORM_SDK || OCULUS_SDK
    OVRSpatialAnchor _anchor; // created when we "stick"
#endif

    void Awake()
    {
        _posFilter = new OneEuroVector3(minCutoff, beta, dCutoff);
        _velFilter = new OneEuroVector3(minCutoff, beta, dCutoff);
    }

    void Update()
    {
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);

        bool hasPose = target && target.gameObject.activeInHierarchy;
        if (hasPose)
        {
            _lostFrames = 0;
            _seenFrames = Mathf.Min(_seenFrames + 1, int.MaxValue);
        }
        else
        {
            _seenFrames = 0;
            _lostFrames++;
        }

        bool consideredSeen = _seenFrames >= foundAfterFrames && _lostFrames < loseAfterFrames;
        if (!consideredSeen) return;
        if (_stuck && !keepFollowingAfterStick) return;
        if (!target) return;

        if (!_hadFirstPose)
        {
            _lastRawPos = target.position;
            _lastRawRot = target.rotation;
            transform.SetPositionAndRotation(_lastRawPos, _lastRawRot);
            _hadFirstPose = true;
        }

        // Raw deltas
        Vector3 rawPos = target.position;
        Quaternion rawRot = target.rotation;
        Vector3 vel = (rawPos - _lastRawPos) / dt;

        // One-Euro for position
        Vector3 smoothedPos = _posFilter.Filter(rawPos, vel, dt);

        // Slerp for rotation (simple + robust)
        Quaternion smoothedRot = Quaternion.Slerp(transform.rotation, rawRot, rotationLerp);

        // Apply
        if (followPosition) transform.position = smoothedPos;
        if (followRotation) transform.rotation = smoothedRot;

        // Stability check for "stick"
        if (stickWhenStable && !_stuck)
        {
            float linJitter = Vector3.Distance(smoothedPos, rawPos);
            float angJitter = Quaternion.Angle(smoothedRot, rawRot);

            if (linJitter <= stickMaxLinearJitter && angJitter <= stickMaxAngularJitter)
                _stableTimer += dt;
            else
                _stableTimer = 0f;

            if (_stableTimer >= stickStableSeconds)
                TryStickAnchor(smoothedPos, smoothedRot);
        }

        _lastRawPos = rawPos;
        _lastRawRot = rawRot;
    }

    void TryStickAnchor(Vector3 posePos, Quaternion poseRot)
    {
#if META_PLATFORM_SDK || OCULUS_SDK
        GameObject anchorGO = new GameObject("QR_StuckAnchor");
        anchorGO.transform.SetPositionAndRotation(posePos, poseRot);
        _anchor = anchorGO.AddComponent<OVRSpatialAnchor>();

        // Reparent this follower under the anchor so its world pose remains the same.
        transform.SetParent(anchorGO.transform, true);

        // Persist the anchor on-device so it survives tracking loss.
        _anchor.Save((OVRSpatialAnchor anchor, bool success) =>
        {
            if (!success)
            {
                Debug.LogWarning("[QRPoseFollower] Anchor Save failed; continuing to follow.");
                return; // don't freeze
            }

            Debug.Log("[QRPoseFollower] Anchor saved.");
            if (!keepFollowingAfterStick)
            {
                _stuck = true; // only freeze if explicitly desired
            }
        });
#else
        // If OVRSpatialAnchor not available, freeze by detaching follow (optional).
        var frozen = new GameObject("QR_FrozenPose");
        frozen.transform.SetPositionAndRotation(posePos, poseRot);
        transform.SetParent(frozen.transform, true);
        if (!keepFollowingAfterStick) _stuck = true;
        Debug.Log("[QRPoseFollower] Stuck (no SpatialAnchor available).");
#endif
    }

    void OnValidate()
    {
        if (_posFilter != null)
        {
            _posFilter.minCutoff = minCutoff;
            _posFilter.beta = beta;
            _posFilter.dCutoff = dCutoff;
        }
        if (_velFilter != null)
        {
            _velFilter.minCutoff = minCutoff;
            _velFilter.beta = beta;
            _velFilter.dCutoff = dCutoff;
        }
    }

    // --------- One-Euro filter helpers ----------
    [Serializable]
    public class OneEuroVector3
    {
        public float minCutoff;
        public float beta;
        public float dCutoff;

        LowPassFilter _x, _y, _z;
        LowPassFilter _dx, _dy, _dz;

        public OneEuroVector3(float minCutoff, float beta, float dCutoff)
        {
            this.minCutoff = minCutoff;
            this.beta = beta;
            this.dCutoff = dCutoff;
            _x = new LowPassFilter();
            _y = new LowPassFilter();
            _z = new LowPassFilter();
            _dx = new LowPassFilter();
            _dy = new LowPassFilter();
            _dz = new LowPassFilter();
        }

        static float Alpha(float cutoff, float dt)
        {
            float tau = 1f / (2f * Mathf.PI * Mathf.Max(cutoff, 1e-5f));
            return 1f / (1f + tau / Mathf.Max(dt, 1e-4f));
        }

        public Vector3 Filter(Vector3 value, Vector3 deriv, float dt)
        {
            // Filter derivative first
            float ad = Alpha(dCutoff, dt);
            float dx = _dx.Filter(deriv.x, ad);
            float dy = _dy.Filter(deriv.y, ad);
            float dz = _dz.Filter(deriv.z, ad);

            // Dynamic cutoff
            float cutoffX = minCutoff + beta * Mathf.Abs(dx);
            float cutoffY = minCutoff + beta * Mathf.Abs(dy);
            float cutoffZ = minCutoff + beta * Mathf.Abs(dz);

            float ax = Alpha(cutoffX, dt);
            float ay = Alpha(cutoffY, dt);
            float az = Alpha(cutoffZ, dt);

            return new Vector3(
                _x.Filter(value.x, ax),
                _y.Filter(value.y, ay),
                _z.Filter(value.z, az)
            );
        }

        class LowPassFilter
        {
            bool _init;
            float _prev;

            public float Filter(float x, float a)
            {
                if (!_init) { _init = true; _prev = x; return x; }
                _prev = a * x + (1 - a) * _prev;
                return _prev;
            }
        }
    }
}
