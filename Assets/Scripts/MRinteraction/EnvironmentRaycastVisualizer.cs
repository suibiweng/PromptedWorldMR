/*
 * Minimal environment raycast visualizer:
 * Draws a line to the hit point and a transform aligned to the hit normal.
 * Toggle `showLine` to enable or disable line rendering.
 */

using Meta.XR.Samples;
using UnityEngine;

namespace Meta.XR.MRUtilityKitSamples.EnvironmentPanelPlacement
{
    [MetaCodeSample("MRUKSample-EnvironmentPanelPlacement")]
    public class EnvironmentRaycastVisualizer : MonoBehaviour
    {
        [Header("Raycast")]
        [SerializeField] private EnvironmentRaycastManager _raycastManager;
        [SerializeField] private Transform _raycastAnchor;
        [SerializeField, Tooltip("Forward offset to avoid hitting the controller itself.")]
        private float _originForwardOffset = 0.1f;

        [Header("Visualization")]
        [SerializeField] private LineRenderer _raycastVisualizationLine;
        [SerializeField] private Transform _raycastVisualizationNormal;
        [SerializeField, Range(0f, 1f), Tooltip("Minimum confidence to show a normal.")]
        private float _normalConfidenceThreshold = 0.01f;

        [Header("Display Options")]
        [Tooltip("Show or hide the ray line visualization.")]
        public bool showLine = true;

        private void Reset()
        {
            _originForwardOffset = 0.1f;
            _normalConfidenceThreshold = 0.01f;
            showLine = true;
        }

        private void Update()
        {
            VisualizeRaycast();
        }

        private Ray GetRaycastRay()
        {
            var origin = _raycastAnchor.position + _raycastAnchor.forward * _originForwardOffset;
            return new Ray(origin, _raycastAnchor.forward);
        }

        private void VisualizeRaycast()
        {
            if (_raycastManager == null || _raycastAnchor == null || _raycastVisualizationLine == null || _raycastVisualizationNormal == null)
            {
                return;
            }

            var ray = GetRaycastRay();
            bool hitSomething = _raycastManager.Raycast(ray, out var hit);

            bool hasHit = hitSomething || hit.status == EnvironmentRaycastHitStatus.HitPointOccluded;
            bool hasNormal = hit.normalConfidence >= _normalConfidenceThreshold;

            // Apply visibility toggles
            _raycastVisualizationLine.enabled = hasHit && showLine;
            _raycastVisualizationNormal.gameObject.SetActive(hasHit && hasNormal);

            if (!hasHit)
            {
                return;
            }

            // Draw line
            if (showLine)
            {
                _raycastVisualizationLine.SetPosition(0, ray.origin);
                _raycastVisualizationLine.SetPosition(1, hit.point);
            }

            // Draw normal
            if (hasNormal)
            {
                _raycastVisualizationNormal.SetPositionAndRotation(hit.point, Quaternion.LookRotation(hit.normal));
            }
        }
    }
}
