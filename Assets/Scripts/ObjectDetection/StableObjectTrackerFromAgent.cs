using System.Collections.Generic;
using System.Linq;
using Meta.XR.EnvironmentDepth;
using UnityEngine;
using System.Globalization;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    [RequireComponent(typeof(ObjectDetectionAgent), typeof(DepthTextureAccess), typeof(EnvironmentDepthManager))]
    public class StableObjectTrackerFromAgent : MonoBehaviour
    {
        [Header("Prefab")]
        public GameObject boundingBoxPrefab;

        [Header("Tracking")]
        public float lostTimeout = 1.5f;
        public float mergeDistance = 0.4f;
        [Range(0.01f, 1f)] public float positionSmoothing = 0.5f;
        [Range(0.01f, 1f)] public float scaleSmoothing = 0.5f;

        [Header("Detection Filtering")]
        [Range(0f, 1f)] public float scoreThreshold = 0.6f;

        [Header("Label")]
        public float labelHeight = 0.05f;
        public float labelSize = 0.02f;

        // ===============================
        // PUBLIC TRACKED OBJECT LIST
        // ===============================
        [System.Serializable]
        public class TrackedObjectData
        {
            public string label; // CLEAN label (e.g. "laptop")
            public GameObject box;
            public GameObject labelObject;
            public Vector3 position;
            public Vector3 scale;
            public float lastSeenTime;
        }

        public List<TrackedObjectData> TrackedObjects = new List<TrackedObjectData>();

        // ===============================
        // Internal tracking state
        // ===============================
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
        // Parse label like:
        // "laptop 0.94"
        // "cup (0.83)"
        // "bottle: 0.71"
        // ===============================
        private bool ParseLabelAndScore(string raw, out string cleanLabel, out float score)
        {
            cleanLabel = raw;
            score = 1.0f;

            if (string.IsNullOrEmpty(raw))
                return false;

            raw = raw.Trim();

            // 1) Parentheses: "cup (0.83)"
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

            // 2) Colon: "cup: 0.83"
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

            // 3) Space: "laptop 0.94"
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

            // 4) Fallback: no score found
            cleanLabel = raw;
            score = 1.0f;
            return true;
        }

        // ===============================
        // Find best tracker for detection
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

        // ===============================
        // Main detection handler
        // ===============================
        private void HandleBatch(List<BoxData> batch)
        {
            float now = Time.time;

            foreach (var b in batch)
            {
                // DEBUG: show raw label
                // Debug.Log("[StableObjectTracker] RAW LABEL = " + b.label);

                if (!ParseLabelAndScore(b.label, out string label, out float score))
                    continue;

                // DEBUG: show parsed result
                // Debug.Log($"[StableObjectTracker] PARSED label='{label}' score={score}");

                if (score < scoreThreshold)
                    continue;

                var xmin = b.position.x;
                var ymin = b.position.y;
                var xmax = b.scale.x;
                var ymax = b.scale.y;

                if (!TryProject(xmin, ymin, xmax, ymax, out var pos, out var rot, out var scl))
                    continue;

                var tracker = FindBestTracker(label, pos);

                if (tracker == null)
                {
                    // ===============================
                    // Create new tracker
                    // ===============================
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
                        lastSeenTime = now
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

                    Debug.Log("[StableObjectTracker] Created box for " + label);
                }
                else
                {
                    tracker.data.lastSeenTime = now;
                    tracker.targetPos = pos;
                    tracker.targetRot = rot;
                    tracker.targetScale = scl;

                    // Update label text with latest score
                    if (tracker.data.labelObject != null)
                    {
                        var tm = tracker.data.labelObject.GetComponent<TextMesh>();
                        if (tm != null)
                            tm.text = $"{label} ({score:0.00})";
                    }
                }
            }

            // ===============================
            // Remove lost trackers
            // ===============================
            for (int i = _trackers.Count - 1; i >= 0; i--)
            {
                var t = _trackers[i];
                if (now - t.data.lastSeenTime > lostTimeout)
                {
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

                // Billboard label
                if (d.labelObject != null && cam != null)
                {
                    Vector3 dir = d.labelObject.transform.position - cam.transform.position;
                    if (dir.sqrMagnitude > 0.0001f)
                        d.labelObject.transform.rotation = Quaternion.LookRotation(dir.normalized);
                }
            }
        }

        // ===============================
        // Projection (unchanged)
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
            if (d <= 0 || d > 20 || float.IsInfinity(d)) return false;

            world = _frame.Pose.position + _frame.Pose.rotation * (dirCam * d);
            rot = Quaternion.LookRotation(world - _frame.Pose.position);
            var w = (xmax - xmin) / _frame.CameraIntrinsics.FocalLength.x * d;
            var h = (ymax - ymin) / _frame.CameraIntrinsics.FocalLength.y * d;
            scale = new Vector3(w, h, 1f);
            return true;
        }
    }
}
