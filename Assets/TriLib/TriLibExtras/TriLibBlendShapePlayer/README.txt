A TriLib replacement for Unity's Blend Shape API, offering up to 2000% faster processing times.

How to use:

Add the following code after your "AssetLoaderOptions" declaration:
AssetLoaderOptions.BlendShapeMapper = ScriptableObject.CreateInstance<BlendShapePlayerMapper>();
This should be enough to enable the new Blend Shape Player.