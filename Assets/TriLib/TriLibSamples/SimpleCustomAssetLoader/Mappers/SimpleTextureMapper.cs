#pragma warning disable 672

using System;
using System.IO;
using TriLibCore.Interfaces;
using TriLibCore.Mappers;

namespace TriLibCore.Samples
{
    /// <summary>
    /// A custom <see cref="TextureMapper"/> implementation that uses a user-supplied callback to retrieve texture data.
    /// This mapper allows you to control how texture streams are opened, enabling scenarios such as loading from memory
    /// or over the network.
    /// </summary>
    public class SimpleTextureMapper : TextureMapper
    {
        /// <summary>
        /// The callback responsible for returning a <see cref="Stream"/> to read texture data for a given <see cref="ITexture"/>.
        /// </summary>
        private Func<ITexture, Stream> _streamReceivingCallback;

        /// <summary>
        /// Configures the callback used for texture loading.
        /// </summary>
        /// <param name="streamReceivingCallback">A required callback that returns a <see cref="Stream"/> containing texture data.</param>
        /// <exception cref="Exception">Thrown if <paramref name="streamReceivingCallback"/> is <c>null</c>.</exception>
        public void Setup(Func<ITexture, Stream> streamReceivingCallback)
        {
            if (streamReceivingCallback == null)
            {
                throw new Exception("Callback parameter is missing.");
            }
            _streamReceivingCallback = streamReceivingCallback;
        }

        /// <summary>
        /// Overrides the default texture mapping logic to use the user-supplied callback.
        /// </summary>
        /// <remarks>
        /// TriLib calls this method for each texture that needs to be loaded, allowing you
        /// to retrieve texture data from a custom source.
        /// </remarks>
        /// <param name="textureLoadingContext">The context containing information about the texture being loaded.</param>
        public override void Map(TextureLoadingContext textureLoadingContext)
        {
            // Use the provided callback to retrieve a Stream containing the texture data
            var stream = _streamReceivingCallback(textureLoadingContext.Texture);
            textureLoadingContext.Stream = stream;
        }
    }
}
