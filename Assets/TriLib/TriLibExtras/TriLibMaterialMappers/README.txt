TriLib glTF2 and Autodesk Interactive specialized Material Mappers, providing faster loading times and less memory usage.

How to use:

You must add the "glTF2StandardMaterialMapper" to your "AssetLoaderOptions" instance, to use it. To do that, use the following snippet:

TriLibCore.Utils.GltfMaterialsHelper.SetupStatic(ref assetLoaderOptions); // where assetLoaderOptions is your AssetLoaderOptions instance

The same way, you must add the "AutodeskInteractiveMaterialMapper" to your "AssetLoaderOptions" instance, to use it. To do that, use the following snippet:

TriLibCore.Utils.AutodeskInteractiveMaterialsHelper.SetupStatic(ref assetLoaderOptions); // where assetLoaderOptions is your AssetLoaderOptions instance