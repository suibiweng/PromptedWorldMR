#pragma warning disable 649
using System.IO;
using TriLibCore.General;
using TriLibCore.Mappers;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TriLibCore.Samples
{
    /// <summary>
    /// Demonstrates how to load a 3D model from a file <see cref="Stream"/> using TriLib,
    /// while applying custom mapper scripts (<see cref="ExternalDataMapperSample"/> and <see cref="TextureMapperSample"/>)
    /// to locate external resources (e.g. textures, materials).
    /// This sample loads the <c>TriLibSample.obj</c> model from the <c>Models</c> folder 
    /// and uses the specified mappers to handle external dependencies.
    /// </summary>
    public class LoadModelFromStreamSample : MonoBehaviour
    {
        /// <summary>
        /// Stores the <see cref="AssetLoaderOptions"/> used to configure the model loading process,
        /// including any external data or texture mappers.
        /// </summary>
        private AssetLoaderOptions _assetLoaderOptions;

        /// <summary>
        /// Gets the absolute or relative path to the <c>TriLibSample.obj</c> model, 
        /// adapting for either Editor or runtime usage.
        /// </summary>
        private string ModelPath
        {
            get
            {
#if UNITY_EDITOR
                return $"{Application.dataPath}/TriLib/TriLibSamples/LoadModelFromStream/Models/TriLibSampleModel.obj";
#else
                return "Models/TriLibSampleModel.obj";
#endif
            }
        }

        /// <summary>
        /// Invoked if any error or exception occurs during model loading (e.g., missing file, invalid format).
        /// </summary>
        /// <param name="obj">
        /// An object implementing <see cref="IContextualizedError"/>, containing details on the original exception
        /// and relevant context.
        /// </param>
        private void OnError(IContextualizedError obj)
        {
            Debug.LogError($"An error occurred while loading your Model: {obj.GetInnerException()}");
        }

        /// <summary>
        /// Invoked once the model's meshes and hierarchy have been loaded, prior to materials/textures being fully applied.  
        /// The partial <see cref="GameObject"/> can be accessed through <c>assetLoaderContext.RootGameObject</c>.
        /// </summary>
        /// <remarks>
        /// This callback is an opportunity to configure or position the base mesh 
        /// before textures and materials have finished loading.
        /// </remarks>
        /// <param name="assetLoaderContext">
        /// The context used to load the model, containing references and state 
        /// related to the loading process.
        /// </param>
        private void OnLoad(AssetLoaderContext assetLoaderContext)
        {
            Debug.Log("Model loaded. Loading materials.");
        }

        /// <summary>
        /// Invoked once the model has fully loaded, including all meshes, materials, and textures.  
        /// The final loaded <see cref="GameObject"/> is accessible through <c>assetLoaderContext.RootGameObject</c>.
        /// </summary>
        /// <remarks>
        /// This is typically the best place to interact with the fully loaded asset, as all 
        /// textures and materials are now bound to the model.
        /// </remarks>
        /// <param name="assetLoaderContext">
        /// The context used to load the model, which stores the resulting <see cref="GameObject"/> 
        /// and any other allocation data.
        /// </param>
        private void OnMaterialsLoad(AssetLoaderContext assetLoaderContext)
        {
            Debug.Log("Materials loaded. Model fully loaded.");
        }

        /// <summary>
        /// Invoked whenever the loading progress updates, ranging from 0.0 to 1.0.
        /// </summary>
        /// <param name="assetLoaderContext">
        /// The context that manages the state and data used in the loading process.
        /// </param>
        /// <param name="progress">A normalized float value indicating the current progress (0.0 to 1.0).</param>
        private void OnProgress(AssetLoaderContext assetLoaderContext, float progress)
        {
            Debug.Log($"Loading Model. Progress: {progress:P}");
        }

        /// <summary>
        /// Configures the <see cref="AssetLoaderOptions"/> (if needed) by assigning 
        /// custom <see cref="ExternalDataMapperSample"/> and <see cref="TextureMapperSample"/> 
        /// instances, then opens a file stream pointing to the model and loads it via TriLib.
        /// </summary>
        /// <remarks>
        /// <para>
        /// You can create the <see cref="AssetLoaderOptions"/> by right-clicking in the Unity Assets window 
        /// and selecting <c>TriLib -&gt; Create -&gt; AssetLoaderOptions -&gt; Pre-Built AssetLoaderOptions</c>.
        /// </para>
        /// <para>
        /// Mappers can be assigned in two ways:
        /// <list type="bullet">
        /// <item>Directly in the Editor by adding mapper assets to the <see cref="AssetLoaderOptions"/>.</item>
        /// <item>Programmatically, as shown here, by creating <see cref="ScriptableObject"/> instances at runtime.</item>
        /// </list>
        /// </para>
        /// <para>
        /// You can also create mapper assets by right-clicking any mapper script in the Assets window 
        /// and selecting <c>Create Mapper Instance</c>.
        /// </para>
        /// </remarks>
        private void Start()
        {
            // Assign AssetLoaderOptions with custom mappers if none are provided
            if (_assetLoaderOptions == null)
            {
                _assetLoaderOptions = AssetLoader.CreateDefaultLoaderOptions(false, true);
                _assetLoaderOptions.ExternalDataMapper = ScriptableObject.CreateInstance<ExternalDataMapperSample>();
                _assetLoaderOptions.TextureMappers = new TextureMapper[] { ScriptableObject.CreateInstance<TextureMapperSample>() };
            }

            // Load the model from a FileStream, providing callbacks for loading progress, errors, etc.
            AssetLoader.LoadModelFromStream(
                File.OpenRead(ModelPath),
                ModelPath,
                null,
                OnLoad,
                OnMaterialsLoad,
                OnProgress,
                OnError,
                null,
                _assetLoaderOptions);
        }
    }
}
