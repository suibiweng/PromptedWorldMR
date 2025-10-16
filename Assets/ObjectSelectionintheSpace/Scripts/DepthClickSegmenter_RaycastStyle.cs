using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Meta.XR; // EnvironmentRaycastManager, EnvironmentRaycastHit

namespace Meta.XR.MRUtilityKitSamples.ObjectSegmentation
{
    public class DepthClickSegmenter_RaycastStyle : MonoBehaviour
    {
        [Header("Refs (match MRUK sample)")]
        [SerializeField] private EnvironmentRaycastManager _raycastManager;
        [SerializeField] private Transform _centerEyeAnchor;          // optional
        [SerializeField] private Transform _raycastAnchor;            // controller/aim pointer
        [SerializeField] private LineRenderer _raycastVisualizationLine;
        [SerializeField] private Transform _raycastVisualizationNormal;

        [Header("Depth Overlay")]
        [SerializeField] private DepthSegmentationOverlay _segmenter;
        [SerializeField] private float _maxRayDistance = 6f;

        [Header("Input (OVR)")]
        [SerializeField] private OVRInput.RawButton _triggerButton = OVRInput.RawButton.RIndexTrigger;
        [SerializeField] private OVRInput.RawButton _clearButton   = OVRInput.RawButton.B;

        [Header("Normal Smoothing")]
        [Range(1, 30)] public int normalSmoothWindow = 10;
        [Range(0f, 1f)] public float hitConfidenceMin = 0.05f;

        [Header("Dev / Editor")]
        [SerializeField] private bool _skipHmdWaitInEditor = true;
        [SerializeField] private bool _debugLogs = false;

        private RollingAverage _normalFilter;
        private bool _enabledOnce;

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
            // Wait for HMD like MRUK, but allow Editor play without a headset
            if (!(Application.isEditor && _skipHmdWaitInEditor))
            {
                enabled = false;
                while (!OVRPlugin.userPresent || !OVRManager.isHmdPresent) { yield return null; }
                yield return null;
                enabled = true;
            }

            _normalFilter = new RollingAverage(Mathf.Max(1, normalSmoothWindow));
            _enabledOnce = true;
            if (_debugLogs) Debug.Log("[DepthClickSegmenter] Ready: pull trigger to segment.");
        }

        private void Update()
        {
            if (!_enabledOnce) return;

            VisualizeRaycast();

            if (OVRInput.GetDown(_clearButton))
            {
                if (_segmenter) _segmenter.Hide();
            }

            if (OVRInput.GetDown(_triggerButton))
            {
                if (!TryGetSeed(out var seed))
                {
                    if (_debugLogs) Debug.Log("[DepthClickSegmenter] No valid seed this frame.");
                    return;
                }

                var nSmoothed = _normalFilter.Update(seed.normal).normalized;

                if (_segmenter)
                {
                    _segmenter.SegmentAt(seed.point, nSmoothed);
                }
                else if (_debugLogs) Debug.LogWarning("[DepthClickSegmenter] _segmenter not assigned.");
            }
        }

        private Ray GetRaycastRay()
        {
            var origin = _raycastAnchor.position + _raycastAnchor.forward * 0.1f; // same nudge as MRUK sample
            return new Ray(origin, _raycastAnchor.forward);
        }

        private void VisualizeRaycast()
        {
            if (!_raycastVisualizationLine && !_raycastVisualizationNormal) return;

            var ray = GetRaycastRay();
            EnvironmentRaycastHit envHit = default;
            bool hasHit = _raycastManager && _raycastManager.Raycast(ray, out envHit, _maxRayDistance);
            bool hasNormal = hasHit && envHit.normalConfidence > 0f;

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

        private bool TryGetSeed(out EnvironmentRaycastHit seed)
        {
            seed = default;
            if (!_raycastManager) return false;

            var ray = GetRaycastRay();
            if (!_raycastManager.Raycast(ray, out var hit, _maxRayDistance)) return false;
            if (hit.normalConfidence < hitConfidenceMin) return false;

            // Ignore ceilings (like sample)
            bool isCeiling = Vector3.Dot(hit.normal, Vector3.down) > 0.7f;
            if (isCeiling) return false;

            seed = hit;
            return true;
        }

        private class RollingAverage
        {
            private readonly List<Vector3> _window = new();
            private int _size;
            private int _idx;

            public RollingAverage(int size) { _size = Mathf.Max(1, size); }

            public Vector3 Update(Vector3 current)
            {
                if (_window.Count == 0)
                {
                    for (int i = 0; i < _size; i++) _window.Add(current);
                    _idx = 0;
                }
                _idx++;
                _window[_idx % _size] = current;
                Vector3 sum = Vector3.zero;
                for (int i = 0; i < _size; i++) sum += _window[i];
                return sum / _size;
            }
        }
    }
}
