using System;
using System.IO;
using System.Text;
using TriLibCore.Interfaces;
using TriLibCore.Utils;
using UnityEngine;

namespace TriLibCore.Samples
{
    /// <summary>
    /// Demonstrates how to load an OBJ model (with a single texture) entirely from string-encoded data.
    /// This class uses <see cref="SimpleCustomAssetLoader"/> to parse model geometry, material (.mtl) files,
    /// and texture images stored as string constants.
    /// </summary>
    public class SimpleCustomLoaderSample : MonoBehaviour
    {
        /// <summary>
        /// Base64-encoded data for a small PNG texture (smile.png).
        /// </summary>
        private const string SmilePngData = "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQAgMAAABinRfyAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAJUExURQAAAP/yAP///1XtZyMAAAA+SURBVAjXY1gFBAyrQkNXMawMDc1iWBoaGsUwNTQ0jGGqoyOMAHNBxJTQUDGGqaIhQK4DYxhEMVgb2ACQUQBbZhuGX7UQtQAAAABJRU5ErkJggg==";

        /// <summary>
        /// OBJ-formatted data (cube geometry) stored as a text string.
        /// </summary>
        private const string CubeObjData =
        @"mtllib cube.mtl
        g default
        v -0.500000 -0.500000 0.500000
        v 0.500000 -0.500000 0.500000
        v -0.500000 0.500000 0.500000
        v 0.500000 0.500000 0.500000
        v -0.500000 0.500000 -0.500000
        v 0.500000 0.500000 -0.500000
        v -0.500000 -0.500000 -0.500000
        v 0.500000 -0.500000 -0.500000
        vt 0.000000 0.000000
        vt 1.000000 0.000000
        vt 1.000000 1.000000
        vt 0.000000 1.000000
        vt 0.000000 1.000000
        vt 1.000000 1.000000
        vt 1.000000 0.000000
        vt 0.000000 0.000000
        vt 0.000000 0.000000
        vt 1.000000 0.000000
        vt 1.000000 1.000000
        vt 0.000000 1.000000
        vt 1.000000 0.000000
        vt 0.000000 0.000000
        vt 0.000000 1.000000
        vt 1.000000 1.000000
        vt 0.000000 0.000000
        vt 1.000000 0.000000
        vt 1.000000 1.000000
        vt 0.000000 1.000000
        vt 0.000000 1.000000
        vt 1.000000 1.000000
        vt 1.000000 0.000000
        vt 0.000000 0.000000
        vn 0.000000 0.000000 1.000000
        vn 0.000000 0.000000 1.000000
        vn 0.000000 0.000000 1.000000
        vn 0.000000 0.000000 1.000000
        vn 0.000000 1.000000 0.000000
        vn 0.000000 1.000000 0.000000
        vn 0.000000 1.000000 0.000000
        vn 0.000000 1.000000 0.000000
        vn 0.000000 0.000000 -1.000000
        vn 0.000000 0.000000 -1.000000
        vn 0.000000 0.000000 -1.000000
        vn 0.000000 0.000000 -1.000000
        vn 0.000000 -1.000000 0.000000
        vn 0.000000 -1.000000 0.000000
        vn 0.000000 -1.000000 0.000000
        vn 0.000000 -1.000000 0.000000
        vn 1.000000 0.000000 0.000000
        vn 1.000000 0.000000 0.000000
        vn 1.000000 0.000000 0.000000
        vn 1.000000 0.000000 0.000000
        vn -1.000000 0.000000 0.000000
        vn -1.000000 0.000000 0.000000
        vn -1.000000 0.000000 0.000000
        vn -1.000000 0.000000 0.000000
        s off
        g cube
        usemtl initialShadingGroup
        f 1/17/1 2/18/2 4/19/3 3/20/4
        f 3/1/5 4/2/6 6/3/7 5/4/8
        f 5/21/9 6/22/10 8/23/11 7/24/12
        f 7/5/13 8/6/14 2/7/15 1/8/16
        f 2/9/17 8/10/18 6/11/19 4/12/20
        f 7/13/21 1/14/22 3/15/23 5/16/24
        ";

        /// <summary>
        /// MTL-formatted data containing material definitions (references the smile.png texture).
        /// </summary>
        private const string CubeMtlData =
        @"newmtl initialShadingGroup
        illum 4
        Kd 1.00 1.00 1.00
        Ka 0.00 0.00 0.00
        Tf 1.00 1.00 1.00
        map_Kd smile.png
        Ni 1.00
        Ks 0.00 0.00 0.00
        Ns 18.00
        ";

        /// <summary>
        /// Filename for the OBJ model.
        /// </summary>
        private const string CubeObjFilename = "cube.obj";

        /// <summary>
        /// Filename for the MTL material file.
        /// </summary>
        private const string CubeMtlFilename = "cube.mtl";

        /// <summary>
        /// Filename for the PNG texture.
        /// </summary>
        private const string SmilePngFilename = "smile.png";

        /// <summary>
        /// Called automatically when the script is first enabled. 
        /// Converts the <c>CubeObjData</c> string to bytes and uses 
        /// <see cref="SimpleCustomAssetLoader"/> to load the model with callbacks.
        /// </summary>
        private void Start()
        {
            var cubeObjBytes = Encoding.UTF8.GetBytes(CubeObjData);
            SimpleCustomAssetLoader.LoadModelFromByteData(
                data: cubeObjBytes,
                modelExtension: FileUtils.GetFileExtension(CubeObjFilename, false),
                onError: OnError,
                onProgress: OnProgress,
                onModelFullyLoad: OnModelFullyLoad,
                customDataReceivingCallback: CustomDataReceivingCallback,
                customFilenameReceivingCallback: CustomFilenameReceivingCallback,
                customTextureReceivingCallback: CustomTextureReceivingCallback,
                modelFilename: CubeObjFilename,
                wrapperGameObject: gameObject
            );
        }

        /// <summary>
        /// Callback for retrieving the texture data stream given a texture reference.
        /// If the requested texture filename matches <see cref="SmilePngFilename"/>, 
        /// the method returns a <see cref="MemoryStream"/> built from Base64-decoded PNG data.
        /// </summary>
        /// <param name="texture">The texture metadata required by TriLib.</param>
        /// <returns>A <see cref="Stream"/> containing the texture data, or <c>null</c> if unrecognized.</returns>
        private Stream CustomTextureReceivingCallback(ITexture texture)
        {
            var textureShortFilename = FileUtils.GetShortFilename(texture.Filename);
            if (textureShortFilename == SmilePngFilename)
            {
                var smilePngBytes = Convert.FromBase64String(SmilePngData);
                return new MemoryStream(smilePngBytes);
            }
            return null;
        }

        /// <summary>
        /// Callback for resolving the full filesystem path for a given filename.
        /// This is optional, so by default we simply return the filename itself.
        /// </summary>
        /// <param name="filename">The original filename reference.</param>
        /// <returns>The resolved path or <paramref name="filename"/> if no changes are necessary.</returns>
        private string CustomFilenameReceivingCallback(string filename)
        {
            return filename;
        }

        /// <summary>
        /// Callback for retrieving external resource data (e.g., .mtl files). 
        /// If the requested filename matches <see cref="CubeMtlFilename"/>, 
        /// this method returns a <see cref="MemoryStream"/> containing the MTL data.
        /// </summary>
        /// <param name="filename">The filename referencing external model data.</param>
        /// <returns>A <see cref="Stream"/> containing the file data, or <c>null</c> if unrecognized.</returns>
        private Stream CustomDataReceivingCallback(string filename)
        {
            var externalDataShortFilename = FileUtils.GetShortFilename(filename);
            if (externalDataShortFilename == CubeMtlFilename)
            {
                var cubeMtlBytes = Encoding.UTF8.GetBytes(CubeMtlData);
                return new MemoryStream(cubeMtlBytes);
            }
            return null;
        }

        /// <summary>
        /// Callback invoked once the model and all referenced resources have finished loading.
        /// If successful, <see cref="AssetLoaderContext.RootGameObject"/> will be instantiated in the scene.
        /// </summary>
        /// <param name="assetLoaderContext">Provides context about the loaded model, including the root <see cref="GameObject"/>.</param>
        private void OnModelFullyLoad(AssetLoaderContext assetLoaderContext)
        {
            if (assetLoaderContext.RootGameObject != null)
            {
                Debug.Log("Model successfully loaded.");
            }
        }

        /// <summary>
        /// Callback invoked periodically to report loading progress, where <c>progress</c> ranges from 0.0 to 1.0.
        /// </summary>
        /// <param name="assetLoaderContext">Provides context about the model loading process.</param>
        /// <param name="progress">A float representing the loading progress (0 to 1).</param>
        private void OnProgress(AssetLoaderContext assetLoaderContext, float progress)
        {
            Debug.Log($"Progress: {progress:P}");
        }

        /// <summary>
        /// Callback invoked if any error occurs while loading the model or its resources.
        /// Logs the error details for debugging.
        /// </summary>
        /// <param name="contextualizedError">Contains exception information and additional context.</param>
        private void OnError(IContextualizedError contextualizedError)
        {
            Debug.LogError($"There was an error loading your model: {contextualizedError}");
        }
    }
}
