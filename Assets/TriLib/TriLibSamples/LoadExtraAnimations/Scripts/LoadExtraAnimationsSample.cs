#pragma warning disable 649
using System.Collections.Generic;
using TriLibCore.Extensions;
using TriLibCore.Utils;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TriLibCore.Samples
{
    /// <summary>
    /// Demonstrates how to load a base 3D model, then load and merge extra animation clips
    /// from multiple animation-only models in the same folder. After loading, this sample
    /// dynamically creates UI buttons that allow you to play the newly added animations
    /// on the base model.
    /// </summary>
    public class LoadExtraAnimationsSample : MonoBehaviour
    {
        /// <summary>
        /// Gets the path to the base <c>BuddyBase.fbx</c> file. Uses an editor-specific path in 
        /// Unity Editor builds, and a relative path in runtime builds.
        /// </summary>
        private string BaseModelPath
        {
            get
            {
#if UNITY_EDITOR
                return $"{Application.dataPath}/TriLib/TriLibSamples/LoadExtraAnimations/Models/BuddyBase.fbx";
#else
                return "Models/BuddyBase.fbx";
#endif
            }
        }

        /// <summary>
        /// A <see cref="Button"/> prefab (or template) that is duplicated each time a new
        /// animation is successfully loaded. Each duplicated button plays the associated animation.
        /// </summary>
        [SerializeField]
        private Button _playAnimationTemplate;

        /// <summary>
        /// The <see cref="Animation"/> component on the loaded base model,
        /// used to host and play additional animation clips.
        /// </summary>
        private Animation _baseAnimation;

        /// <summary>
        /// A list of <see cref="AssetLoaderContext"/> instances for animations loaded before
        /// the base model. These stored animations are processed after the base model loads,
        /// ensuring that we have a valid <see cref="_baseAnimation"/> to attach them to.
        /// </summary>
        private readonly IList<AssetLoaderContext> _loadedAnimations = new List<AssetLoaderContext>();

        /// <summary>
        /// Caches the <see cref="AssetLoaderOptions"/> used to load both the base model
        /// and the additional animation models. Created on-demand if <c>null</c>.
        /// </summary>
        private AssetLoaderOptions _assetLoaderOptions;

        /// <summary>
        /// Called by Unity once at scene startup. Loads the base model, then loads additional
        /// animations from <c>BuddyIdle.fbx</c>, <c>BuddyWalk.fbx</c>, and <c>BuddyJump.fbx</c>.
        /// These animation files are expected to reside in the same folder as the base model.
        /// </summary>
        private void Start()
        {
            LoadBaseModel();
            LoadAnimation("BuddyIdle.fbx");
            LoadAnimation("BuddyWalk.fbx");
            LoadAnimation("BuddyJump.fbx");
        }

        /// <summary>
        /// Loads an animation model (with no geometry, textures, or materials) from the same
        /// directory as the base model. Once loaded, <see cref="OnAnimationModelLoad"/> is called
        /// to transfer its animation clips onto the base model.
        /// </summary>
        /// <param name="modelFilename">
        /// The file name (e.g., <c>BuddyIdle.fbx</c>) containing the animation clips to be merged.
        /// </param>
        private void LoadAnimation(string modelFilename)
        {
            var modelsDirectory = FileUtils.GetFileDirectory(BaseModelPath);
            var modelPath = FileUtils.SanitizePath($"{modelsDirectory}/{modelFilename}");

            // If no loader options exist, create minimal defaults to import only animations
            if (_assetLoaderOptions == null)
            {
                _assetLoaderOptions = AssetLoader.CreateDefaultLoaderOptions(false, true);
                _assetLoaderOptions.ImportMeshes = false;
                _assetLoaderOptions.ImportTextures = false;
                _assetLoaderOptions.ImportMaterials = false;
            }

            AssetLoader.LoadModelFromFile(
                path: modelPath,
                onLoad: OnAnimationModelLoad,
                onMaterialsLoad: null,
                onProgress: OnProgress,
                onError: OnError,
                wrapperGameObject: gameObject,
                assetLoaderOptions: _assetLoaderOptions
            );
        }

        /// <summary>
        /// Invoked after an animation model finishes loading. Retrieves all animation clips 
        /// from the loaded <see cref="Animation"/> component. If the base model is already loaded,
        /// it merges the clips immediately; otherwise, it waits until the base model finishes.
        /// </summary>
        /// <param name="assetLoaderContext">
        /// The <see cref="AssetLoaderContext"/> providing access to the loaded animation object.
        /// </param>
        private void OnAnimationModelLoad(AssetLoaderContext assetLoaderContext)
        {
            Debug.Log($"Animation loaded: {FileUtils.GetShortFilename(assetLoaderContext.Filename)}");

            // If the base model is ready, attach the animation immediately
            if (_baseAnimation != null)
            {
                AddAnimation(assetLoaderContext);
            }
            else
            {
                // Otherwise, store the animation to be processed once the base model is loaded
                _loadedAnimations.Add(assetLoaderContext);
            }

            // Disable the loaded animation GameObject; we only need its clips, not its mesh or visuals
            assetLoaderContext.RootGameObject.SetActive(false);
        }

        /// <summary>
        /// Merges animation clips from the loaded animation <see cref="GameObject"/> 
        /// into the base model’s <see cref="_baseAnimation"/>. Spawns UI buttons to 
        /// trigger each newly added clip.
        /// </summary>
        /// <param name="loadedAnimationContext">
        /// The <see cref="AssetLoaderContext"/> containing the loaded animation clips.
        /// </param>
        private void AddAnimation(AssetLoaderContext loadedAnimationContext)
        {
            var rootGameObjectAnimation = loadedAnimationContext.RootGameObject.GetComponent<Animation>();
            if (rootGameObjectAnimation != null)
            {
                // Determine a unique prefix for each animation clip
                var shortFilename = FileUtils.GetShortFilename(loadedAnimationContext.Filename);

                // Gather all animation clips
                var newAnimationClips = rootGameObjectAnimation.GetAllAnimationClips();
                foreach (var newAnimationClip in newAnimationClips)
                {
                    // Create a unique clip name
                    var animationName = $"{shortFilename}_{newAnimationClip.name}";

                    // Add the clip to the base model's Animation component
                    _baseAnimation.AddClip(newAnimationClip, animationName);

                    // Instantiate a UI button to play this animation
                    var playAnimationButton = Instantiate(_playAnimationTemplate, _playAnimationTemplate.transform.parent);
                    var playAnimationButtonText = playAnimationButton.GetComponentInChildren<Text>();
                    playAnimationButtonText.text = shortFilename;
                    playAnimationButton.gameObject.SetActive(true);

                    // On button click, play the added animation using CrossFade
                    playAnimationButton.onClick.AddListener(delegate
                    {
                        _baseAnimation.CrossFade(animationName);
                    });
                }
            }

            // Cleanup: remove the temporary animation root from the scene
            Destroy(loadedAnimationContext.RootGameObject);
        }

        /// <summary>
        /// Loads the base model file (<c>BuddyBase.fbx</c>) which includes mesh, textures, materials,
        /// and an <see cref="Animation"/> component to host additional clips. 
        /// </summary>
        private void LoadBaseModel()
        {
            if (_assetLoaderOptions == null)
            {
                _assetLoaderOptions = AssetLoader.CreateDefaultLoaderOptions(false, true);
            }

            AssetLoader.LoadModelFromFile(
                path: BaseModelPath,
                onLoad: OnBaseModelLoad,
                onMaterialsLoad: OnBaseModelMaterialsLoad,
                onProgress: OnProgress,
                onError: OnError,
                wrapperGameObject: gameObject,
                assetLoaderOptions: _assetLoaderOptions
            );
        }

        /// <summary>
        /// Invoked if an error occurs at any point during model loading or processing,
        /// such as a missing file or incompatible format.
        /// </summary>
        /// <param name="obj">
        /// Provides context for the error, including the original exception object 
        /// and the context in which it was thrown.
        /// </param>
        private void OnError(IContextualizedError obj)
        {
            Debug.LogError($"An error occurred while loading your Model: {obj.GetInnerException()}");
        }

        /// <summary>
        /// Reports loading progress for either the base model or an animation file, 
        /// intended for UI progress bars or logs. Currently empty, but can be 
        /// implemented to provide visual feedback.
        /// </summary>
        /// <param name="assetLoaderContext">Context for the current loading operation.</param>
        /// <param name="progress">A float from 0.0 to 1.0 representing loading progress.</param>
        private void OnProgress(AssetLoaderContext assetLoaderContext, float progress)
        {
            // Optionally implement progress UI updates here
        }

        /// <summary>
        /// Invoked when the base model’s meshes, materials, and textures finish loading. 
        /// Initializes the base model's <see cref="Animation"/> component, then processes 
        /// any animations that were loaded prior to this model’s completion.
        /// </summary>
        /// <param name="assetLoaderContext">
        /// Provides access to the fully loaded base model, including its root <see cref="GameObject"/>.
        /// </param>
        private void OnBaseModelMaterialsLoad(AssetLoaderContext assetLoaderContext)
        {
            Debug.Log($"Base Model loaded: {FileUtils.GetShortFilename(assetLoaderContext.Filename)}");

            // Grab the Animation component from the loaded base model
            _baseAnimation = assetLoaderContext.RootGameObject.GetComponent<Animation>();

            // Attach any previously loaded animations now that the base model is ready
            for (var i = _loadedAnimations.Count - 1; i >= 0; i--)
            {
                AddAnimation(_loadedAnimations[i]);
                _loadedAnimations.RemoveAt(i);
            }
        }

        /// <summary>
        /// Invoked once the base model’s meshes and hierarchy are loaded, but 
        /// prior to texture and material completion. This sample does not use
        /// <see cref="OnBaseModelLoad"/> to modify the model, but it remains
        /// available should you need to perform early setup tasks.
        /// </summary>
        /// <param name="assetLoaderContext">
        /// The TriLib context containing references to the partially loaded base model.
        /// </param>
        private void OnBaseModelLoad(AssetLoaderContext assetLoaderContext)
        {
            // Intentionally left empty. You could position, scale, or add scripts here if needed.
        }
    }
}
