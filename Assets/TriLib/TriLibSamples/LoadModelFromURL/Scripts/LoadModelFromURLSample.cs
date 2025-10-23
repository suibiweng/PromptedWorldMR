using UnityEngine;

namespace TriLibCore.Samples
{
    /// <summary>
    /// Demonstrates how to load a compressed (zipped) 3D model from a remote URL using TriLib.
    /// This class creates and configures loader options, downloads the model asynchronously,
    /// and reports progress or errors through callbacks.
    /// </summary>
    public class LoadModelFromURLSample : MonoBehaviour
    {
        /// <summary>
        /// The remote URL pointing to the zipped 3D model file.
        /// </summary>
        [Tooltip("URL of the compressed model file to load.")]
        public string ModelURL = "https://ricardoreis.net/trilib/demos/sample/TriLibSampleModel.zip";

        /// <summary>
        /// Cached <see cref="AssetLoaderOptions"/> instance used to configure the model loading behavior.
        /// </summary>
        private AssetLoaderOptions _assetLoaderOptions;

        /// <summary>
        /// Unity’s Start method which is called on the frame when the script is enabled just before
        /// any of the Update methods are called for the first time.
        /// Creates default loader options if none are set, then begins downloading the model from the specified URL.
        /// </summary>
        /// <remarks>
        /// You can create and store a custom <see cref="AssetLoaderOptions"/> by right-clicking in the Assets folder
        /// and selecting <c>TriLib &gt; Create &gt; AssetLoaderOptions &gt; Pre-Built AssetLoaderOptions</c>.
        /// Alternatively, you can instantiate default options via <c>AssetLoader.CreateDefaultLoaderOptions(false, true)</c> 
        /// as demonstrated below.
        /// </remarks>
        private void Start()
        {
            // Create default AssetLoaderOptions if none exist
            if (_assetLoaderOptions == null)
            {
                _assetLoaderOptions = AssetLoader.CreateDefaultLoaderOptions(false, true);
            }

            // Create a web request for the model's URL
            var webRequest = AssetDownloader.CreateWebRequest(ModelURL);

            // Load the model using the web request, providing callbacks for handling progress, success, and errors
            AssetDownloader.LoadModelFromUri(
                webRequest,
                onLoad: OnLoad,
                onMaterialsLoad: OnMaterialsLoad,
                onProgress: OnProgress,
                onError: OnError,
                wrapperGameObject: null,
                assetLoaderOptions: _assetLoaderOptions
            );
        }

        /// <summary>
        /// Callback invoked when an error occurs during the download or loading process.
        /// Logs the detailed exception information for troubleshooting.
        /// </summary>
        /// <param name="obj">The contextualized error containing the original exception and any relevant context information.</param>
        private void OnError(IContextualizedError obj)
        {
            Debug.LogError($"An error occurred while loading your model: {obj.GetInnerException()}");
        }

        /// <summary>
        /// Callback invoked to report the current model loading progress.
        /// Use this to update UI or track loading status.
        /// </summary>
        /// <param name="assetLoaderContext">The context used by TriLib while loading the model.</param>
        /// <param name="progress">The current loading progress value between 0.0 and 1.0.</param>
        private void OnProgress(AssetLoaderContext assetLoaderContext, float progress)
        {
            Debug.Log($"Loading Model. Progress: {progress:P}");
        }

        /// <summary>
        /// Callback invoked once all model textures and materials have finished loading.
        /// At this stage, the <see cref="GameObject"/> is fully loaded and rendered.
        /// </summary>
        /// <remarks>
        /// The loaded <see cref="GameObject"/> can be accessed via <c>assetLoaderContext.RootGameObject</c>.
        /// </remarks>
        /// <param name="assetLoaderContext">The context containing loading details and the resultant GameObject.</param>
        private void OnMaterialsLoad(AssetLoaderContext assetLoaderContext)
        {
            Debug.Log("All materials have been applied. The model is fully loaded.");
        }

        /// <summary>
        /// Callback invoked when the model’s meshes and hierarchy are loaded, but before textures and materials are applied.
        /// </summary>
        /// <remarks>
        /// The partially loaded <see cref="GameObject"/> can be accessed via <c>assetLoaderContext.RootGameObject</c>.
        /// </remarks>
        /// <param name="assetLoaderContext">The context containing loading details and the resultant GameObject.</param>
        private void OnLoad(AssetLoaderContext assetLoaderContext)
        {
            Debug.Log("Model mesh and hierarchy loaded successfully. Proceeding to load materials...");
        }
    }
}
