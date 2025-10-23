#pragma warning disable 672

using System;
using System.IO;
using TriLibCore.Interfaces;
using TriLibCore.Mappers;
using TriLibCore.Utils;
using UnityEngine;

namespace TriLibCore.Samples
{

    /// <summary>
    /// Provides static methods for loading 3D models from byte arrays or <see cref="Stream"/> objects. 
    /// This class integrates custom data and texture mappers via user-supplied callbacks, enabling 
    /// flexible loading scenarios such as in-memory data, custom file paths, or network streams.
    /// </summary>
    public class SimpleCustomAssetLoader
    {
        /// <summary>
        /// Loads a model from an in-memory byte array, using callbacks to handle events and external data.
        /// </summary>
        /// <param name="data">The raw byte array containing the model data.</param>
        /// <param name="modelExtension">The model file extension (e.g., ".fbx", ".obj"). If omitted, you can provide a <paramref name="modelFilename"/> to infer the extension.</param>
        /// <param name="onError">An optional callback invoked if an error occurs during loading.</param>
        /// <param name="onProgress">A callback invoked to report loading progress, where the float parameter goes from 0.0 to 1.0.</param>
        /// <param name="onModelFullyLoad">A callback invoked once the model is fully loaded (including textures and materials).</param>
        /// <param name="customDataReceivingCallback">A required callback for obtaining external file data streams when additional files are referenced.</param>
        /// <param name="customFilenameReceivingCallback">An optional callback to resolve or modify the final filename from the original reference.</param>
        /// <param name="customTextureReceivingCallback">A required callback for obtaining texture data streams.</param>
        /// <param name="modelFilename">An optional filename to associate with the model. If provided, TriLib may use this to infer the file extension.</param>
        /// <param name="wrapperGameObject">An optional <see cref="GameObject"/> to serve as a parent for the loaded model’s root object.</param>
        /// <param name="assetLoaderOptions">An optional set of loading options for finer control over the model load process.</param>
        /// <param name="customData">Any custom user data that should be passed through TriLib’s loading pipeline and accessible in callbacks.</param>
        /// <returns>An <see cref="AssetLoaderContext"/> containing references to the loaded root <see cref="GameObject"/> and other metadata.</returns>
        /// <exception cref="Exception">Thrown if <paramref name="data"/> is null or empty.</exception>
        public static AssetLoaderContext LoadModelFromByteData(
            byte[] data,
            string modelExtension,
            Action<IContextualizedError> onError,
            Action<AssetLoaderContext, float> onProgress,
            Action<AssetLoaderContext> onModelFullyLoad,
            Func<string, Stream> customDataReceivingCallback,
            Func<string, string> customFilenameReceivingCallback,
            Func<ITexture, Stream> customTextureReceivingCallback,
            string modelFilename = null,
            GameObject wrapperGameObject = null,
            AssetLoaderOptions assetLoaderOptions = null,
            object customData = null)
        {
            if (data == null || data.Length == 0)
            {
                throw new Exception("Missing model file byte data.");
            }

            // Create a MemoryStream from the provided byte array and forward to the overload that handles streams
            return LoadModelFromStream(
                new MemoryStream(data),
                modelExtension,
                onError,
                onProgress,
                onModelFullyLoad,
                customDataReceivingCallback,
                customFilenameReceivingCallback,
                customTextureReceivingCallback,
                modelFilename,
                wrapperGameObject,
                assetLoaderOptions,
                customData
            );
        }

        /// <summary>
        /// Loads a model from a <see cref="Stream"/>, using callbacks to handle external data and textures.
        /// </summary>
        /// <remarks>
        /// This method is useful if you have already prepared a <see cref="Stream"/>, such as from a custom 
        /// data source, a file, or network operation. You can attach event callbacks to monitor progress, 
        /// receive errors, and handle external dependencies or textures.
        /// </remarks>
        /// <param name="stream">The <see cref="Stream"/> containing the model data.</param>
        /// <param name="modelExtension">The model file extension (e.g., ".fbx", ".obj"). This is used by TriLib to determine import logic.</param>
        /// <param name="onError">An optional callback invoked if an error occurs during loading.</param>
        /// <param name="onProgress">A callback to report loading progress, from 0.0 (start) to 1.0 (fully loaded).</param>
        /// <param name="onModelFullyLoad">A callback invoked once the model is fully loaded (including meshes, materials, and textures).</param>
        /// <param name="customDataReceivingCallback">A required callback for obtaining streams to any external file data the model references.</param>
        /// <param name="customFilenameReceivingCallback">An optional callback to modify or resolve filenames before loading.</param>
        /// <param name="customTextureReceivingCallback">A required callback for obtaining texture data streams.</param>
        /// <param name="modelFilename">An optional filename to represent the model. If an extension is not provided in <paramref name="modelExtension"/>, it will be derived from this parameter.</param>
        /// <param name="wrapperGameObject">An optional <see cref="GameObject"/> that will become the parent of the loaded model’s root <see cref="GameObject"/>.</param>
        /// <param name="assetLoaderOptions">Optional loading options to customize import settings, scaling, etc.</param>
        /// <param name="customData">Any additional user data to be passed through the loading pipeline and accessible within callbacks.</param>
        /// <returns>An <see cref="AssetLoaderContext"/> containing the loaded model’s references and metadata.</returns>
        /// <exception cref="Exception">Thrown if <paramref name="stream"/> is <c>null</c> or if the model extension cannot be resolved.</exception>
        public static AssetLoaderContext LoadModelFromStream(
            Stream stream,
            string modelExtension,
            Action<IContextualizedError> onError,
            Action<AssetLoaderContext, float> onProgress,
            Action<AssetLoaderContext> onModelFullyLoad,
            Func<string, Stream> customDataReceivingCallback,
            Func<string, string> customFilenameReceivingCallback,
            Func<ITexture, Stream> customTextureReceivingCallback,
            string modelFilename = null,
            GameObject wrapperGameObject = null,
            AssetLoaderOptions assetLoaderOptions = null,
            object customData = null)
        {
            if (stream == null)
            {
                throw new Exception("Missing model file stream.");
            }

            // Attempt to infer the file extension if it is not provided
            if (string.IsNullOrWhiteSpace(modelExtension) && !string.IsNullOrWhiteSpace(modelFilename))
            {
                modelExtension = FileUtils.GetFileExtension(modelFilename);
            }
            if (string.IsNullOrWhiteSpace(modelExtension))
            {
                throw new Exception("Missing model extension parameter.");
            }

            // Create instances of our custom data mappers
            var simpleExternalDataMapper = ScriptableObject.CreateInstance<SimpleExternalDataMapper>();
            simpleExternalDataMapper.Setup(customDataReceivingCallback, customFilenameReceivingCallback);

            var simpleTextureMapper = ScriptableObject.CreateInstance<SimpleTextureMapper>();
            simpleTextureMapper.Setup(customTextureReceivingCallback);

            // If no AssetLoaderOptions are provided, create a default set
            if (assetLoaderOptions == null)
            {
                assetLoaderOptions = AssetLoader.CreateDefaultLoaderOptions();
            }

            // Inject the custom data and texture mappers into the AssetLoaderOptions
            assetLoaderOptions.ExternalDataMapper = simpleExternalDataMapper;
            assetLoaderOptions.TextureMappers = new TextureMapper[] { simpleTextureMapper };

            // Use the standard TriLib stream-loading mechanism with custom mappers and callbacks
            return AssetLoader.LoadModelFromStream(
                stream,
                modelFilename,
                modelExtension,
                null,
                onModelFullyLoad,
                onProgress,
                onError,
                wrapperGameObject,
                assetLoaderOptions,
                customData
            );
        }
    }
}
