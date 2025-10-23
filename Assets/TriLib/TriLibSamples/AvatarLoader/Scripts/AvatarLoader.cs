#pragma warning disable 649
using System;
using TriLibCore.Extensions;
using TriLibCore.General;
using TriLibCore.Mappers;
using TriLibCore.Utils;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace TriLibCore.Samples
{
    /// <summary>
    /// Demonstrates how to load and control a custom humanoid avatar using TriLib.
    /// This class extends <see cref="AbstractInputSystem"/> to handle user input,
    /// integrate TriLib’s file-loading and network-loading features, 
    /// and manage avatar loading and scaling in conjunction with <see cref="AvatarController"/>.
    /// </summary>
    public class AvatarLoader : AbstractInputSystem
    {
        /// <summary>
        /// A static instance of this class, allowing simple global access.
        /// Set in <see cref="Start"/>, ensuring there is only one active <see cref="AvatarLoader"/>.
        /// </summary>
        public static AvatarLoader Instance { get; private set; }

        /// <summary>
        /// A <see cref="RectTransform"/> representing a loading bar that displays
        /// model or skybox loading progress (used on platforms supporting asynchronous loading).
        /// </summary>
        [SerializeField]
        private RectTransform _loadingBar;

        /// <summary>
        /// A <see cref="GameObject"/> that wraps help information or a help overlay in the UI.
        /// </summary>
        [SerializeField]
        private GameObject _helpWrapper;

        /// <summary>
        /// A <see cref="GameObject"/> wrapper to indicate loading progress
        /// (used on platforms that do not fully support asynchronous loading).
        /// </summary>
        [SerializeField]
        private GameObject _loadingWrapper;

        /// <summary>
        /// A <see cref="GameObject"/> representing a dialog for entering a model URL to load.
        /// </summary>
        [SerializeField]
        private GameObject _modelUrlDialog;

        /// <summary>
        /// An <see cref="InputField"/> in which the user can type or paste a model URL.
        /// </summary>
        [SerializeField]
        private InputField _modelUrl;

        /// <summary>
        /// A <see cref="Slider"/> controlling animation playback. 
        /// Displays or manipulates the normalized time of the current animation.
        /// </summary>
        [SerializeField]
        public Slider PlaybackSlider;

        /// <summary>
        /// A <see cref="Text"/> used to display the current time or frame within the played animation.
        /// </summary>
        [SerializeField]
        public Text PlaybackTime;

        /// <summary>
        /// A <see cref="Dropdown"/> used to select different animations for playback.
        /// </summary>
        [SerializeField]
        public Dropdown PlaybackAnimation;

        /// <summary>
        /// A <see cref="Selectable"/> (e.g., a button) that triggers animation playback.
        /// </summary>
        [SerializeField]
        public Selectable Play;

        /// <summary>
        /// A <see cref="Selectable"/> (e.g., a button) that stops the current animation.
        /// </summary>
        [SerializeField]
        public Selectable Stop;

        /// <summary>
        /// A <see cref="GameObject"/> used to visually hide or wrap the loaded model during loading.
        /// Passed to TriLib’s load methods if desired.
        /// </summary>
        [SerializeField]
        private GameObject _wrapper;

        /// <summary>
        /// A TriLib <see cref="HumanoidAvatarMapper"/> that defines how the
        /// avatar’s bones map to Unity’s humanoid rig.
        /// </summary>
        [SerializeField]
        private HumanoidAvatarMapper _humanoidAvatarMapper;

        /// <summary>
        /// The TriLib loader options used for model loading in this sample.
        /// These options can be preconfigured to support humanoid animation
        /// or other specialized settings.
        /// </summary>
        public AssetLoaderOptions AssetLoaderOptions;

        /// <summary>
        /// Holds the current camera pitch (Y-axis rotation) and yaw (X-axis rotation),
        /// allowing the camera to orbit around the loaded model.
        /// </summary>
        public Vector2 CameraAngle;

        /// <summary>
        /// A reference to the loaded avatar’s root <see cref="GameObject"/>.
        /// This is set once meshes and hierarchy are loaded, and can be destroyed or replaced
        /// when the user loads a new model.
        /// </summary>
        public GameObject RootGameObject { get; set; }

        /// <summary>
        /// A ratio that scales mouse input. Larger values make camera movement
        /// more sensitive to mouse movement.
        /// </summary>
        public const float InputMultiplierRatio = 0.1f;

        /// <summary>
        /// The maximum pitch angle (rotation around the local X-axis) to prevent
        /// flipping the camera.
        /// </summary>
        public const float MaxPitch = 80f;

        /// <summary>
        /// The <see cref="AssetLoaderFilePicker"/> created for this viewer,
        /// enabling file or directory selection for model loading.
        /// </summary>
        public AssetLoaderFilePicker FilePickerAssetLoader;

        /// <summary>
        /// Tracks the peak memory usage (in bytes) observed during model loading.
        /// </summary>
        public long PeakMemory;

#if TRILIB_SHOW_MEMORY_USAGE
        /// <summary>
        /// Tracks the peak managed memory usage (in bytes) observed during model loading
        /// (only available if <c>TRILIB_SHOW_MEMORY_USAGE</c> is defined).
        /// </summary>
        public long PeakManagedMemory;
#endif

        /// <summary>
        /// Indicates whether a model is currently being loaded.
        /// </summary>
        private bool _loading;

        /// <summary>
        /// Loads an avatar from a file, using the internal wrapper object.
        /// This method delegates the actual file loading operation to <c>LoadModelFromFile</c>.
        /// </summary>
        public void LoadAvatarFromFile()
        {
            LoadModelFromFile(_wrapper);
        }

        /// <summary>
        /// Called by Unity when the script instance is first enabled. Sets up singletons,
        /// configures default <see cref="AssetLoaderOptions"/> for humanoid avatars,
        /// and adjusts the scale of any already-loaded avatar.
        /// </summary>
        public void Start()
        {
            // Ensure singletons are properly initialized for TriLib’s infrastructure
            Dispatcher.CheckInstance();
            PasteManager.CheckInstance();

            // Set the global instance of AvatarLoader
            Instance = this;

            // If no loader options have been set, configure a default set
            if (AssetLoaderOptions == null)
            {
                AssetLoaderOptions = AssetLoader.CreateDefaultLoaderOptions(false, true);
                AssetLoaderOptions.AnimationType = AnimationType.Humanoid;
                AssetLoaderOptions.HumanoidAvatarMapper = _humanoidAvatarMapper;
            }

            // If there is an existing avatar in the scene (e.g., from a previous load), 
            // scale it to fit the character controller
            if (AvatarController.Instance != null && AvatarController.Instance.InnerAvatar != null)
            {
                var bounds = AvatarController.Instance.InnerAvatar.CalculateBounds();
                var factor = AvatarController.Instance.CharacterController.height / bounds.size.y;
                AvatarController.Instance.InnerAvatar.transform.localScale = factor * Vector3.one;
            }
        }

        /// <summary>
        /// Checks each frame for mouse input to lock or unlock the cursor,
        /// and updates the camera view if the cursor is locked.
        /// </summary>
        private void Update()
        {
            // Toggle cursor lock state on right mouse button click
            if (GetMouseButtonDown(1))
            {
                Cursor.lockState =
                    (Cursor.lockState == CursorLockMode.None)
                    ? CursorLockMode.Locked
                    : CursorLockMode.None;
            }

            // Only rotate the camera if the mouse is locked
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                UpdateCamera();
            }
        }

        /// <summary>
        /// Updates the camera angles (pitch and yaw) based on mouse input,
        /// applying <see cref="InputMultiplierRatio"/> to control sensitivity
        /// and limiting pitch to avoid flipping the camera.
        /// </summary>
        public void UpdateCamera()
        {
            CameraAngle.x = Mathf.Repeat(CameraAngle.x + GetAxis("Mouse X") * InputMultiplierRatio, 360f);
            CameraAngle.y = Mathf.Clamp(CameraAngle.y + GetAxis("Mouse Y") * InputMultiplierRatio, -MaxPitch, MaxPitch);
        }

        /// <summary>
        /// Displays a help overlay or panel in the UI.
        /// </summary>
        public void ShowHelp()
        {
            _helpWrapper.SetActive(true);
        }

        /// <summary>
        /// Hides the help overlay or panel in the UI.
        /// </summary>
        public void HideHelp()
        {
            _helpWrapper.SetActive(false);
        }

        /// <summary>
        /// Displays a file picker dialog so the user can select a local model file
        /// (e.g., FBX, OBJ) to load. If a <paramref name="wrapperGameObject"/> is provided,
        /// TriLib will place the loaded model under that object’s hierarchy.
        /// </summary>
        /// <param name="wrapperGameObject">An optional object to serve as the loaded model’s parent.</param>
        /// <param name="onMaterialsLoad">An optional callback to override <see cref="OnMaterialsLoad"/>.</param>
        public void LoadModelFromFile(GameObject wrapperGameObject = null, Action<AssetLoaderContext> onMaterialsLoad = null)
        {
            SetLoading(false);
            FilePickerAssetLoader = AssetLoaderFilePicker.Create();
            FilePickerAssetLoader.LoadModelFromFilePickerAsync(
                "Select a File",
                OnLoad,
                onMaterialsLoad ?? OnMaterialsLoad,
                OnProgress,
                OnBeginLoadModel,
                OnError,
                wrapperGameObject ? wrapperGameObject : gameObject,
                AssetLoaderOptions
            );
        }

        /// <summary>
        /// Displays a directory picker dialog so the user can select a folder containing
        /// model files to load. Optionally sets the loaded model’s parent to <paramref name="wrapperGameObject"/>.
        /// </summary>
        /// <param name="wrapperGameObject">An optional object to serve as the loaded model’s parent.</param>
        /// <param name="onMaterialsLoad">An optional callback to override <see cref="OnMaterialsLoad"/>.</param>
        public void LoadModelFromDirectory(GameObject wrapperGameObject = null, Action<AssetLoaderContext> onMaterialsLoad = null)
        {
            SetLoading(false);
            FilePickerAssetLoader = AssetLoaderFilePicker.Create();
            FilePickerAssetLoader.LoadModelFromDirectoryPickerAsync(
                "Select a Directory",
                OnLoad,
                onMaterialsLoad ?? OnMaterialsLoad,
                OnProgress,
                OnBeginLoadModel,
                OnError,
                wrapperGameObject ? wrapperGameObject : gameObject,
                AssetLoaderOptions,
                true
            );
        }

        /// <summary>
        /// Displays the URL loading dialog, focusing the input field 
        /// so the user can paste or type a model URL.
        /// </summary>
        public void ShowModelUrlDialog()
        {
            _modelUrlDialog.SetActive(true);
            _modelUrl.Select();
            _modelUrl.ActivateInputField();
        }

        /// <summary>
        /// Hides the URL loading dialog, clearing any typed model URL.
        /// </summary>
        public void HideModelUrlDialog()
        {
            _modelUrlDialog.SetActive(false);
            _modelUrl.text = null;
        }

        /// <summary>
        /// Loads a model from the URL specified in the URL dialog’s input field.
        /// Closes the dialog once loading begins.
        /// </summary>
        public void LoadModelFromURLWithDialogValues()
        {
            if (string.IsNullOrWhiteSpace(_modelUrl.text))
            {
                return;
            }
            var trimmedUrl = _modelUrl.text.Trim();
            var request = AssetDownloader.CreateWebRequest(trimmedUrl);

            // Attempt to derive the file extension from the final segment of the URL
            var fileExtension = FileUtils.GetFileExtension(request.uri.Segments[request.uri.Segments.Length - 1], false);
            try
            {
                LoadModelFromURL(request, fileExtension);
            }
            catch (Exception e)
            {
                HideModelUrlDialog();
                OnError(new ContextualizedError<object>(e, null));
            }
        }

        /// <summary>
        /// Loads a model from a custom <see cref="UnityWebRequest"/> and file extension,
        /// potentially handling zip archives if <paramref name="fileExtension"/> indicates so.
        /// </summary>
        /// <param name="request">A <see cref="UnityWebRequest"/> pointing to a model file.</param>
        /// <param name="fileExtension">The file extension (e.g., <c>fbx</c>, <c>zip</c>).</param>
        /// <param name="wrapperGameObject">An optional object to serve as the loaded model’s parent.</param>
        /// <param name="customData">Optional user data to pass along with the load process.</param>
        /// <param name="onMaterialsLoad">Optional callback for completion logic after textures/materials load.</param>
        /// <exception cref="Exception">Thrown if <paramref name="fileExtension"/> cannot be determined.</exception>
        public void LoadModelFromURL(
            UnityWebRequest request,
            string fileExtension,
            GameObject wrapperGameObject = null,
            object customData = null,
            Action<AssetLoaderContext> onMaterialsLoad = null)
        {
            if (string.IsNullOrWhiteSpace(fileExtension))
            {
                throw new Exception("TriLib could not determine the given model extension.");
            }
            HideModelUrlDialog();
            SetLoading(true);
            OnBeginLoadModel(true);

            fileExtension = fileExtension.ToLowerInvariant();
            var isZipFile = (fileExtension == "zip" || fileExtension == ".zip");

            AssetDownloader.LoadModelFromUri(
                unityWebRequest: request,
                onLoad: OnLoad,
                onMaterialsLoad: onMaterialsLoad ?? OnMaterialsLoad,
                onProgress: OnProgress,
                onError: OnError,
                wrapperGameObject: wrapperGameObject,
                assetLoaderOptions: AssetLoaderOptions,
                customContextData: customData,
                fileExtension: isZipFile ? null : fileExtension,
                isZipFile: isZipFile
            );
        }

        /// <summary>
        /// Invoked when the user selects a file or directory in the picker,
        /// or cancels the selection. Resets the scene if a file was chosen.
        /// </summary>
        /// <param name="hasFiles">
        /// True if the user selected a file/directory; false if the user canceled.
        /// </param>
        public void OnBeginLoadModel(bool hasFiles)
        {
            if (hasFiles)
            {
                Resources.UnloadUnusedAssets();

                // Destroy any existing loaded model
                if (RootGameObject != null)
                {
                    Destroy(RootGameObject);
                }
                SetLoading(true);
            }
        }

        /// <summary>
        /// Invoked to report model loading progress, receiving a value [0..1].
        /// Updates the UI loading bar’s width to reflect current progress.
        /// </summary>
        /// <param name="assetLoaderContext">Provides info about the current load process.</param>
        /// <param name="value">A float from 0.0 to 1.0 indicating loading progress.</param>
        public void OnProgress(AssetLoaderContext assetLoaderContext, float value)
        {
            _loadingBar.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Screen.width * value);
        }

        /// <summary>
        /// Invoked if any error occurs while loading the model. Logs the error,
        /// clears the reference to the loaded model, and disables the loading UI.
        /// </summary>
        /// <param name="contextualizedError">
        /// An object containing exception details and any relevant load context.
        /// </param>
        public void OnError(IContextualizedError contextualizedError)
        {
            Debug.LogError(contextualizedError);
            RootGameObject = null;
            SetLoading(false);
        }

        /// <summary>
        /// Invoked once the model’s meshes and hierarchy have been loaded,
        /// but before textures and materials are processed. Resets memory usage counters
        /// in this sample. Specific logic (e.g., camera fitting) may be placed here.
        /// </summary>
        /// <param name="assetLoaderContext">Contains references and data about the loaded model.</param>
        public void OnLoad(AssetLoaderContext assetLoaderContext)
        {
            PeakMemory = 0;
#if TRILIB_SHOW_MEMORY_USAGE
            PeakManagedMemory = 0;
#endif
        }

        /// <summary>
        /// Invoked after textures and materials are loaded, indicating the model is fully ready.
        /// Disables the loading screen and, if needed, integrates the newly loaded model with
        /// the <see cref="AvatarController"/>.
        /// </summary>
        /// <param name="assetLoaderContext">
        /// The <see cref="AssetLoaderContext"/> with references to the loaded <see cref="GameObject"/>.
        /// </param>
        public void OnMaterialsLoad(AssetLoaderContext assetLoaderContext)
        {
            // Model is fully loaded, so disable any loading UI
            SetLoading(false);

            // If a new avatar was successfully loaded, integrate with the AvatarController
            if (assetLoaderContext.RootGameObject != null)
            {
                var existingInnerAvatar = AvatarController.Instance.InnerAvatar;
                if (existingInnerAvatar != null)
                {
                    Destroy(existingInnerAvatar);
                }

                // Preserve the current animator controller to maintain existing animations
                var controller = AvatarController.Instance.Animator.runtimeAnimatorController;

                // Scale the loaded model to match the character controller
                var bounds = assetLoaderContext.RootGameObject.CalculateBounds();
                var factor = AvatarController.Instance.CharacterController.height / bounds.size.y;
                assetLoaderContext.RootGameObject.transform.localScale = factor * Vector3.one;

                // Assign the loaded model as the new avatar
                AvatarController.Instance.InnerAvatar = assetLoaderContext.RootGameObject;
                assetLoaderContext.RootGameObject.transform.SetParent(
                    AvatarController.Instance.transform,
                    worldPositionStays: false
                );

                // Update the AvatarController’s animator with the loaded model’s animator
                AvatarController.Instance.Animator = assetLoaderContext.RootGameObject.GetComponent<Animator>();
                AvatarController.Instance.Animator.runtimeAnimatorController = controller;
            }
        }

        /// <summary>
        /// Enables or disables the loading state. Disabling the loading state re-enables
        /// all <see cref="Selectable"/> UI elements and hides the loading wrapper/indicator.
        /// </summary>
        /// <param name="value">True if loading is active, false otherwise.</param>
        public void SetLoading(bool value)
        {
#if UNITY_2023_3_OR_NEWER
            var selectables = FindObjectsByType<Selectable>(FindObjectsSortMode.None);
#else
            var selectables = FindObjectsOfType<Selectable>();
#endif
            for (var i = 0; i < selectables.Length; i++)
            {
                selectables[i].interactable = !value;
            }
            _loadingWrapper.gameObject.SetActive(value);
            _loadingBar.gameObject.SetActive(value);
            _loading = value;
        }
    }
}
