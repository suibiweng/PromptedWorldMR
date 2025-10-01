// (c) Meta Platforms, Inc. and affiliates. Confidential and proprietary.

using System.Collections;
using UnityEngine;
using Meta.XR.MRUtilityKit;
using Oculus.Interaction;
using UnityEngine.UI;

namespace Meta.XR.TrackedKeyboardSample
{
    /// <summary>
    /// Controls the tracked keyboard behavior in mixed reality.
    /// </summary>
    public sealed class TrackedKeyboardManager : MonoBehaviour
    {
        [Header("Prefabs and References")]
        [SerializeField, Tooltip("Prefab for the tracked keyboard.")]
        private GameObject _keyboardPrefab;

        [SerializeField, Tooltip("Reference to the left hand GameObject.")]
        private GameObject _leftHand;

        [SerializeField, Tooltip("Reference to the right hand GameObject.")]
        private GameObject _rightHand;

        [Tooltip("Objects that shouldn't be rendered during full passthrough")]
        [SerializeField] private GameObject[] _objects;

        [SerializeField, Tooltip("Boundary visualizer implementation.")]
        private BoundaryVisual _boundaryVisualImplementation;

        [SerializeField, Tooltip("Toggle button to show/hide the passive keyboard visual")]
        private Toggle _passiveVisualToggle;

        [SerializeField]
        private Transform _deskTransform;

        private KeyboardInteractionManager _handDetector;
        private HandVisual _leftHandVisual;
        private HandVisual _rightHandVisual;
        private MaterialPropertyBlockEditor _leftHandMaterialEditor;
        private MaterialPropertyBlockEditor _rightHandMaterialEditor;
        private RayInteractor _leftRayInteractor;
        private RayInteractor _rightRayInteractor;
        private Bounded2DVisualizer _boundaryVisualizer;
        private TouchScreenKeyboard _overlayKeyboard;
        private bool _isMRMode = false;
        private float _deskHeightOffset = 0.015f;
        private bool _awaitingDialogueResponse = false;

        public MRUKTrackable Trackable { get; private set; }

        private void Awake()
        {
            InitializeHands();
        }

        /// <summary>
        /// Initializes hand visuals and interactors.
        /// </summary>
        private void InitializeHands()
        {
            if (_leftHand != null)
            {
                _leftHandVisual = _leftHand.GetComponentInChildren<HandVisual>();
                _leftRayInteractor = _leftHand.GetComponentInChildren<RayInteractor>();
                _leftHandMaterialEditor = _leftHandVisual.GetComponentInChildren<MaterialPropertyBlockEditor>();
            }
            else
            {
                Debug.LogWarning("Left hand reference is not assigned.");
            }

            if (_rightHand != null)
            {
                _rightHandVisual = _rightHand.GetComponentInChildren<HandVisual>();
                _rightRayInteractor = _rightHand.GetComponentInChildren<RayInteractor>();
                _rightHandMaterialEditor = _rightHandVisual.GetComponentInChildren<MaterialPropertyBlockEditor>();
            }
            else
            {
                Debug.LogWarning("Right hand reference is not assigned.");
            }
        }

        /// <summary>
        /// Called when a trackable is added.
        /// </summary>
        /// <param name="trackable">The added MRUKTrackable.</param>
        public void OnTrackableAdded(MRUKTrackable trackable)
        {
            if (trackable.TrackableType != OVRAnchor.TrackableType.Keyboard)
                return;

            if (_keyboardPrefab == null)
            {
                Debug.LogError("Keyboard prefab is not assigned.");
                return;
            }

            var newGameObject = Instantiate(_keyboardPrefab, trackable.transform);
            Trackable = trackable;

            _boundaryVisualizer = newGameObject.GetComponentInChildren<Bounded2DVisualizer>();
            if (_boundaryVisualizer != null)
            {
                // Initialize with the selected BoundaryVisual implementation
                _boundaryVisualizer.Initialize(trackable, _boundaryVisualImplementation);
                ToggleBoundaryVisual(_passiveVisualToggle.isOn);
            }
            else
            {
                Debug.LogWarning("Bounded3DVisualizer not found on the instantiated prefab.");
            }

            _handDetector = newGameObject.GetComponentInChildren<KeyboardInteractionManager>();
            if (_handDetector != null)
            {
                _handDetector.LeftHandVisual = _leftHandVisual;
                _handDetector.RightHandVisual = _rightHandVisual;
                _handDetector.LeftRayInteractor = _leftRayInteractor;
                _handDetector.RightRayInteractor = _rightRayInteractor;
                _handDetector.LeftHandPropertyBlock = _leftHandMaterialEditor;
                _handDetector.RightHandPropertyBlock = _rightHandMaterialEditor;
            }
            else
            {
                Debug.LogWarning("HandProximityDetector not found on the instantiated prefab.");
            }

            if (trackable.VolumeBounds != null)
            {
                float deskHeight = trackable.transform.position.y - (_boundaryVisualizer.BoxCollider.size.z / 2f) - _deskHeightOffset;
                _deskTransform.position = new Vector3(_deskTransform.position.x, deskHeight,
                    _deskTransform.position.z);
            }
        }

        /// <summary>
        /// Called when a trackable is removed.
        /// </summary>
        /// <param name="trackable">The removed MRUKTrackable.</param>
        public void OnTrackableRemoved(MRUKTrackable trackable)
        {
            if (_leftHandVisual != null)
                _leftHandVisual.ForceOffVisibility = false;

            if (_rightHandVisual != null)
                _rightHandVisual.ForceOffVisibility = false;

            if (_leftRayInteractor != null)
                _leftRayInteractor.enabled = true;

            if (_rightRayInteractor != null)
                _rightRayInteractor.enabled = true;

            Trackable = null;
            _handDetector = null;

            Destroy(trackable.gameObject);
        }

        /// <summary>
        /// Toggles the visibility of the keyboard boundary.
        /// </summary>
        /// <param name="visible">Whether the boundary should be visible.</param>
        public void ToggleBoundaryVisual(bool visible)
        {
            if (_boundaryVisualizer == null)
            {
                return;
            }

            bool shouldShow = !_isMRMode && visible;
            _boundaryVisualizer.SetUserEnabled(shouldShow);
        }

        /// <summary>
        /// Toggles Mixed Reality mode.
        /// </summary>
        public void ToggleMrMode()
        {
            _isMRMode = !_isMRMode;

            if (_boundaryVisualizer != null)
                _boundaryVisualizer.SetUserEnabled(!_isMRMode);

            if (Camera.main != null)
                Camera.main.clearFlags = _isMRMode ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;

            foreach (GameObject obj in _objects)
            {
                obj.SetActive(!_isMRMode);
            }
        }

        /// <summary>
        /// Launches the local keyboard selection dialog.
        /// </summary>
        public void LaunchLocalKeyboardSelectionDialog()
        {
            LaunchOverlayIntent("systemux://dialog/enable-tracked-keyboard?default_action=enable");
            _awaitingDialogueResponse = true;
        }

        /// <summary>
        /// Checks the keyboard presence once the external dialogue is closed and the app regains focus.
        /// </summary>
        /// <param name="hasFocus"></param>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (_awaitingDialogueResponse && hasFocus)
            {
                StartCoroutine(CheckKeyboardPresence());
                _awaitingDialogueResponse = false;
            }
        }

        private IEnumerator CheckKeyboardPresence()
        {
            yield return new WaitForSeconds(0.5f);

            if (!HasActiveKeyboard())
            {
                StartCoroutine(RefreshMRUKCoroutine());
            }
        }

        private bool HasActiveKeyboard()
        {
            if (MRUK.Instance && MRUK.Instance.IsInitialized)
            {
                if (Trackable && Trackable.TrackableType == OVRAnchor.TrackableType.Keyboard)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Forces a refresh of the MRUK tracking system to detect externally-enabled keyboard tracking, after app launch.
        /// </summary>
        private IEnumerator RefreshMRUKCoroutine()
        {
            if (MRUK.Instance)
            {
                MRUK.Instance.enabled = false;
                yield return null;
                MRUK.Instance.enabled = true;
            }
        }

        /// <summary>
        /// Launches an overlay intent with the specified URI.
        /// </summary>
        /// <param name="dataUri">The URI for the intent.</param>
        private void LaunchOverlayIntent(string dataUri)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                    using (var intent = new AndroidJavaObject("android.content.Intent"))
                    {
                        intent.Call<AndroidJavaObject>("setPackage", "com.oculus.vrshell");
                        intent.Call<AndroidJavaObject>("setAction", "com.oculus.vrshell.intent.action.LAUNCH");
                        intent.Call<AndroidJavaObject>("putExtra", "intent_data", dataUri);
                        currentActivity.Call("sendBroadcast", intent);
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to launch overlay intent: {e.Message}");
            }
#endif
        }
    }
}
