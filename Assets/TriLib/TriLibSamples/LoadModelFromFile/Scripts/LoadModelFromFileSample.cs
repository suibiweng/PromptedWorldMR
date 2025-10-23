#pragma warning disable 649
using TriLibCore.General;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TriLibCore.Samples
{
    /// <summary>
    /// Provides a sample showing how to load the <c>TriLibSample.obj</c> model 
    /// from a specific local path at runtime or in the Unity Editor.  
    /// This example demonstrates basic usage of <see cref="TriLibCore.General.AssetLoader"/> 
    /// for loading 3D assets from a file.
    /// </summary>
    public class LoadModelFromFileSample : MonoBehaviour
    {
        /// <summary>
        /// Stores <see cref="AssetLoaderOptions"/> for configuring how TriLib loads the model 
        /// (e.g., whether to import animations, materials, etc.).
        /// </summary>
        private AssetLoaderOptions _assetLoaderOptions;

        /// <summary>
        /// Gets the path to the <c>TriLibSample.obj</c> model.  
        /// When in the Unity Editor, a direct path to the <c>Assets/TriLib</c> folder is returned; 
        /// at runtime, a relative path is used.
        /// </summary>
        private string ModelPath
        {
            get
            {
#if UNITY_EDITOR
                return $"{Application.dataPath}/TriLib/TriLibSamples/LoadModelFromFile/Models/TriLibSampleModel.obj";
#else
                return "Models/TriLibSampleModel.obj";
#endif
            }
        }
        /// <summary>
        /// Triggered if an error occurs during model loading, such as missing files or format issues.
        /// </summary>
        /// <param name="obj">
        /// The contextualized error, containing both the original exception and the context 
        /// passed to the method where the error was thrown.
        /// </param>
        private void OnError(IContextualizedError obj)
        {
            Debug.LogError($"An error occurred while loading your Model: {obj.GetInnerException()}");
        }

        /// <summary>
        /// Called when the model's meshes and hierarchy are first loaded, before materials and textures finish.  
        /// The partially loaded <see cref="GameObject"/> is available through <c>assetLoaderContext.RootGameObject</c>.
        /// </summary>
        /// <param name="assetLoaderContext">The context used to load the model.</param>
        /// <remarks>
        /// If your application needs to do any setup or processing of the base mesh data 
        /// before materials are applied, use this callback.
        /// </remarks>
        private void OnLoad(AssetLoaderContext assetLoaderContext)
        {
            Debug.Log("Model loaded. Loading materials.");
        }

        /// <summary>
        /// Called when the model (including all textures and materials) has finished loading.  
        /// The fully loaded <see cref="GameObject"/> is available through <c>assetLoaderContext.RootGameObject</c>.
        /// </summary>
        /// <param name="assetLoaderContext">The context that was used to load the model.</param>
        /// <remarks>
        /// After this callback, the model is completely ready, 
        /// including its hierarchy, meshes, materials, and textures.
        /// </remarks>
        private void OnMaterialsLoad(AssetLoaderContext assetLoaderContext)
        {
            Debug.Log("Materials loaded. Model fully loaded.");
        }

        /// <summary>
        /// Invoked when there is an update in the loading progress of the model (0% to 100%).
        /// </summary>
        /// <param name="assetLoaderContext">The context used to load the model, which holds relevant loading information.</param>
        /// <param name="progress">The current load progress, as a normalized value between 0.0 and 1.0.</param>
        private void OnProgress(AssetLoaderContext assetLoaderContext, float progress)
        {
            Debug.Log($"Loading Model. Progress: {progress:P}");
        }

        /// <summary>
        /// Automatically loads the <c>TriLibSample.obj</c> model once the script starts running, 
        /// using either a user-defined <see cref="AssetLoaderOptions"/> or a default configuration.
        /// </summary>
        /// <remarks>
        /// You can create <see cref="AssetLoaderOptions"/> by right-clicking in the Assets window 
        /// and selecting <c>TriLib -&gt; Create -&gt; AssetLoaderOptions -&gt; Pre-Built AssetLoaderOptions</c>.
        /// </remarks>
        private void Start()
        {
            // Use a default set of loader options if none were set via the Inspector or elsewhere.
            if (_assetLoaderOptions == null)
            {
                _assetLoaderOptions = AssetLoader.CreateDefaultLoaderOptions(false, true);
            }
            // Instruct TriLib to load the model from file, providing callbacks for progress, errors, etc.
            AssetLoader.LoadModelFromFile(ModelPath, OnLoad, OnMaterialsLoad, OnProgress, OnError, null, _assetLoaderOptions);
        }
    }
}
