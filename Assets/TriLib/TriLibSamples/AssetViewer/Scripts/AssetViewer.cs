#pragma warning disable 649
#pragma warning disable 108
#pragma warning disable 618
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TriLibCore.SFB;
using TriLibCore.Extensions;
using TriLibCore.Extras;
using TriLibCore.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Profiling;
using TriLibCore.General;
using UnityEngine.Networking;
using System.Diagnostics;

namespace TriLibCore.Samples
{
    /// <summary>
    /// A TriLib sample that allows the user to load models and HDR skyboxes from
    /// the local file-system and URLs. The loaded model can be manipulated (rotated, zoomed)
    /// within the scene, and the user can toggle various debug and rendering options.
    /// </summary>
    public class AssetViewer : AbstractInputSystem
    {
        /// <summary>
        /// Specifies how far the camera should be placed from the loaded model,
        /// in relation to the model’s bounding size.
        /// </summary>
        public const float CameraDistanceRatio = 2f;

        /// <summary>
        /// Scales mouse movement inputs (higher means more sensitive movements).
        /// </summary>
        public const float InputMultiplierRatio = 0.1f;

        /// <summary>
        /// The maximum pitch angle for the camera or directional light (rotation around X-axis).
        /// Prevents flipping beyond 80 degrees.
        /// </summary>
        public const float MaxPitch = 80f;

        /// <summary>
        /// The minimum allowable camera distance to the pivot (prevents zooming through the model).
        /// </summary>
        public const float MinCameraDistance = 0.01f;

        /// <summary>
        /// Used for scaling the skybox in relation to the loaded model’s bounding size.
        /// </summary>
        public const float SkyboxScale = 100f;

        /// <summary>
        /// Options controlling how the model is loaded (e.g., whether to import materials,
        /// animations, cameras, etc.). If not set in the inspector, a default is created at runtime.
        /// </summary>
        public AssetLoaderOptions AssetLoaderOptions;

        /// <summary>
        /// Current angles for camera rotation around the loaded model (x = yaw, y = pitch).
        /// </summary>
        public Vector2 CameraAngle;

        /// <summary>
        /// Current camera distance from the <see cref="CameraPivot"/>.
        /// </summary>
        public float CameraDistance = 1f;

        /// <summary>
        /// The position the camera orbits around (usually at or near the model’s center/bounds).
        /// </summary>
        public Vector3 CameraPivot;

        /// <summary>
        /// Reference to the scene’s <see cref="CanvasScaler"/> for configuring UI scaling on different devices.
        /// </summary>
        [SerializeField]
        public CanvasScaler CanvasScaler;

        /// <summary>
        /// An instance of the TriLib file picker for selecting models/directories on supported platforms.
        /// </summary>
        public AssetLoaderFilePicker FilePickerAssetLoader;

        /// <summary>
        /// Dynamically scales input movement based on the loaded model’s bounding size.
        /// Larger models result in a higher <see cref="InputMultiplier"/> for easier navigation.
        /// </summary>
        public float InputMultiplier = 1f;

        /// <summary>
        /// Tracks the peak memory usage (in bytes) observed during model loading.
        /// Reset each time a new model is loaded.
        /// </summary>
        public long PeakMemory;

#if TRILIB_SHOW_MEMORY_USAGE
        /// <summary>
        /// Tracks the peak managed memory usage (in bytes) observed during model loading.
        /// Only available when <c>TRILIB_SHOW_MEMORY_USAGE</c> is defined.
        /// </summary>
        public long PeakManagedMemory;
#endif

        /// <summary>
        /// Button (or other <see cref="Selectable"/>) used to trigger animation playback.
        /// </summary>
        [SerializeField]
        public Selectable Play;

        /// <summary>
        /// Dropdown listing available animation clips on the loaded model.
        /// Allows the user to switch between clips.
        /// </summary>
        [SerializeField]
        public Dropdown PlaybackAnimation;

        /// <summary>
        /// Slider representing the normalized time of the current animation.
        /// Allows manual scrubbing through the clip if the animation is not playing.
        /// </summary>
        [SerializeField]
        public Slider PlaybackSlider;

        /// <summary>
        /// Displays the current animation time in a <c>mm:ss</c> format.
        /// </summary>
        [SerializeField]
        public Text PlaybackTime;

        /// <summary>
        /// A dedicated <see cref="GameObject"/> for rendering the HDR skybox.
        /// </summary>
        [SerializeField]
        public GameObject Skybox;

        /// <summary>
        /// Button (or other <see cref="Selectable"/>) used to stop animation playback.
        /// </summary>
        [SerializeField]
        public Selectable Stop;

        /// <summary>
        /// Defines the maximum ratio for camera distance relative to the model’s bounding size.
        /// Used to clamp the zoom level in <see cref="ProcessInputInternal"/>.
        /// </summary>
        private const float MaxCameraDistanceRatio = 3f;

        /// <summary>
        /// A reference to an <see cref="Animation"/> component attached to the loaded model.
        /// This is used for basic animation playback if the model contains animation clips.
        /// </summary>
        private Animation _animation;

        /// <summary>
        /// A list of available <see cref="AnimationClip"/> objects from the loaded model’s <see cref="_animation"/>.
        /// </summary>
        private List<AnimationClip> _animations;

        /// <summary>
        /// A list of cameras found in the loaded model (if <see cref="AssetLoaderOptions.ImportCameras"/> is true).
        /// The user can switch to these cameras via the UI.
        /// </summary>
        private IList<Camera> _cameras;

        /// <summary>
        /// Dropdown listing all model cameras, plus a default “User Camera” option.
        /// </summary>
        [SerializeField]
        private Dropdown _camerasDropdown;

        /// <summary>
        /// Dropdown for debug or rendering options (e.g., switching to “Show Normals” or “Show Albedo” shaders).
        /// </summary>
        [SerializeField]
        private Dropdown _debugOptionsDropdown;

        /// <summary>
        /// Panel displayed when an error occurs (e.g., model fails to load).
        /// </summary>
        [SerializeField]
        private GameObject _errorPanel;

        /// <summary>
        /// Text used to display the error message within the error panel.
        /// </summary>
        [SerializeField]
        private Text _errorPanelText;

        /// <summary>
        /// Toggle enabling “Fast Mode” for loading. May skip some import steps or detail
        /// in order to load models more quickly.
        /// </summary>
        [SerializeField]
        private Toggle _fastLoadToggle;

        /// <summary>
        /// Indicates if <see cref="AssetLoaderOptions"/> was manually set in the Unity Inspector
        /// (so as not to override certain user-configured options in <see cref="OnFastLoadToggleChanged"/>).
        /// </summary>
        private bool _hasAssetLoader;

        /// <summary>
        /// Wrapper <see cref="GameObject"/> containing help or usage instructions in the UI.
        /// </summary>
        [SerializeField]
        private GameObject _helpWrapper;

        /// <summary>
        /// A <see cref="Light"/> used to illuminate the loaded model. Its orientation can be controlled.
        /// </summary>
        [SerializeField]
        private Light _light;

        /// <summary>
        /// The current yaw (x) and pitch (y) angles for the directional <see cref="Light"/>.
        /// Used similarly to <see cref="CameraAngle"/> for rotation, but for the scene’s main light.
        /// </summary>
        private Vector2 _lightAngle = new Vector2(0f, -45f);

        /// <summary>
        /// Toggle controlling whether to import cameras from the model.
        /// </summary>
        [SerializeField]
        private Toggle _loadCamerasToggle;

        /// <summary>
        /// Indicates if the model (or skybox) is currently in the process of loading.
        /// </summary>
        private bool _loading;

        /// <summary>
        /// A <see cref="RectTransform"/> representing a loading bar that displays model or skybox loading progress.
        /// </summary>
        [SerializeField]
        private RectTransform _loadingBar;

        /// <summary>
        /// UI text displaying how long loading the model took once loading completes.
        /// </summary>
        [SerializeField]
        private Text _loadingTimeText;

        /// <summary>
        /// A <see cref="GameObject"/> representing a loading screen or overlay.
        /// Useful on platforms that don’t fully support asynchronous loading.
        /// </summary>
        [SerializeField]
        private GameObject _loadingWrapper;

        /// <summary>
        /// Toggle controlling whether to import lights from the model.
        /// </summary>
        [SerializeField]
        private Toggle _loadLightsToggle;

        /// <summary>
        /// A <see cref="GameObject"/> representing the “Load Model from Directory” button in the UI.
        /// Visible or hidden depending on platform capabilities (e.g., file system access).
        /// </summary>
        [SerializeField]
        private GameObject _loadModelFromDirectory;

        /// <summary>
        /// Toggle controlling whether to load point clouds from certain model formats.
        /// </summary>
        [SerializeField]
        private Toggle _loadPointClouds;

        /// <summary>
        /// The primary scene <see cref="Camera"/> used when not viewing through imported cameras.
        /// </summary>
        [SerializeField]
        private Camera _mainCamera;

        /// <summary>
        /// Displays memory usage information in the UI, including total and peak usage.
        /// </summary>
        [SerializeField]
        private Text _memoryUsageText;

        /// <summary>
        /// An <see cref="InputField"/> for entering a remote model URL to download and load.
        /// </summary>
        [SerializeField]
        private InputField _modelUrl;

        /// <summary>
        /// Dialog <see cref="GameObject"/> wrapping the input for a remote model URL.
        /// </summary>
        [SerializeField]
        private GameObject _modelUrlDialog;

        /// <summary>
        /// A <see cref="GameObject"/> wrapping the point size slider (for point cloud rendering).
        /// Active only if <see cref="_loadPointClouds"/> is toggled on.
        /// </summary>
        [SerializeField]
        private GameObject _pointCloudSizeWrapper;

        /// <summary>
        /// UI text or label that indicates “Point Size”.
        /// </summary>
        [SerializeField]
        private GameObject _pointSizeLabel;

        /// <summary>
        /// Slider controlling the size of points in point cloud rendering.
        /// </summary>
        [SerializeField]
        private Slider _pointSizeSlider;

        /// <summary>
        /// The main <see cref="ReflectionProbe"/> used to update environment reflections after the skybox changes.
        /// </summary>
        [SerializeField]
        private ReflectionProbe _reflectionProbe;

        /// <summary>
        /// A hidden “Show Albedo” shader used for debug rendering.
        /// </summary>
        private Shader _showAlbedoShader;

        /// <summary>
        /// A hidden “Show Emission” shader used for debug rendering.
        /// </summary>
        private Shader _showEmissionShader;

        /// <summary>
        /// A hidden “Show Metallic” shader used for debug rendering.
        /// </summary>
        private Shader _showMetallicShader;

        /// <summary>
        /// A hidden “Show Normals” shader used for debug rendering.
        /// </summary>
        private Shader _showNormalsShader;

        /// <summary>
        /// A hidden “Show Occlusion” shader used for debug rendering.
        /// </summary>
        private Shader _showOcclusionShader;

        /// <summary>
        /// A runtime behavior for displaying the skeleton or rig of the loaded model
        /// (if relevant) for debugging or educational purposes.
        /// </summary>
        private ShowSkeleton _showSkeleton;

        /// <summary>
        /// A hidden “Show Smoothness” shader used for debug rendering.
        /// </summary>
        private Shader _showSmoothShader;

        /// <summary>
        /// A hidden “Show Vertex Colors” shader used for debug rendering.
        /// </summary>
        private Shader _showVertexColorsShader;

        /// <summary>
        /// Slider controlling the HDR skybox’s <c>_Exposure</c> property, allowing brightness tweaks.
        /// </summary>
        [SerializeField]
        private Slider _skyboxExposureSlider;

        /// <summary>
        /// The active (instantiated) material used for the HDR skybox.
        /// </summary>
        private Material _skyboxMaterial;

        /// <summary>
        /// A material preset used to instantiate the final skybox material with <see cref="_skyboxTexture"/>.
        /// </summary>
        [SerializeField]
        private Material _skyboxMaterialPreset;

        /// <summary>
        /// The <see cref="Renderer"/> component on the <see cref="Skybox"/> <see cref="GameObject"/>.
        /// </summary>
        [SerializeField]
        private Renderer _skyboxRenderer;

        /// <summary>
        /// The loaded HDR texture displayed on the skybox. Assigned in <see cref="DoLoadSkybox"/>.
        /// </summary>
        private Texture2D _skyboxTexture;

        /// <summary>
        /// Tracks how long a model load operation takes from user selection until fully loaded.
        /// </summary>
        private Stopwatch _stopwatch;

        /// <summary>
        /// UI text displaying memory usage per object type (meshes, textures, materials, etc.).
        /// </summary>
        [SerializeField]
        private Text _usedMemoryText;

        /// <summary>
        /// A singleton reference to the active <see cref="AssetViewer"/> in the scene,
        /// established in <see cref="Start"/>.
        /// </summary>
        public static AssetViewer Instance { get; private set; }

        /// <summary>
        /// The root <see cref="GameObject"/> of the currently loaded model.
        /// If a new model is loaded, the previous root is destroyed and replaced.
        /// </summary>
        public GameObject RootGameObject { get; set; }

        /// <summary>
        /// Returns true if any model animation is currently playing via <see cref="_animation"/>.
        /// </summary>
        private bool AnimationIsPlaying => _animation != null && _animation.isPlaying;

        /// <summary>
        /// Retrieves the current <see cref="AnimationState"/> corresponding to the selection
        /// in <see cref="PlaybackAnimation"/>, if any.
        /// </summary>
        private AnimationState CurrentAnimationState
        {
            get
            {
                if (_animation != null)
                {
                    return _animation[PlaybackAnimation.options[PlaybackAnimation.value].text];
                }
                return null;
            }
        }

        /// <summary>
        /// Switches between cameras (if any) embedded in the loaded model and the default user camera.
        /// Triggered by the “Camera” dropdown <see cref="_camerasDropdown"/>.
        /// </summary>
        /// <param name="index">The dropdown index indicating which camera to enable.</param>
        public void CameraChanged(int index)
        {
            for (var i = 0; i < _cameras.Count; i++)
            {
                _cameras[i].enabled = false;
            }
            if (index == 0)
            {
                _mainCamera.enabled = true;
            }
            else
            {
                _cameras[index - 1].enabled = true;
            }
        }

        /// <summary>
        /// Removes the existing HDR skybox texture, setting the skybox to a default blank state.
        /// </summary>
        public void ClearSkybox()
        {
            if (_skyboxMaterial == null)
            {
                _skyboxMaterial = Instantiate(_skyboxMaterialPreset);
            }
            _skyboxMaterial.mainTexture = null;
            _skyboxExposureSlider.value = 1f;
            OnSkyboxExposureChanged(1f);
        }

        /// <summary>
        /// Hides the help or usage instructions panel.
        /// </summary>
        public void HideHelp()
        {
            _helpWrapper.SetActive(false);
        }

        /// <summary>
        /// Hides the model URL dialog and clears the entered URL text.
        /// </summary>
        public void HideModelUrlDialog()
        {
            _modelUrlDialog.SetActive(false);
            _modelUrl.text = null;
        }

        /// <summary>
        /// Indicates whether a model (or skybox) is currently being loaded.
        /// </summary>
        /// <returns>True if the system is loading; otherwise, false.</returns>
        public bool IsLoading()
        {
            return _loading;
        }

        /// <summary>
        /// Displays a folder picker dialog to load a model from all files in the chosen directory.
        /// The <see cref="AssetLoaderOptions"/> are updated with user toggles before loading.
        /// </summary>
        public void LoadModelFromDirectory()
        {
            ConfigureAssetLoaderOptions();
            SetLoading(false);
            FilePickerAssetLoader = AssetLoaderFilePicker.Create();
            FilePickerAssetLoader.LoadModelFromDirectoryPickerAsync(
                "Select a Directory",
                OnLoad,
                OnMaterialsLoad,
                OnProgress,
                OnBeginLoadModel,
                OnError,
                gameObject,
                AssetLoaderOptions,
                true
            );
        }

        /// <summary>
        /// Configures the AssetLoaderOptions based on the UI settings.
        /// </summary>
        private void ConfigureAssetLoaderOptions()
        {
            AssetLoaderOptions.ImportCameras = _loadCamerasToggle.isOn;
            AssetLoaderOptions.ImportLights = _loadLightsToggle.isOn;
            AssetLoaderOptions.LoadPointClouds = _loadPointClouds.isOn;
            AssetLoaderOptions.LimitBoneWeights = false;
            AssetLoaderOptions.Timeout = 900; // Increase loading timeout for the viewer scene.
        }

        /// <summary>
        /// Displays a file picker dialog to select a single model file. 
        /// Updates <see cref="AssetLoaderOptions"/> based on UI toggles before loading.
        /// </summary>
        public void LoadModelFromFile()
        {
            ConfigureAssetLoaderOptions();
            SetLoading(false);
            FilePickerAssetLoader = AssetLoaderFilePicker.Create();
            FilePickerAssetLoader.LoadModelFromFilePickerAsync(
                "Select a File",
                OnLoad,
                OnMaterialsLoad,
                OnProgress,
                OnBeginLoadModel,
                OnError,
                gameObject,
                AssetLoaderOptions
            );
        }

        /// <summary>
        /// Loads a model from a remote URL, optionally handling zip archives if <paramref name="fileExtension"/> 
        /// indicates a “.zip”. Integrates with TriLib’s <see cref="AssetDownloader"/> for network downloads.
        /// </summary>
        /// <param name="request">A <see cref="UnityWebRequest"/> pointing to the model.</param>
        /// <param name="fileExtension">File extension (e.g., <c>fbx</c>, <c>zip</c>) for load logic.</param>
        /// <param name="wrapperGameObject">Optional parent for the loaded model’s root <see cref="GameObject"/>.</param>
        /// <param name="customData">Optional user data passed into TriLib load methods.</param>
        /// <param name="onMaterialsLoad">Optional callback after textures and materials finish loading.</param>
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
            var isZipFile = fileExtension == "zip" || fileExtension == ".zip";
            AssetDownloader.LoadModelFromUri(
                request,
                OnLoad,
                onMaterialsLoad ?? OnMaterialsLoad,
                OnProgress,
                OnError,
                wrapperGameObject,
                AssetLoaderOptions,
                customData,
                isZipFile ? null : fileExtension,
                isZipFile
            );
        }

        /// <summary>
        /// Loads a model from the URL the user entered into <see cref="_modelUrl"/>.
        /// Invokes <see cref="LoadModelFromURL"/> once the user commits the address.
        /// </summary>
        public void LoadModelFromURLWithDialogValues()
        {
            ConfigureAssetLoaderOptions();
            if (string.IsNullOrWhiteSpace(_modelUrl.text))
            {
                return;
            }
            var request = AssetDownloader.CreateWebRequest(_modelUrl.text.Trim());
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
        /// Opens a file picker dialog to select an HDR image for the skybox.
        /// </summary>
        public void LoadSkyboxFromFile()
        {
            SetLoading(false);
            var title = "Select a skybox image";
            var extensions = new ExtensionFilter[]
            {
                new ExtensionFilter("Radiance HDR Image (hdr)", "hdr")
            };
            StandaloneFileBrowser.OpenFilePanelAsync(title, null, extensions, true, OnSkyboxStreamSelected);
        }

        /// <summary>
        /// Called when the user either selects a file/directory/url or cancels the dialog.
        /// If a file was chosen, clears previous model data and starts tracking load time.
        /// </summary>
        /// <param name="hasFiles">
        /// True if the user selected a file/directory, otherwise false (canceled).
        /// </param>
        public void OnBeginLoadModel(bool hasFiles)
        {
            if (hasFiles)
            {
                Resources.UnloadUnusedAssets();
                if (RootGameObject != null)
                {
                    Destroy(RootGameObject);
                }
                SetLoading(true);
            }
            if (hasFiles)
            {
                if (Application.GetStackTraceLogType(LogType.Exception) != StackTraceLogType.None ||
                    Application.GetStackTraceLogType(LogType.Error) != StackTraceLogType.None)
                {
                    _errorPanel.SetActive(false);
                }
                _animations = null;
                _loadingTimeText.text = null;
                _stopwatch = new Stopwatch();
                _stopwatch.Start();
            }
        }

        /// <summary>
        /// Invoked when the user changes an option in the debug dropdown, switching between
        /// various debug shaders (e.g., normals, albedo) or showing/hiding the skeleton.
        /// </summary>
        /// <param name="value">The zero-based index of the debug option selected.</param>
        public void OnDebugOptionsDropdownChanged(int value)
        {
            switch (value)
            {
                default:
                    if (_showSkeleton != null)
                    {
                        _showSkeleton.enabled = (value == 1);
                    }
                    _mainCamera.ResetReplacementShader();
                    _mainCamera.renderingPath = RenderingPath.UsePlayerSettings;
                    break;
                case 2:
                    _mainCamera.SetReplacementShader(_showAlbedoShader, null);
                    _mainCamera.renderingPath = RenderingPath.Forward;
                    break;
                case 3:
                    _mainCamera.SetReplacementShader(_showEmissionShader, null);
                    _mainCamera.renderingPath = RenderingPath.Forward;
                    break;
                case 4:
                    _mainCamera.SetReplacementShader(_showOcclusionShader, null);
                    _mainCamera.renderingPath = RenderingPath.Forward;
                    break;
                case 5:
                    _mainCamera.SetReplacementShader(_showNormalsShader, null);
                    _mainCamera.renderingPath = RenderingPath.Forward;
                    break;
                case 6:
                    _mainCamera.SetReplacementShader(_showMetallicShader, null);
                    _mainCamera.renderingPath = RenderingPath.Forward;
                    break;
                case 7:
                    _mainCamera.SetReplacementShader(_showSmoothShader, null);
                    _mainCamera.renderingPath = RenderingPath.Forward;
                    break;
                case 8:
                    _mainCamera.SetReplacementShader(_showVertexColorsShader, null);
                    _mainCamera.renderingPath = RenderingPath.Forward;
                    break;
            }
        }

        /// <summary>
        /// Called if any error occurs during model loading. Displays the error message in the UI,
        /// logs it to the console, stops animation playback, and disables the loading state.
        /// </summary>
        /// <param name="contextualizedError">
        /// Provides context on the error, including the exception and potentially related data.
        /// </param>
        public void OnError(IContextualizedError contextualizedError)
        {
            if (Application.GetStackTraceLogType(LogType.Exception) != StackTraceLogType.None ||
                Application.GetStackTraceLogType(LogType.Error) != StackTraceLogType.None)
            {
                _errorPanelText.text = contextualizedError.ToString();
                _errorPanel.SetActive(true);
            }
            UnityEngine.Debug.LogError(contextualizedError);
            RootGameObject = null;
            SetLoading(false);
            StopAnimation();
        }

        /// <summary>
        /// Toggles “Fast Mode” on or off. When on, certain advanced settings (e.g., advanced geometry processing)
        /// are disabled to speed up loading, at the cost of skipping some features.
        /// </summary>
        /// <param name="value">True if fast load mode is toggled on, false otherwise.</param>
        public void OnFastLoadToggleChanged(bool value)
        {
            if (!_hasAssetLoader)
            {
                AssetLoaderOptions = AssetLoader.CreateDefaultLoaderOptions(false, true);
            }
            if (value)
            {
                AssetLoader.LoadFastestSettings(ref AssetLoaderOptions);
            }
        }

        /// <summary>
        /// Called once the model’s meshes and hierarchy are loaded (but before textures/materials).
        /// Resets certain viewer state, configures cameras, handles animations, etc.
        /// </summary>
        /// <param name="assetLoaderContext">
        /// Context containing the newly loaded root <see cref="GameObject"/> and other references.
        /// </param>
        public void OnLoad(AssetLoaderContext assetLoaderContext)
        {
            PeakMemory = 0;
#if TRILIB_SHOW_MEMORY_USAGE
            PeakManagedMemory = 0;
#endif
            ResetModelScale();
            _camerasDropdown.options.Clear();
            PlaybackAnimation.options.Clear();
            _cameras = null;
            _animation = null;
            _mainCamera.enabled = true;

            if (assetLoaderContext.RootGameObject != null)
            {
                if (assetLoaderContext.Options.ImportCameras)
                {
                    _cameras = assetLoaderContext.RootGameObject.GetComponentsInChildren<Camera>();
                    if (_cameras.Count > 0)
                    {
                        _camerasDropdown.gameObject.SetActive(true);
                        _camerasDropdown.options.Add(new Dropdown.OptionData("User Camera"));
                        for (var i = 0; i < _cameras.Count; i++)
                        {
                            var camera = _cameras[i];
                            camera.enabled = false;
                            _camerasDropdown.options.Add(new Dropdown.OptionData(camera.name));
                        }
                        _camerasDropdown.captionText.text = _cameras[0].name;
                    }
                    else
                    {
                        _cameras = null;
                    }
                }
                _animation = assetLoaderContext.RootGameObject.GetComponent<Animation>();
                if (_animation != null)
                {
                    _animations = _animation.GetAllAnimationClips();
                    if (_animations.Count > 0)
                    {
                        PlaybackAnimation.interactable = true;
                        for (var i = 0; i < _animations.Count; i++)
                        {
                            var animationClip = _animations[i];
                            PlaybackAnimation.options.Add(new Dropdown.OptionData(animationClip.name));
                        }
                        PlaybackAnimation.captionText.text = _animations[0].name;
                    }
                    else
                    {
                        _animation = null;
                    }
                }
                _camerasDropdown.value = 0;
                PlaybackAnimation.value = 0;
                StopAnimation();
                RootGameObject = assetLoaderContext.RootGameObject;
            }

            if (_cameras == null)
            {
                _camerasDropdown.gameObject.SetActive(false);
            }
            if (_animation == null)
            {
                PlaybackAnimation.interactable = false;
                PlaybackAnimation.captionText.text = "No Animations";
            }
        }

        /// <summary>
        /// Invoked once textures and materials finish loading, indicating the model is fully ready.
        /// Disables the loading UI, logs load time, and handles post-load actions (e.g., skeleton display).
        /// </summary>
        /// <param name="assetLoaderContext">
        /// Provides references to the fully loaded model, including <c>RootGameObject</c> 
        /// and any instantiated components.
        /// </param>
        public void OnMaterialsLoad(AssetLoaderContext assetLoaderContext)
        {
            SetLoading(false);
            _stopwatch.Stop();
            _loadingTimeText.text = $"Loaded in: {_stopwatch.Elapsed.Minutes:00}:{_stopwatch.Elapsed.Seconds:00}";

            if (assetLoaderContext.RootGameObject != null)
            {
                _showSkeleton = assetLoaderContext.RootGameObject.AddComponent<ShowSkeleton>();
                _showSkeleton.Setup(assetLoaderContext, this);
                assetLoaderContext.Allocations.Add(_showSkeleton);

                if (assetLoaderContext.Options.LoadPointClouds && assetLoaderContext.RootGameObject != null)
                {
                    HandlePointClouds(assetLoaderContext);
                }

                var meshAllocation = 0;
                var textureAllocation = 0;
                var materialAllocation = 0;
                var animationClipAllocation = 0;
                var miscAllocation = 0;

                foreach (var allocation in assetLoaderContext.Allocations)
                {
                    var runtimeMemorySize = Profiler.GetRuntimeMemorySize(allocation);
                    if (allocation is Mesh)
                    {
                        meshAllocation += runtimeMemorySize;
                    }
                    else if (allocation is Texture)
                    {
                        textureAllocation += runtimeMemorySize;
                    }
                    else if (allocation is Material)
                    {
                        materialAllocation += runtimeMemorySize;
                    }
                    else if (allocation is AnimationClip)
                    {
                        animationClipAllocation += runtimeMemorySize;
                    }
                    else
                    {
                        miscAllocation += runtimeMemorySize;
                    }
                }

                _usedMemoryText.text = UnityEngine.Debug.isDebugBuild
                    ? $"Used Memory:\nMeshes: {ProcessUtils.SizeSuffix(meshAllocation)}\nTextures: {ProcessUtils.SizeSuffix(textureAllocation)}\nMaterials: {ProcessUtils.SizeSuffix(materialAllocation)}\nAnimation Clips: {ProcessUtils.SizeSuffix(animationClipAllocation)}\nMisc.: {ProcessUtils.SizeSuffix(miscAllocation)}"
                    : string.Empty;
            }
            else
            {
                _usedMemoryText.text = string.Empty;
            }

            OnDebugOptionsDropdownChanged(_debugOptionsDropdown.value);
            OnModelTransformChanged();
        }

        /// <summary>
        /// Repositions the camera relative to the loaded model bounds whenever the model’s transform changes.
        /// Adjusts the skybox scale and sets <see cref="CameraDistance"/> accordingly.
        /// </summary>
        public void OnModelTransformChanged()
        {
            if (RootGameObject != null && _mainCamera.enabled)
            {
                var bounds = RootGameObject.CalculateBounds();
                var boundsMagnitude = bounds.size.magnitude;
                _mainCamera.FitToBounds(bounds, CameraDistanceRatio);
                CameraPivot = bounds.center;
                CameraDistance = Vector3.Distance(_mainCamera.transform.position, CameraPivot);
                CameraAngle = Vector2.zero;
                Skybox.transform.localScale = boundsMagnitude * SkyboxScale * Vector3.one;
                InputMultiplier = boundsMagnitude * InputMultiplierRatio;
            }
        }

        /// <summary>
        /// Toggles point cloud loading. Also toggles visibility of the point size UI slider/label.
        /// </summary>
        /// <param name="value">True if point clouds should be loaded, false otherwise.</param>
        public void OnPointCloudToggleChanged(bool value)
        {
            _pointCloudSizeWrapper.SetActive(value);
            _pointSizeLabel.SetActive(value);
        }

        /// <summary>
        /// Called when the point size slider value changes, updating the size
        /// of any <see cref="PointRenderer"/> components rendering point clouds.
        /// </summary>
        /// <param name="value">A float representing the new point size.</param>
        public void OnPointSizeSliderChanged(float value)
        {
            var pointRenderers = FindObjectsOfType<PointRenderer>();
            foreach (var pointRenderer in pointRenderers)
            {
                pointRenderer.PointSize = value;
            }
        }

        /// <summary>
        /// Reports loading progress for the current model (0.0 to 1.0). 
        /// Updates the loading bar’s width accordingly.
        /// </summary>
        /// <param name="assetLoaderContext">Provides info about the current load process.</param>
        /// <param name="value">A float from 0.0 to 1.0 representing the loading progress.</param>
        public void OnProgress(AssetLoaderContext assetLoaderContext, float value)
        {
            _loadingBar.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Screen.width * value);
        }

        /// <summary>
        /// Adjusts the skybox exposure level based on <paramref name="exposure"/>, 
        /// updating the reflection probe to reflect the new brightness.
        /// </summary>
        /// <param name="exposure">A float representing the skybox exposure multiplier.</param>
        public void OnSkyboxExposureChanged(float exposure)
        {
            _skyboxMaterial.SetFloat("_Exposure", exposure);
            _skyboxRenderer.material = _skyboxMaterial;
            RenderSettings.skybox = _skyboxMaterial;
            DynamicGI.UpdateEnvironment();
            _reflectionProbe.RenderProbe();
        }

        /// <summary>
        /// Begins playback of the currently selected animation (if any).
        /// Uses the name from <see cref="PlaybackAnimation"/> to identify the clip.
        /// </summary>
        public void PlayAnimation()
        {
            if (_animation == null)
            {
                return;
            }
            _animation.Play(PlaybackAnimation.options[PlaybackAnimation.value].text, PlayMode.StopAll);
        }

        /// <summary>
        /// When the animation selection changes in <see cref="PlaybackAnimation"/>,
        /// stops the currently playing animation to allow switching to the new one.
        /// </summary>
        /// <param name="index">The zero-based index for the selected animation option.</param>
        public void PlaybackAnimationChanged(int index)
        {
            StopAnimation();
        }

        /// <summary>
        /// Invoked when the <see cref="PlaybackSlider"/> value changes.
        /// If no animation is playing, updates the <see cref="RootGameObject"/> to display 
        /// the current frame of the selected animation at that normalized time.
        /// </summary>
        /// <param name="value">Normalized time [0..1] for the animation.</param>
        public void PlaybackSliderChanged(float value)
        {
            if (!AnimationIsPlaying)
            {
                var animationState = CurrentAnimationState;
                if (animationState != null)
                {
                    SampleAnimationAt(value);
                }
            }
        }

        /// <summary>
        /// Processes input each frame, such as rotating, zooming, or panning the camera,
        /// but only if the primary <see cref="_mainCamera"/> is active.
        /// </summary>
        public void ProcessInput()
        {
            if (!_mainCamera.enabled)
            {
                return;
            }
            ProcessInputInternal(_mainCamera.transform);
        }

        /// <summary>
        /// Resets the loaded model’s scale to <c>Vector3.one</c> if a model is present.
        /// Called after loading or reloading a model to clear any previously applied scaling.
        /// </summary>
        public void ResetModelScale()
        {
            if (RootGameObject != null)
            {
                RootGameObject.transform.localScale = Vector3.one;
            }
        }

        /// <summary>
        /// Adjusts the camera to fit a specific bounding volume (e.g., after user modifications),
        /// updating <see cref="CameraDistance"/>, <see cref="CameraPivot"/>, and skybox scaling.
        /// </summary>
        /// <param name="bounds">The bounding volume to fit the camera to.</param>
        public void SetCustomBounds(Bounds bounds)
        {
            _mainCamera.FitToBounds(bounds, CameraDistanceRatio);
            CameraDistance = _mainCamera.transform.position.magnitude;
            CameraPivot = bounds.center;
            Skybox.transform.localScale = bounds.size.magnitude * SkyboxScale * Vector3.one;
            InputMultiplier = bounds.size.magnitude * InputMultiplierRatio;
            CameraAngle = Vector2.zero;
        }

        /// <summary>
        /// Toggles loading state UI, disabling or enabling <see cref="Selectable"/> elements,
        /// and showing or hiding the <see cref="_loadingWrapper"/> and <see cref="_loadingBar"/>.
        /// </summary>
        /// <param name="value">If true, shows loading UI; false hides loading UI.</param>
        public void SetLoading(bool value)
        {
#if UNITY_2023_3_OR_NEWER
            var selectables = FindObjectsByType<Selectable>(FindObjectsSortMode.None);
#else
            var selectables = FindObjectsOfType<Selectable>();
#endif
            for (var i = 0; i < selectables.Length; i++)
            {
                var button = selectables[i];
                button.interactable = !value;
            }
            _loadingWrapper.gameObject.SetActive(value);
            _loadingBar.gameObject.SetActive(value);
            _loading = value;
        }

        /// <summary>
        /// Displays the help panel.
        /// </summary>
        public void ShowHelp()
        {
            _helpWrapper.SetActive(true);
        }

        /// <summary>
        /// Shows the input field and dialog for loading a model from a remote URL.
        /// </summary>
        public void ShowModelUrlDialog()
        {
            _modelUrlDialog.SetActive(true);
            _modelUrl.Select();
            _modelUrl.ActivateInputField();
        }

        /// <summary>
        /// Initializes this <see cref="AssetViewer"/>, setting up references, toggles,
        /// and default skybox conditions. Called automatically on scene load.
        /// </summary>
        public void Start()
        {
            Dispatcher.CheckInstance();
            PasteManager.CheckInstance();
            Instance = this;

#if UNITY_WEBGL && !UNITY_EDITOR
            _loadModelFromDirectory.SetActive(true);
#endif

            if (SystemInfo.deviceType == DeviceType.Handheld)
            {
                CanvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            }

            _hasAssetLoader = AssetLoaderOptions != null;
            _showNormalsShader = Shader.Find("Hidden/ShowNormals");
            _showMetallicShader = Shader.Find("Hidden/ShowMetallic");
            _showSmoothShader = Shader.Find("Hidden/ShowSmooth");
            _showAlbedoShader = Shader.Find("Hidden/ShowAlbedo");
            _showOcclusionShader = Shader.Find("Hidden/ShowOcclusion");
            _showEmissionShader = Shader.Find("Hidden/ShowEmission");
            _showVertexColorsShader = Shader.Find("Hidden/ShowVertexColors");

            OnFastLoadToggleChanged(false);
            ClearSkybox();
            InvokeRepeating("ShowMemoryUsage", 0f, 1f);
        }

        /// <summary>
        /// Stops playback of the currently selected animation and resets its time to zero.
        /// </summary>
        public void StopAnimation()
        {
            if (_animation == null)
            {
                return;
            }
            PlaybackSlider.value = 0f;
            _animation.Stop();
            SampleAnimationAt(0f);
        }

        /// <summary>
        /// Updates camera angles based on mouse movement (pitch and yaw),
        /// applying <see cref="InputMultiplierRatio"/> and clamping pitch within <see cref="MaxPitch"/>.
        /// </summary>
        public void UpdateCamera()
        {
            CameraAngle.x = Mathf.Repeat(CameraAngle.x + GetAxis("Mouse X"), 360f);
            CameraAngle.y = Mathf.Clamp(CameraAngle.y + GetAxis("Mouse Y"), -MaxPitch, MaxPitch);
        }

        /// <summary>
        /// Loads an HDR skybox texture from a <see cref="Stream"/>, then applies it to 
        /// the <see cref="_skyboxMaterial"/>.
        /// </summary>
        /// <param name="stream">The HDR image data stream.</param>
        /// <returns>An <see cref="IEnumerator"/> for the coroutine.</returns>
        private IEnumerator DoLoadSkybox(Stream stream)
        {
            // Double frame waiting hack to ensure textures are fully ready
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            if (_skyboxTexture != null)
            {
                Destroy(_skyboxTexture);
            }
            ClearSkybox();
            _skyboxTexture = HDRLoader.HDRLoader.Load(stream, out var gamma, out var exposure);
            _skyboxMaterial.mainTexture = _skyboxTexture;
            _skyboxExposureSlider.value = 1f;
            OnSkyboxExposureChanged(exposure);
            stream.Close();
            SetLoading(false);
        }

        /// <summary>
        /// If the model includes point clouds, adds a <see cref="PointRenderer"/> to each relevant object.
        /// </summary>
        /// <param name="assetLoaderContext">Contains references to each object and allocated resources.</param>
        private void HandlePointClouds(AssetLoaderContext assetLoaderContext)
        {
            foreach (var gameObject in assetLoaderContext.GameObjects.Values)
            {
                if (gameObject.TryGetComponent<MeshFilter>(out var meshFilter))
                {
                    var pointRenderer = gameObject.AddComponent<PointRenderer>();
                    pointRenderer.PointSize = _pointSizeSlider.value;
                    pointRenderer.Initialize(assetLoaderContext.Options.UseSharedMeshes ? meshFilter.sharedMesh : meshFilter.mesh);
                    assetLoaderContext.Allocations.Add(pointRenderer.Mesh);
                    assetLoaderContext.Allocations.Add(pointRenderer.Material);
                }
            }
        }

        /// <summary>
        /// Starts the coroutine to load an HDR skybox from the provided <see cref="Stream"/>.
        /// </summary>
        /// <param name="stream">The HDR texture data stream.</param>
        private void LoadSkybox(Stream stream)
        {
            SetLoading(true);
            StartCoroutine(DoLoadSkybox(stream));
        }

        /// <summary>
        /// Handler for the file picker callback when the user selects an HDR image for the skybox.
        /// </summary>
        /// <param name="files">A list of selected items with streams.</param>
        private void OnSkyboxStreamSelected(IList<ItemWithStream> files)
        {
            if (files != null && files.Count > 0 && files[0].HasData)
            {
                Utils.Dispatcher.InvokeAsyncUnchecked(LoadSkybox, files[0].OpenStream());
            }
            else
            {
                Utils.Dispatcher.InvokeAsync(ClearSkybox);
            }
        }

        /// <summary>
        /// Central logic for processing camera/light movements based on user input.
        /// Pans, rotates, or zooms, and repositions the directional <see cref="_light"/>.
        /// </summary>
        /// <param name="cameraTransform">The transform of the <see cref="_mainCamera"/> or another active camera.</param>
        private void ProcessInputInternal(Transform cameraTransform)
        {
            if (!EventSystem.current.IsPointerOverGameObject())
            {
                // Rotate camera or light on left mouse drag
                if (GetMouseButton(0))
                {
                    if (GetKey(KeyCode.LeftAlt) || GetKey(KeyCode.RightAlt))
                    {
                        _lightAngle.x = Mathf.Repeat(_lightAngle.x + GetAxis("Mouse X"), 360f);
                        _lightAngle.y = Mathf.Clamp(_lightAngle.y + GetAxis("Mouse Y"), -MaxPitch, MaxPitch);
                    }
                    else
                    {
                        UpdateCamera();
                    }
                }
                // Pan camera on middle mouse drag
                if (GetMouseButton(2))
                {
                    CameraPivot -= cameraTransform.up * GetAxis("Mouse Y") * InputMultiplier
                                   + cameraTransform.right * GetAxis("Mouse X") * InputMultiplier;
                }
                // Zoom in/out with mouse scroll
                CameraDistance = Mathf.Min(
                    CameraDistance - GetMouseScrollDelta().y * InputMultiplier,
                    InputMultiplier * (1f / InputMultiplierRatio) * MaxCameraDistanceRatio
                );
                if (CameraDistance < 0f)
                {
                    CameraPivot += cameraTransform.forward * -CameraDistance;
                    CameraDistance = 0f;
                }
                Skybox.transform.position = CameraPivot;
                cameraTransform.position = CameraPivot
                    + Quaternion.AngleAxis(CameraAngle.x, Vector3.up)
                    * Quaternion.AngleAxis(CameraAngle.y, Vector3.right)
                    * new Vector3(0f, 0f, Mathf.Max(MinCameraDistance, CameraDistance));
                cameraTransform.LookAt(CameraPivot);

                // Position the directional light based on _lightAngle
                _light.transform.position = CameraPivot
                    + Quaternion.AngleAxis(_lightAngle.x, Vector3.up)
                    * Quaternion.AngleAxis(_lightAngle.y, Vector3.right)
                    * Vector3.forward;
                _light.transform.LookAt(CameraPivot);
            }
        }

        /// <summary>
        /// Samples the currently selected animation clip at the specified normalized time.
        /// Updates the <see cref="RootGameObject"/> to the appropriate frame.
        /// </summary>
        /// <param name="value">A float [0..1] for normalized animation time.</param>
        private void SampleAnimationAt(float value)
        {
            if (_animation == null || RootGameObject == null)
            {
                return;
            }
            var animationClip = _animation.GetClip(PlaybackAnimation.options[PlaybackAnimation.value].text);
            animationClip.SampleAnimation(RootGameObject, animationClip.length * value);
        }

        /// <summary>
        /// Periodically updates memory usage stats, displayed in the UI.
        /// Called via <see cref="InvokeRepeating"/> in <see cref="Start"/>.
        /// </summary>
        private void ShowMemoryUsage()
        {
#if TRILIB_SHOW_MEMORY_USAGE
            var memory = RuntimeProcessUtils.GetProcessMemory();
            var managedMemory = GC.GetTotalMemory(false);
            PeakMemory = Math.Max(memory, PeakMemory);
            PeakManagedMemory = Math.Max(managedMemory, PeakManagedMemory);
            _memoryUsageText.text = $"(Total: {ProcessUtils.SizeSuffix(memory)} Peak: {ProcessUtils.SizeSuffix(PeakMemory)}) (Managed: {ProcessUtils.SizeSuffix(managedMemory)} Peak: {ProcessUtils.SizeSuffix(PeakManagedMemory)})";
#else
            var memory = GC.GetTotalMemory(false);
            PeakMemory = Math.Max(memory, PeakMemory);
            _memoryUsageText.text = $"Total: {ProcessUtils.SizeSuffix(memory)} Peak: {ProcessUtils.SizeSuffix(PeakMemory)}";
#endif
        }

        /// <summary>
        /// Main update loop for this viewer, invoked by Unity each frame. 
        /// Processes user input for panning/zooming/rotating the camera, 
        /// and updates the on-screen animation playback HUD.
        /// </summary>
        private void Update()
        {
            ProcessInput();
            UpdateHUD();
        }

        /// <summary>
        /// Updates animation HUD elements such as the <see cref="PlaybackTime"/>,
        /// <see cref="PlaybackSlider"/>, and toggles the <see cref="Play"/> or <see cref="Stop"/> 
        /// button visibility based on whether an animation is currently playing.
        /// </summary>
        private void UpdateHUD()
        {
            var animationState = CurrentAnimationState;
            var time = animationState == null ? 0f : PlaybackSlider.value * animationState.length % animationState.length;
            var seconds = time % 60f;
            var milliseconds = time * 100f % 100f;
            PlaybackTime.text = $"{seconds:00}:{milliseconds:00}";

            var normalizedTime = animationState == null ? 0f : animationState.normalizedTime % 1f;
            if (AnimationIsPlaying)
            {
                PlaybackSlider.value = float.IsNaN(normalizedTime) ? 0f : normalizedTime;
            }

            var animationIsPlaying = AnimationIsPlaying;
            if (_animation != null)
            {
                Play.gameObject.SetActive(!animationIsPlaying);
                Stop.gameObject.SetActive(animationIsPlaying);
            }
            else
            {
                Play.gameObject.SetActive(true);
                Stop.gameObject.SetActive(false);
                PlaybackSlider.value = 0f;
            }
        }
    }
}
