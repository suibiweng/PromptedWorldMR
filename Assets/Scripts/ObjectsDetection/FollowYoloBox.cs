using System.Linq;
using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// Attach to a spawned marker to keep it following the latest detection
    /// of the same YOLO class. It uses smooth damp so it looks stable between updates.
    public class FollowYoloBox : MonoBehaviour
    {
        [SerializeField] private SentisInferenceUiManager _ui; // set by Init
        [SerializeField] private string _targetClass;          // set by Init
        [SerializeField] private float _smoothTime = 0.08f;    // lower = snappier
        [SerializeField] private float _maxSnap = 0.35f;       // meters; snap if jump > this
        [SerializeField] private int _maxMissFrames = 12;

        private Vector3 _vel;               // for SmoothDamp
        private Vector3 _lastGoodPos;
        private int _missFrames;

        public void Init(SentisInferenceUiManager ui, string className, float? maxSnapMeters = null)
        {
            _ui = ui;
            _targetClass = className;
            if (maxSnapMeters.HasValue) _maxSnap = Mathf.Max(0.05f, maxSnapMeters.Value * 2f); // scale with your spawn distance
            _lastGoodPos = transform.position;
        }

        void Update()
        {
            if (_ui == null) return;

            // Find the best current box of my class, prioritize closest to my current position
            var boxes = _ui.BoxDrawn;
            var candidates = boxes.Where(b =>
                    !string.IsNullOrEmpty(b.ClassName) &&
                    b.ClassName == _targetClass &&
                    b.WorldPos.HasValue).ToList();

            if (candidates.Count == 0)
            {
                // No detection for my class this frame
                _missFrames++;
                if (_missFrames > _maxMissFrames)
                {
                    // Optional: fade/hide after long loss; here we just stay at last position
                    _missFrames = _maxMissFrames;
                }
                return;
            }

            // Pick nearest candidate to current marker position
            Vector3 here = transform.position;
            var best = candidates
                .OrderBy(b => Vector3.SqrMagnitude((b.WorldPos ?? here) - here))
                .First();

            Vector3 target = best.WorldPos ?? _lastGoodPos;

            // If jump is huge (e.g., ID switch), hard snap to avoid lag trails
            if (Vector3.Distance(_lastGoodPos, target) > _maxSnap)
            {
                transform.position = target;
                _vel = Vector3.zero;
            }
            else
            {
                transform.position = Vector3.SmoothDamp(transform.position, target, ref _vel, _smoothTime);
            }

            _lastGoodPos = transform.position;
            _missFrames = 0;
        }
    }
}
