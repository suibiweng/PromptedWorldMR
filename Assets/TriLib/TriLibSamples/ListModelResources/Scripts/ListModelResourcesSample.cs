#pragma warning disable 649
using TriLibCore.General;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TriLibCore.Samples
{
    /// <summary>
    /// Demonstrates how to use TriLib to load a 3D model and list its associated 
    /// resources (model path, textures, and external data). This sample allows 
    /// the user to select a model via a file-picker and displays the loaded 
    /// resources in a UI text component.
    /// </summary>
    public class ListModelResourcesSample : MonoBehaviour
    {
        /// <summary>
        /// A reference to a UI <see cref="Text"/> component used to display 
        /// the paths of the loaded model, textures, and external resources.
        /// </summary>
        [SerializeField]
        private Text ResourcesText;

        /// <summary>
        /// A reference to the currently loaded model’s root <see cref="GameObject"/>, 
        /// which will be replaced when loading a new model.
        /// </summary>
        private GameObject _loadedGameObject;

        /// <summary>
        /// A cached <see cref="AssetLoaderOptions"/> instance used to configure 
        /// model loading behaviors. Created on demand if not already set.
        /// </summary>
        private AssetLoaderOptions _assetLoaderOptions;

        /// <summary>
        /// Invokes TriLib’s file-picker dialog for selecting a model. 
        /// If <see cref="_assetLoaderOptions"/> is null, a default set of 
        /// loader options is created first. Registers callback methods for 
        /// load progress, completion, and errors.
        /// </summary>
        /// <remarks>
        /// You can create <see cref="AssetLoaderOptions"/> via 
        /// <c>TriLib &gt; Create &gt; AssetLoaderOptions &gt; Pre-Built AssetLoaderOptions</c>
        /// in the Unity editor.
        /// </remarks>
        public void LoadModel()
        {
            // Ensure we have AssetLoaderOptions
            if (_assetLoaderOptions == null)
            {
                _assetLoaderOptions = AssetLoader.CreateDefaultLoaderOptions(false, true);
            }

            // Create the file picker and begin an asynchronous load
            var assetLoaderFilePicker = AssetLoaderFilePicker.Create();
            assetLoaderFilePicker.LoadModelFromFilePickerAsync(
                "Select a Model file",
                onLoad: OnLoad,
                onMaterialsLoad: OnMaterialsLoad,
                onProgress: OnProgress,
                onBeginLoad: OnBeginLoad,
                onError: OnError,
                wrapperGameObject: null,
                assetLoaderOptions: _assetLoaderOptions
            );
        }

        /// <summary>
        /// Called by Unity when this script instance is being loaded. 
        /// If <see cref="_assetLoaderOptions"/> is not assigned, creates a default set 
        /// of TriLib loader options. You can also load a model at startup if desired.
        /// </summary>
        /// <remarks>
        /// By default, this sample does not automatically load a model at startup. 
        /// The user can initiate loading via <see cref="LoadModel"/>.
        /// </remarks>
        private void Start()
        {
            if (_assetLoaderOptions == null)
            {
                _assetLoaderOptions = AssetLoader.CreateDefaultLoaderOptions(false, true);
            }
        }

        /// <summary>
        /// Invoked when the user confirms a file selection in the file-picker 
        /// (i.e., when model loading begins). Resets any previously loaded model 
        /// and updates the UI to indicate loading has started.
        /// </summary>
        /// <param name="filesSelected">True if a file was selected; otherwise, false.</param>
        private void OnBeginLoad(bool filesSelected)
        {
            if (filesSelected)
            {
                Debug.Log("User selected a Model.");

                // Destroy the previously loaded GameObject, if present.
                if (_loadedGameObject != null)
                {
                    Destroy(_loadedGameObject);
                }

                // Reset the resources text and clear the old model reference.
                ResourcesText.text = "Loading Model";
                _loadedGameObject = null;
            }
        }

        /// <summary>
        /// Called when an error occurs during model loading. 
        /// Logs a detailed message for troubleshooting.
        /// </summary>
        /// <param name="obj">
        /// An object implementing <see cref="IContextualizedError"/>, containing
        /// the original exception and additional context.
        /// </param>
        private void OnError(IContextualizedError obj)
        {
            Debug.LogError($"An error occurred while loading your Model: {obj.GetInnerException()}");
        }

        /// <summary>
        /// Invoked periodically to report loading progress. 
        /// You can use this to update a progress bar or similar UI element.
        /// </summary>
        /// <param name="assetLoaderContext">
        /// The TriLib context managing the current model load operation.
        /// </param>
        /// <param name="progress">A float representing progress from 0.0 to 1.0.</param>
        private void OnProgress(AssetLoaderContext assetLoaderContext, float progress)
        {
            Debug.Log($"Loading Model. Progress: {progress:P}");
        }

        /// <summary>
        /// Invoked once the model, including all textures and materials, 
        /// has fully loaded. Gathers and displays the loaded model’s path, 
        /// texture paths, and any external data paths in the <see cref="ResourcesText"/>.
        /// </summary>
        /// <remarks>
        /// The loaded root <see cref="GameObject"/> can be accessed via 
        /// <c>assetLoaderContext.RootGameObject</c> if you need to manipulate it further.
        /// </remarks>
        /// <param name="assetLoaderContext">
        /// The TriLib context containing references to loaded assets and paths.
        /// </param>
        private void OnMaterialsLoad(AssetLoaderContext assetLoaderContext)
        {
            Debug.Log("Materials loaded. Model fully loaded.");

            // Build a string containing all resources used by the model
            var text = string.Empty;

            // 1) Model File Path
            var modelPath = assetLoaderContext.Filename;
            if (!string.IsNullOrEmpty(modelPath))
            {
                text += $"Model: '{modelPath}'\n";
            }

            // 2) Texture Files
            foreach (var kvp in assetLoaderContext.LoadedCompoundTextures)
            {
                // Each key’s ResolvedFilename is the local or absolute path to the loaded texture
                var finalPath = kvp.Key.ResolvedFilename;
                if (!string.IsNullOrEmpty(finalPath))
                {
                    text += $"Texture: '{finalPath}'\n";
                }
            }

            // 3) External Data (e.g., references to other files used by the model)
            foreach (var kvp in assetLoaderContext.LoadedExternalData)
            {
                var finalPath = kvp.Value;
                if (!string.IsNullOrEmpty(finalPath))
                {
                    text += $"External Data: '{finalPath}'\n";
                }
            }

            // Display the compiled resource paths in the UI
            ResourcesText.text = text;
        }

        /// <summary>
        /// Invoked once the model’s meshes and hierarchy are loaded, 
        /// but before textures and materials have completed loading.
        /// Stores a reference to the newly loaded <see cref="GameObject"/> 
        /// for further manipulation in the scene.
        /// </summary>
        /// <remarks>
        /// For example, you could reposition or scale the model here,
        /// or attach scripts to the loaded GameObject.
        /// </remarks>
        /// <param name="assetLoaderContext">
        /// The TriLib context that provides references to the loaded model data.
        /// </param>
        private void OnLoad(AssetLoaderContext assetLoaderContext)
        {
            Debug.Log("Model loaded. Loading materials.");

            // Save reference to the loaded model’s root GameObject
            _loadedGameObject = assetLoaderContext.RootGameObject;
        }
    }
}
