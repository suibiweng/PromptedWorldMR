#pragma warning disable 672

using System;
using System.IO;
using TriLibCore.Mappers;

namespace TriLibCore.Samples
{
    /// <summary>
    /// A custom <see cref="ExternalDataMapper"/> implementation that uses user-supplied callbacks 
    /// to handle external data loading. This is useful when the model references additional files
    /// (e.g., textures, external geometry, etc.) and you want fine-grained control over how and 
    /// where those files are retrieved.
    /// </summary>
    public class SimpleExternalDataMapper : ExternalDataMapper
    {
        /// <summary>
        /// The callback responsible for returning a <see cref="Stream"/> to load the external file content.
        /// </summary>
        private Func<string, Stream> _streamReceivingCallback;

        /// <summary>
        /// The callback responsible for providing a full or modified file path from the given original path.
        /// </summary>
        private Func<string, string> _finalPathReceivingCallback;

        /// <summary>
        /// Configures the callbacks used for external data mapping.
        /// </summary>
        /// <param name="streamReceivingCallback">A required callback that returns the <see cref="Stream"/> used to read the requested file’s content.</param>
        /// <param name="finalPathReceivingCallback">An optional callback that modifies or resolves the final file path before loading.</param>
        /// <exception cref="Exception">Thrown if <paramref name="streamReceivingCallback"/> is <c>null</c>.</exception>
        public void Setup(Func<string, Stream> streamReceivingCallback, Func<string, string> finalPathReceivingCallback)
        {
            if (streamReceivingCallback == null)
            {
                throw new Exception("Callback parameter is missing.");
            }
            _streamReceivingCallback = streamReceivingCallback;
            _finalPathReceivingCallback = finalPathReceivingCallback;
        }

        /// <summary>
        /// Overrides the default mapping logic to use the user-supplied callbacks.
        /// </summary>
        /// <remarks>
        /// When TriLib needs to load external data (e.g., texture files), it calls this method,
        /// allowing you to provide custom behavior such as streaming from memory or from an alternative storage location.
        /// </remarks>
        /// <param name="assetLoaderContext">The current TriLib asset loading context containing model-related state.</param>
        /// <param name="originalFilename">The original filename (or path) referencing external data.</param>
        /// <param name="finalPath">The resolved final path of the file. This may be modified by your custom callback.</param>
        /// <returns>An open <see cref="Stream"/> containing the external file data.</returns>
        public override Stream Map(AssetLoaderContext assetLoaderContext, string originalFilename, out string finalPath)
        {
            // Use custom logic to resolve final filename/path
            finalPath = _finalPathReceivingCallback != null ? _finalPathReceivingCallback(originalFilename) : originalFilename;

            // Retrieve a Stream for reading the external data
            return _streamReceivingCallback(originalFilename);
        }
    }
}
