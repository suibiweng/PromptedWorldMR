using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Meta.XR; // EnvironmentRaycastManager, EnvironmentRaycastHit

/// Drop this on a GameObject and wire references like the MRUK sample.
/// Hold the index trigger to "click-to-segment" and render a live mesh overlay.
namespace Meta.XR.MRUtilityKitSamples.ObjectSegmentation
{
    public class SurfaceSegmenterOVR_RaycastStyle : MonoBehaviour
    {
        [Header("Refs (match the sample pattern)")]
        [SerializeField] private EnvironmentRaycastManager _raycastManager;
        [SerializeField] private Transform _centerEyeAnchor;          // HMD
        [SerializeField] private Transform _raycastAnchor;            // controller/aim pointer (e.g., RightHandAnchor/Pointer)
        [SerializeField] private LineRenderer _raycastVisualizationLine;
        [SerializeField] private Transform _raycastVisualizationNormal;

        [Header("OVR Input")]
        [SerializeField] private OVRInput.RawButton _segmentButton = OVRInput.RawButton.RIndexTrigger;
        [SerializeField] private OVRInput.RawButton _lockButton    = OVRInput.RawButton.A;   // optional: locks current patch
        [SerializeField] private OVRInput.RawButton _clearButton   = OVRInput.RawButton.B;   // optional: clears all patches

        [Header("Overlay")]
        [SerializeField] private Material _highlightMaterial;        // transparent material
        [SerializeField] private float _maxRayDistance = 5f;
        [SerializeField, Tooltip("Attach a MeshCollider to overlays for re-hitting them with Physics ray.")]
        private bool _addColliderToOverlay = false;

        [Header("Segmentation Tunables")]
        [Range(4,64)]  public int raysPerRing = 32;
        [Range(0,6)]   public int ringCount = 2;
        public float ringAngularJitter = 0.0f;      // degrees
        public float ringRadiusStart  = 0.01f;      // meters
        public float ringRadiusStep   = 0.02f;      // meters
        public float maxSurfaceStep   = 0.03f;      // meters depth delta vs seed
        public float maxNormalAngleDeg = 22.5f;     // degrees
        [Range(0f,1f)] public float hitConfidenceMin = 0.2f;
        public float inflateBorder = 0.002f;

        [Header("Session")]
        public bool drawWhileHeld = true;           // live update while holding trigger
        public bool keepLockedPatches = true;       // allow storing multiple locked selections

        // internal state
        private SegmentedOverlay _liveOverlay;
        private bool _isSegmenting;
        private RollingAverage _normalFilter = new RollingAverage();
        private readonly List<Vector3> _hitsWorld = new();
        private readonly List<Vector3> _hitsOnPlane = new();
        private readonly List<SegmentedOverlay> _lockedPatches = new();

        private void Reset()
        {
            if (_raycastVisualizationLine)
            {
                _raycastVisualizationLine.positionCount = 2;
                _raycastVisualizationLine.enabled = false;
            }
        }

        private IEnumerator Start()
        {
            // Match the sample: wait until headset tracking is live before enabling update loop
            enabled = false;
            while (!OVRPlugin.userPresent || !OVRManager.isHmdPresent) { yield return null; }
            yield return null;
            enabled = true;

            // Create a live overlay container
            var go = new GameObject("SegmentedOverlay_Live");
            _liveOverlay = go.AddComponent<SegmentedOverlay>();
            _liveOverlay.Initialize(_highlightMaterial);
            if (_addColliderToOverlay) go.AddComponent<MeshCollider>();
            _liveOverlay.SetActive(false);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                _isSegmenting = false;
                _liveOverlay.SetActive(false);
            }
        }

        private void Update()
        {
            if (!Application.isFocused) return;

            VisualizeRaycast();

            // Clear all
            if (OVRInput.GetDown(_clearButton)) ClearLocked();

            // Lock current
            if (OVRInput.GetDown(_lockButton)) LockCurrent();

            // Segment flow mirrors the sample's grab logic
            if (_isSegmenting)
            {
                if (OVRInput.GetUp(_segmentButton))
                {
                    _isSegmenting = false;
                    if (!drawWhileHeld) _liveOverlay.SetActive(false);
                }
                else
                {
                    UpdateSegmentationFrame();
                }
            }
            else
            {
                if (OVRInput.GetDown(_segmentButton))
                {
                    _isSegmenting = true;
                    _normalFilter.Reset();
                    _liveOverlay.SetActive(true);
                    UpdateSegmentationFrame(); // immediate feedback
                }
            }
        }

        private Ray GetRaycastRay()
        {
            // Same trick as sample: start a bit forward to avoid self-hit
            var origin = _raycastAnchor.position + _raycastAnchor.forward * 0.1f;
            return new Ray(origin, _raycastAnchor.forward);
        }

        private void UpdateSegmentationFrame()
        {
            if (!TryGetSeed(out var seed))
            {
                if (drawWhileHeld) _liveOverlay.SetActive(false);
                return;
            }

            // Smooth normal like the sample’s rolling average
            var seedN = _normalFilter.UpdateRollingAverage(seed.normal).normalized;

            // Build tangent basis (x,y,n) on the seed plane
            Vector3 n = seedN;
            Vector3 x = Vector3.Normalize(Vector3.Cross(Vector3.up, n));
            if (x.sqrMagnitude < 1e-6f) x = Vector3.Normalize(Vector3.Cross(Vector3.right, n));
            Vector3 y = Vector3.Cross(n, x);

            // Region grow by short rays along n from a ring lattice in plane
            _hitsWorld.Clear();
            _hitsOnPlane.Clear();
            _hitsWorld.Add(seed.point);
            _hitsOnPlane.Add(Vector3.zero);

            float seedDepth = (seed.point - _raycastAnchor.position).magnitude;
            float cosThresh = Mathf.Cos(maxNormalAngleDeg * Mathf.Deg2Rad);

            int samples = Mathf.Max(6, raysPerRing);
            int rings = Mathf.Max(1, ringCount);
            for (int ring = 0; ring < rings; ring++)
            {
                float radius = ringRadiusStart + ring * ringRadiusStep;
                for (int i = 0; i < samples; i++)
                {
                    float ang = (i / (float)samples) * Mathf.PI * 2f;
                    if (ringAngularJitter > 0f) ang += Random.Range(-ringAngularJitter, ringAngularJitter) * Mathf.Deg2Rad;

                    Vector3 offset = x * Mathf.Cos(ang) * radius + y * Mathf.Sin(ang) * radius;
                    Vector3 origin = seed.point - n * 0.01f + offset; // nudge toward viewer
                    Vector3 dir = n;

                    var ray = new Ray(origin, dir);
                    if (_raycastManager.Raycast(ray, out var hit, _maxRayDistance))
                    {
                        if (hit.normalConfidence < hitConfidenceMin) continue;

                        float depthDelta = Mathf.Abs((hit.point - _raycastAnchor.position).magnitude - seedDepth);
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
                _liveOverlay.SetActive(true);
                _liveOverlay.UpdateMesh(seed.point, n, x, y, _hitsOnPlane, _hitsWorld, inflateBorder);
                // keep MeshCollider in sync (optional)
                if (_addColliderToOverlay)
                {
                    var mc = _liveOverlay.GetComponent<MeshCollider>();
                    var mf = _liveOverlay.GetComponent<MeshFilter>();
                    if (mc && mf && mf.sharedMesh) mc.sharedMesh = mf.sharedMesh;
                }
            }
            else
            {
                if (drawWhileHeld) _liveOverlay.SetActive(false);
            }
        }

        private bool TryGetSeed(out EnvironmentRaycastHit seed)
        {
            var ray = GetRaycastRay();

            // Like the sample: allow hitting an existing overlay (if you enabled collider)
            if (Physics.Raycast(ray, out var ph) && ph.transform == (_liveOverlay ? _liveOverlay.transform : null))
            {
                seed = new EnvironmentRaycastHit
                {
                    status = EnvironmentRaycastHitStatus.Hit,
                    point = ph.point,
                    normal = ph.normal,
                    normalConfidence = 1f
                };
                return true;
            }

            if (!_raycastManager.Raycast(ray, out seed, _maxRayDistance)) return false;
            if (seed.normalConfidence < hitConfidenceMin) return false;

            // Ignore ceilings (match sample behavior)
            bool isCeiling = Vector3.Dot(seed.normal, Vector3.down) > 0.7f;
            if (isCeiling) return false;

            return true;
        }

        private void VisualizeRaycast()
        {
            var ray = GetRaycastRay();
            var hasHit = RaycastOverlayOrEnvironment(ray, out var envHit) ||
                         envHit.status == EnvironmentRaycastHitStatus.HitPointOccluded;
            var hasNormal = envHit.normalConfidence > 0f;

            if (_raycastVisualizationLine)
            {
                _raycastVisualizationLine.enabled = hasHit;
                if (hasHit)
                {
                    _raycastVisualizationLine.SetPosition(0, ray.origin);
                    _raycastVisualizationLine.SetPosition(1, envHit.point);
                }
            }

            if (_raycastVisualizationNormal)
            {
                _raycastVisualizationNormal.gameObject.SetActive(hasHit && hasNormal);
                if (hasHit && hasNormal)
                {
                    _raycastVisualizationNormal.SetPositionAndRotation(
                        envHit.point, Quaternion.LookRotation(envHit.normal));
                }
            }
        }

        private bool RaycastOverlayOrEnvironment(Ray ray, out EnvironmentRaycastHit envHit)
        {
            if (Physics.Raycast(ray, out var physicsHit) && _liveOverlay && physicsHit.transform == _liveOverlay.transform)
            {
                envHit = new EnvironmentRaycastHit
                {
                    status = EnvironmentRaycastHitStatus.Hit,
                    point = physicsHit.point,
                    normal = physicsHit.normal,
                    normalConfidence = 1f
                };
                return true;
            }

            bool ok = _raycastManager.Raycast(ray, out envHit, _maxRayDistance);
            return ok;
        }

        private void LockCurrent()
        {
            if (!_liveOverlay || !_liveOverlay.enabled) return;
            if (!keepLockedPatches) return;

            // Duplicate current mesh into a new persistent overlay
            var srcMF = _liveOverlay.GetComponent<MeshFilter>();
            if (!srcMF || !srcMF.sharedMesh) return;

            var go = new GameObject("SegmentedOverlay_Locked");
            var overlay = go.AddComponent<SegmentedOverlay>();
            overlay.Initialize(_highlightMaterial);
            var mf = overlay.GetComponent<MeshFilter>();
            mf.sharedMesh = Instantiate(srcMF.sharedMesh);
            overlay.SetActive(true);
            if (_addColliderToOverlay) go.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;

            _lockedPatches.Add(overlay);
        }

        private void ClearLocked()
        {
            foreach (var ov in _lockedPatches)
                if (ov) Destroy(ov.gameObject);
            _lockedPatches.Clear();
        }

        // === Rolling average like the sample ===
        private class RollingAverage
        {
            private List<Vector3> _normals;
            private int _idx;

            public void Reset()
            {
                _normals = null;
                _idx = 0;
            }

            public Vector3 UpdateRollingAverage(Vector3 current)
            {
                if (_normals == null)
                {
                    const int size = 10;
                    _normals = Enumerable.Repeat(current, size).ToList();
                }
                _idx++;
                _normals[_idx % _normals.Count] = current;
                Vector3 sum = default;
                foreach (var n in _normals) sum += n;
                return sum.normalized;
            }
        }
    }
}
