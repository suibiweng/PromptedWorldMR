using System.Collections.Generic;
using System.Linq;
using Meta.XR.EnvironmentDepth;
using UnityEngine;
using System.Globalization;
using TMPro;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    [RequireComponent(typeof(ObjectDetectionAgent), typeof(DepthTextureAccess), typeof(EnvironmentDepthManager))]
    public class StableObjectTrackerFromAgent : MonoBehaviour
    {
        [Header("Prefab")]
        public GameObject boundingBoxPrefab;

        [Header("Tracking Mode")]
        public bool persistentTracking = true;
[Header("Tracking Distances (IDENTITY)")]
public float mergeDistance = 0.25f;
public float duplicateRejectDistance = 0.15f;
public float maxNewObjectDistance = 0.4f;

[Header("Depth Validation")]
public float minDepthDistance = 0.05f;
public float maxDepthDistance = 4.0f;
public bool enableRaycastValidation = false;   // 🔥 TURN THIS OFF FIRST
public float surfaceSnapTolerance = 0.5f;

[Header("Size Validation")]
public float minObjectSize = 0.005f;
public float maxObjectSize = 5.0f;


        [Header("Semantic Filtering")]
        public List<string> ignoreLabels = new List<string>()
        {
            "person","people","human","hand","face",
            "wall","floor","ceiling","door","window",
            "table","desk","bed","couch","sofa","storage","cabinet","shelf",
            "screen","tv","monitor",
            "lamp","plant","picture","painting","wall_art",
            "unknown","other","background"
        };

        [Header("Tracker Revalidation")]
        public int maxMissingFrames = 30;
        public float minSurfaceDistance = 0.5f;

        [Range(0.01f, 1f)] public float positionSmoothing = 0.5f;
        [Range(0.01f, 1f)] public float scaleSmoothing = 0.5f;

        [Header("Detection Filtering")]
        [Range(0f, 1f)] public float scoreThreshold = 0.4f;

        [Header("Label")]
        public float labelHeight = 0.05f;
        public float labelSize = 0.02f;

        // ===============================
        // DATA STRUCTURES
        // ===============================
        [System.Serializable]
        public class TrackedObjectData
        {
            public string label;
            public GameObject box;
            public GameObject labelObject;
            public Vector3 position;
            public Vector3 scale;
            public float lastSeenTime;
            public bool seenThisFrame;
            public int missingFrameCount;
        }

        public List<TrackedObjectData> TrackedObjects = new List<TrackedObjectData>();

        class TrackedItem
        {
            public TrackedObjectData data;
            public Vector3 targetPos;
            public Vector3 targetScale;
            public Quaternion targetRot;
        }

        private List<TrackedItem> _trackers = new List<TrackedItem>();

        private ObjectDetectionAgent _agent;
        private PassthroughCameraAccess _cam;
        private DepthTextureAccess _depth;
        private int _eyeIdx;

        // 🔥 NEW: Unified world manager
        private ObjectManager _objectManager;

        private struct FrameData
        {
            public Pose Pose;
            public PassthroughCameraAccess.CameraIntrinsics CameraIntrinsics;
            public float[] Depth;
            public Matrix4x4[] ViewProjectionMatrix;
        }

        private FrameData _frame;

        private void Awake()
        {
            _agent = GetComponent<ObjectDetectionAgent>();
            _cam = FindAnyObjectByType<PassthroughCameraAccess>();
            _depth = GetComponent<DepthTextureAccess>();
            _eyeIdx = _cam.CameraPosition == PassthroughCameraAccess.CameraPositionType.Left ? 0 : 1;

            // 🔥 Find ObjectManager
            _objectManager = FindAnyObjectByType<ObjectManager>();
        }

        private void OnEnable()
        {
            _agent.OnBoxesUpdated += HandleBatch;
            _depth.OnDepthTextureUpdateCPU += OnDepth;
        }

        private void OnDisable()
        {
            _agent.OnBoxesUpdated -= HandleBatch;
            _depth.OnDepthTextureUpdateCPU -= OnDepth;
        }

        private void OnDepth(DepthTextureAccess.DepthFrameData d)
        {
            _frame.Pose = _cam.GetCameraPose();
            _frame.CameraIntrinsics = _cam.Intrinsics;
            _frame.Depth = d.DepthTexturePixels.ToArray();
            _frame.ViewProjectionMatrix = d.ViewProjectionMatrix.ToArray();
        }

        // ===============================
        // LABEL PARSING
        // ===============================
        private bool ParseLabelAndScore(string raw, out string cleanLabel, out float score)
        {
            cleanLabel = raw;
            score = 1.0f;
            if (string.IsNullOrEmpty(raw)) return false;

            raw = raw.Trim();

            int idxParen = raw.LastIndexOf('(');
            if (idxParen >= 0 && raw.EndsWith(")"))
            {
                string name = raw.Substring(0, idxParen).Trim();
                string num = raw.Substring(idxParen + 1, raw.Length - idxParen - 2);
                if (float.TryParse(num, NumberStyles.Any, CultureInfo.InvariantCulture, out float s))
                {
                    cleanLabel = name;
                    score = s;
                    return true;
                }
            }

            int idxColon = raw.LastIndexOf(':');
            if (idxColon >= 0)
            {
                string name = raw.Substring(0, idxColon).Trim();
                string num = raw.Substring(idxColon + 1).Trim();
                if (float.TryParse(num, NumberStyles.Any, CultureInfo.InvariantCulture, out float s))
                {
                    cleanLabel = name;
                    score = s;
                    return true;
                }
            }

            int idxSpace = raw.LastIndexOf(' ');
            if (idxSpace > 0)
            {
                string name = raw.Substring(0, idxSpace).Trim();
                string num = raw.Substring(idxSpace + 1).Trim();
                if (float.TryParse(num, NumberStyles.Any, CultureInfo.InvariantCulture, out float s))
                {
                    cleanLabel = name;
                    score = s;
                    return true;
                }
            }

            cleanLabel = raw;
            score = 1.0f;
            return true;
        }

        // ===============================
        // IDENTITY LOGIC
        // ===============================
        private TrackedItem FindBestTracker(string label, Vector3 pos)
        {
            float bestDist = float.MaxValue;
            TrackedItem best = null;

            foreach (var t in _trackers)
            {
                if (t.data.label != label) continue;
                float d = Vector3.Distance(t.data.position, pos);
                if (d < mergeDistance && d < bestDist)
                {
                    bestDist = d;
                    best = t;
                }
            }
            return best;
        }

        private bool ExistsVeryClose(string label, Vector3 pos)
        {
            foreach (var t in _trackers)
            {
                if (t.data.label != label) continue;
                if (Vector3.Distance(t.data.position, pos) < duplicateRejectDistance)
                    return true;
            }
            return false;
        }

        private bool HasSameLabelNearby(string label, Vector3 pos, float radius)
        {
            foreach (var t in _trackers)
            {
                if (t.data.label != label) continue;
                if (Vector3.Distance(t.data.position, pos) < radius)
                    return true;
            }
            return false;
        }

        // ===============================
        // SURFACE VALIDATION
        // ===============================
        bool IsTrackerOnRealSurface(TrackedItem t)
        {
            Vector3 origin = _frame.Pose.position;
            Vector3 dir = (t.data.position - origin).normalized;

            if (Physics.Raycast(origin, dir, out RaycastHit hit, maxDepthDistance))
            {
                float d = Vector3.Distance(hit.point, t.data.position);
                return d < minSurfaceDistance;
            }

            return false;
        }

        // ===============================
        // MAIN HANDLER
        // ===============================
        private void HandleBatch(List<BoxData> batch)
        {
            float now = Time.time;

            foreach (var t in _trackers)
                t.data.seenThisFrame = false;

            foreach (var b in batch)
            {
                if (!ParseLabelAndScore(b.label, out string label, out float score)) continue;

                label = label.ToLower();
                if (ignoreLabels.Contains(label)) continue;
                if (score < scoreThreshold) continue;

                if (!TryProject(b.position.x, b.position.y, b.scale.x, b.scale.y, out var pos, out var rot, out var scl))
                    continue;

                var tracker = FindBestTracker(label, pos);

                if (tracker == null && ExistsVeryClose(label, pos))
                    continue;

                if (tracker == null)
                {
                    if (HasSameLabelNearby(label, pos, maxNewObjectDistance))
                        continue;

                    var box = Instantiate(boundingBoxPrefab);

                    var labelGO = new GameObject("Label_" + label);
                    labelGO.transform.SetParent(box.transform, false);

                    var tm = labelGO.AddComponent<TextMesh>();
                    tm.text = $"{label} ({score:0.00})";
                    tm.fontSize = 48;
                    tm.characterSize = labelSize;
                    tm.anchor = TextAnchor.MiddleCenter;
                    tm.alignment = TextAlignment.Center;
                    tm.color = Color.white;

                    labelGO.transform.localPosition = Vector3.up * labelHeight;

                    var data = new TrackedObjectData()
                    {
                        label = label,
                        box = box,
                        labelObject = labelGO,
                        position = pos,
                        scale = scl,
                        lastSeenTime = now,
                        seenThisFrame = true,
                        missingFrameCount = 0
                    };

                    TrackedObjects.Add(data);

                    tracker = new TrackedItem()
                    {
                        data = data,
                        targetPos = pos,
                        targetRot = rot,
                        targetScale = scl
                    };

                    box.transform.SetPositionAndRotation(pos, rot);
                    box.transform.localScale = scl;

                    _trackers.Add(tracker);

                    // 🔥 SEND TO OBJECT MANAGER
                    if (_objectManager != null)
                        _objectManager.RegisterOrUpdate(label, box.transform);
                }
                else
                {
                    tracker.data.lastSeenTime = now;
                    tracker.data.seenThisFrame = true;
                    tracker.data.missingFrameCount = 0;

                    tracker.targetPos = pos;
                    tracker.targetRot = rot;
                    tracker.targetScale = scl;

                    // 🔥 UPDATE OBJECT MANAGER
                    if (_objectManager != null)
                        _objectManager.RegisterOrUpdate(label, tracker.data.box.transform);
                }
            }

            // ===============================
            // CLEANUP PASS
            // ===============================
            for (int i = _trackers.Count - 1; i >= 0; i--)
            {
                var t = _trackers[i];

                if (!t.data.seenThisFrame)
                    t.data.missingFrameCount++;

                bool shouldRemove = false;

                if (t.data.missingFrameCount > maxMissingFrames)
                    shouldRemove = true;

                if (enableRaycastValidation && !IsTrackerOnRealSurface(t))
                    shouldRemove = true;

                if (shouldRemove)
                {
                    if (_objectManager != null)
                        _objectManager.Remove(t.data.box.transform);

                    Destroy(t.data.box);
                    TrackedObjects.Remove(t.data);
                    _trackers.RemoveAt(i);
                }
            }
        }

        private void Update()
        {
            Camera cam = Camera.main;

            foreach (var t in _trackers)
            {
                var d = t.data;
                if (d.box == null) continue;

                var tr = d.box.transform;

                tr.position = Vector3.Lerp(tr.position, t.targetPos, positionSmoothing);
                tr.rotation = Quaternion.Slerp(tr.rotation, t.targetRot, positionSmoothing);
                tr.localScale = Vector3.Lerp(tr.localScale, t.targetScale, scaleSmoothing);

                d.position = tr.position;
                d.scale = tr.localScale;

                if (d.labelObject != null && cam != null)
                {
                    Vector3 dir = d.labelObject.transform.position - cam.transform.position;
                    if (dir.sqrMagnitude > 0.0001f)
                        d.labelObject.transform.rotation = Quaternion.LookRotation(dir.normalized);
                }
            }
        }

        // ===============================
        // PROJECTION
        // ===============================
        public bool TryProject(float xmin, float ymin, float xmax, float ymax,
            out Vector3 world, out Quaternion rot, out Vector3 scale)
        {
            world = default;
            rot = default;
            scale = default;

            var px = (xmin + xmax) * 0.5f;
            var py = (ymin + ymax) * 0.5f;

            var dirCam = new Vector3(
                (px - _frame.CameraIntrinsics.PrincipalPoint.x) / _frame.CameraIntrinsics.FocalLength.x,
                -(py - _frame.CameraIntrinsics.PrincipalPoint.y) / _frame.CameraIntrinsics.FocalLength.y,
                1f).normalized;

            var world1M = _frame.Pose.position + _frame.Pose.rotation * dirCam;
            var clip = _frame.ViewProjectionMatrix[_eyeIdx] * new Vector4(world1M.x, world1M.y, world1M.z, 1f);
            if (clip.w <= 0) return false;

            var uv = (new Vector2(clip.x, clip.y) / clip.w) * 0.5f + Vector2.one * 0.5f;
            const int texSize = DepthTextureAccess.TextureSize;
            var sx = Mathf.Clamp((int)(uv.x * texSize), 0, texSize - 1);
            var sy = Mathf.Clamp((int)(uv.y * texSize), 0, texSize - 1);
            var idx = _eyeIdx * texSize * texSize + sy * texSize + sx;
            var d = _frame.Depth[idx];

            if (d <= minDepthDistance || d > maxDepthDistance || float.IsInfinity(d))
                return false;

            world = _frame.Pose.position + _frame.Pose.rotation * (dirCam * d);
            rot = Quaternion.LookRotation(world - _frame.Pose.position);

            var w = (xmax - xmin) / _frame.CameraIntrinsics.FocalLength.x * d;
            var h = (ymax - ymin) / _frame.CameraIntrinsics.FocalLength.y * d;
            scale = new Vector3(w, h, 1f);

            if (scale.x < minObjectSize || scale.y < minObjectSize || scale.x > maxObjectSize || scale.y > maxObjectSize)
                return false;

            return true;
        }
    }
}
