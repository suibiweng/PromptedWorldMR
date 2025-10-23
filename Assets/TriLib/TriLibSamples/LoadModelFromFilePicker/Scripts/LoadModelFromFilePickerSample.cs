#pragma warning disable 649
using UnityEngine;
using TriLibCore.Extensions;
using UnityEngine.UI;

namespace TriLibCore.Samples
{
    /// <summary>
    /// Demonstrates how to load a 3D model via a file-picker at runtime 
    /// using <see cref="TriLibCore.General.AssetLoaderFilePicker"/>.
    /// This sample displays a UI button to open the picker, and updates 
    /// a progress label while the model is being loaded.
    /// </summary>
    public class LoadModelFromFilePickerSample : MonoBehaviour
    {
        /// <summary>
        /// Stores the <see cref="AssetLoaderOptions"/> instance used to configure model loading behavior.
        /// </summary>
        private AssetLoaderOptions _assetLoaderOptions;

        /// <summary>
        /// Stores a reference to the most recently loaded <see cref="GameObject"/>. 
        /// If a new model is loaded, the old GameObject is destroyed.
        /// </summary>
        private GameObject _loadedGameObject;

        /// <summary>
        /// Button used to open the model file-picker dialog.
        /// </summary>
        [SerializeField]
        private Button _loadModelButton;

        /// <summary>
        /// Text element used to display the current loading progress.
        /// </summary>
        [SerializeField]
        private Text _progressText;
        /// <summary>
        /// Invoked by the UI to create or use existing <see cref="AssetLoaderOptions"/>, 
        /// then open the file-picker and load the selected model asynchronously.
        /// </summary>
        /// <remarks>
        /// You can create <see cref="AssetLoaderOptions"/> assets by right-clicking in the 
        /// Assets window and selecting <c>TriLib &gt; Create &gt; AssetLoaderOptions &gt; Pre-Built AssetLoaderOptions</c>.
        /// </remarks>
        public void LoadModel()
        {
            if (_assetLoaderOptions == null)
            {
                // Create default loader options if none are set
                _assetLoaderOptions = AssetLoader.CreateDefaultLoaderOptions(false, true);
            }

            // Create an AssetLoaderFilePicker to open a file dialog
            var assetLoaderFilePicker = AssetLoaderFilePicker.Create();
            // Asynchronously load the chosen model, providing callbacks for various events
            assetLoaderFilePicker.LoadModelFromFilePickerAsync(
                "Select a Model file",
                OnLoad,
                OnMaterialsLoad,
                OnProgress,
                OnBeginLoad,
                OnError,
                null,
                _assetLoaderOptions);
        }

        /// <summary>
        /// Called when the file-picker either begins or cancels the loading operation.
        /// </summary>
        /// <param name="filesSelected">
        /// Indicates whether at least one file has been selected. 
        /// If <c>true</c>, loading has begun; if <c>false</c>, the operation was canceled.
        /// </param>
        private void OnBeginLoad(bool filesSelected)
        {
            // Disable the button if loading is in progress; show progress text if files are selected
            _loadModelButton.interactable = !filesSelected;
            _progressText.enabled = filesSelected;
        }

        /// <summary>
        /// Called if an exception or error occurs at any point during model loading.
        /// </summary>
        /// <param name="obj">
        /// An object implementing <see cref="IContextualizedError"/>, containing details on the exception.
        /// </param>
        private void OnError(IContextualizedError obj)
        {
            Debug.LogError($"An error occurred while loading your Model: {obj.GetInnerException()}");
        }

        /// <summary>
        /// Called after the model's meshes and hierarchy have been loaded, but before materials are finished. 
        /// Allows performing tasks with the partial model (e.g., positioning it, adding components).
        /// </summary>
        /// <remarks>
        /// The loaded <see cref="GameObject"/> can be accessed through 
        /// <c>assetLoaderContext.RootGameObject</c>, but note that textures 
        /// and materials may not be fully applied at this time.
        /// </remarks>
        /// <param name="assetLoaderContext">
        /// The context used to load the model, which contains references to the loaded data.
        /// </param>
        private void OnLoad(AssetLoaderContext assetLoaderContext)
        {
            // Destroy previously loaded model if present
            if (_loadedGameObject != null)
            {
                Destroy(_loadedGameObject);
            }

            // Store the new model reference
            _loadedGameObject = assetLoaderContext.RootGameObject;

            // Optionally fit the camera to display the loaded model
            if (_loadedGameObject != null)
            {
                Camera.main.FitToBounds(assetLoaderContext.RootGameObject, 2f);
            }
        }

        /// <summary>
        /// Called after all meshes, materials, and textures have finished loading.
        /// This indicates the model is fully ready and visible in the scene.
        /// </summary>
        /// <remarks>
        /// The loaded <see cref="GameObject"/> can be accessed through 
        /// <c>assetLoaderContext.RootGameObject</c> if needed for further customization.
        /// </remarks>
        /// <param name="assetLoaderContext">
        /// The context used to load the model, which also holds the finished <see cref="GameObject"/>.
        /// </param>
        private void OnMaterialsLoad(AssetLoaderContext assetLoaderContext)
        {
            if (assetLoaderContext.RootGameObject != null)
            {
                Debug.Log("Model fully loaded.");
            }
            else
            {
                Debug.Log("Model could not be loaded.");
            }
            // Re-enable the button and hide the progress text once loading is complete
            _loadModelButton.interactable = true;
            _progressText.enabled = false;
        }

        /// <summary>
        /// Called whenever the loading process updates its progress, ranging from 0.0 to 1.0.
        /// </summary>
        /// <param name="assetLoaderContext">
        /// The context used to load the model, providing details on its state and configuration.
        /// </param>
        /// <param name="progress">The current loading progress as a value between 0.0 and 1.0.</param>
        private void OnProgress(AssetLoaderContext assetLoaderContext, float progress)
        {
            _progressText.text = $"Progress: {progress:P}";
        }
    }
}
