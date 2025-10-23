Adds TIFF texture support for TriLib, enabling seamless runtime loading of models that require TIFF textures.

How to use:

On your source code, add a "TiffTextureMapper" instance to your "AssetLoaderOptions.TextureMappers" field.

var tiffTextureMapper = ScriptableObject.CreateInstance<tifftexturemapper>();
AssetLoaderOptions.TextureMappers = new TextureMapper[] { tiffTextureMapper };