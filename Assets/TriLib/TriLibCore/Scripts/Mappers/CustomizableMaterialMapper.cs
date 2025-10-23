using System;
using System.Collections;
using TriLibCore.General;
using UnityEngine;

namespace TriLibCore.Mappers
{
    /// <summary>
    /// A customizable material mapper for converting source materials into Unity materials during runtime model importing.
    /// </summary>
    [Serializable]
    [CreateAssetMenu(menuName = "TriLib/Mappers/Material/Customizable Material Mapper", fileName = "CustomizableMaterialMapper")]
    public class CustomizableMaterialMapper : MaterialMapper
    {
        /// <summary>
        /// The number of generic material properties supported by this mapper.
        /// </summary>
        public const int GenericPropertiesCount = 20;

        /// <summary>
        /// Array mapping generic material properties to their corresponding indices.
        /// </summary>
        public static int[] GenericPropertyIndices = { 0, 1, 2, 3, 4, 7, 8, 6, 10, 11, 12, 14, 13, 15, 9, 5, -1, 18, 19, -1, 16, 17 };

        /// <summary>
        /// Specifies the compatible material shading setups for this mapper.
        /// </summary>
        [SerializeField]
        private MaterialShadingSetup _compatibleSetups;

        /// <summary>
        /// Indicates whether material textures should be converted during mapping.
        /// </summary>
        [SerializeField]
        private bool _convertMaterialTextures;

        /// <summary>
        /// The material preset used for cutout materials.
        /// </summary>
        [SerializeField]
        private Material _cutoutMaterialPreset;

        /// <summary>
        /// The material preset used for cutout materials without metallic textures.
        /// </summary>
        [SerializeField]
        private Material _cutoutNoMetallicMaterialPreset;

        /// <summary>
        /// Indicates whether metallic and smoothness values should be extracted from textures.
        /// </summary>
        [SerializeField]
        private bool _extractMetallicAndSmoothness;

        /// <summary>
        /// Indicates whether this mapper is compatible with the current material context.
        /// </summary>
        [SerializeField]
        private bool _isCompatible = true;

        /// <summary>
        /// Array of shader keywords used for material properties.
        /// </summary>
        [SerializeField]
        private string[] _keywords = new string[GenericPropertiesCount];

        /// <summary>
        /// The default material preset used for standard materials.
        /// </summary>
        [SerializeField]
        private Material _materialPreset;

        /// <summary>
        /// The material preset used for standard materials without metallic textures.
        /// </summary>
        [SerializeField]
        private Material _noMetallicMaterialPreset;

        /// <summary>
        /// The material preset used for transparent compose materials.
        /// </summary>
        [SerializeField]
        private Material _transparentComposeMaterialPreset;

        /// <summary>
        /// The material preset used for transparent compose materials without metallic textures.
        /// </summary>
        [SerializeField]
        private Material _transparentComposeNoMetallicMaterialPreset;

        /// <summary>
        /// The material preset used for transparent materials.
        /// </summary>
        [SerializeField]
        private Material _transparentMaterialPreset;

        /// <summary>
        /// The material preset used for transparent materials without metallic textures.
        /// </summary>
        [SerializeField]
        private Material _transparentNoMetallicMaterialPreset;

        /// <summary>
        /// Indicates whether to use a shader variant collection for material optimization.
        /// </summary>
        [SerializeField]
        private bool _useShaderVariantCollection;

        /// <summary>
        /// Array of property names mapped to generic material properties.
        /// </summary>
        [SerializeField]
        private string[] _values = new string[GenericPropertiesCount];

        /// <summary>
        /// Gets whether material textures should be converted.
        /// </summary>
        public override bool ConvertMaterialTextures => _convertMaterialTextures;

        /// <summary>
        /// Gets the cutout material preset.
        /// </summary>
        public override Material CutoutMaterialPreset => GetMaterialPreset(_cutoutMaterialPreset);

        /// <summary>
        /// Gets the cutout material preset without metallic textures.
        /// </summary>
        public override Material CutoutMaterialPresetNoMetallicTexture => GetMaterialPreset(_cutoutNoMetallicMaterialPreset);

        /// <summary>
        /// Gets the default material preset.
        /// </summary>
        public override Material MaterialPreset => GetMaterialPreset(_materialPreset);

        /// <summary>
        /// Gets the material preset without metallic textures.
        /// </summary>
        public override Material MaterialPresetNoMetallicTexture => GetMaterialPreset(_noMetallicMaterialPreset);

        /// <summary>
        /// Gets the transparent compose material preset.
        /// </summary>
        public override Material TransparentComposeMaterialPreset => GetMaterialPreset(_transparentComposeMaterialPreset);

        /// <summary>
        /// Gets the transparent compose material preset without metallic textures.
        /// </summary>
        public override Material TransparentComposeMaterialPresetNoMetallicTexture => GetMaterialPreset(_transparentComposeNoMetallicMaterialPreset);

        /// <summary>
        /// Gets the transparent material preset.
        /// </summary>
        public override Material TransparentMaterialPreset => GetMaterialPreset(_transparentMaterialPreset);

        /// <summary>
        /// Gets the transparent material preset without metallic textures.
        /// </summary>
        public override Material TransparentMaterialPresetNoMetallicTexture => GetMaterialPreset(_transparentNoMetallicMaterialPreset);

        /// <summary>
        /// Gets whether this mapper uses coroutines for asynchronous processing.
        /// </summary>
        public override bool UsesCoroutines => true;

        /// <summary>
        /// Gets whether to use a shader variant collection.
        /// </summary>
        public override bool UseShaderVariantCollection => _useShaderVariantCollection;

        /// <summary>
        /// Gets whether metallic and smoothness values should be extracted.
        /// </summary>
        public override bool ExtractMetallicAndSmoothness => _extractMetallicAndSmoothness;

        /// <summary>
        /// The material to use while the Material Mapper is processing.
        /// </summary>
        public override Material LoadingMaterial => _materialPreset;

        /// <summary>
        /// Gets the shader property name for the diffuse color.
        /// </summary>
        /// <param name="materialMapperContext">The material mapping context.</param>
        /// <returns>The shader property name for the diffuse color.</returns>
        public override string GetDiffuseColorName(MaterialMapperContext materialMapperContext)
        {
            return GetMappedProperty(GenericMaterialProperty.DiffuseColor);
        }

        /// <summary>
        /// Gets the shader property name for the diffuse texture.
        /// </summary>
        /// <param name="materialMapperContext">The material mapping context.</param>
        /// <returns>The shader property name for the diffuse texture.</returns>
        public override string GetDiffuseTextureName(MaterialMapperContext materialMapperContext)
        {
            return GetMappedProperty(GenericMaterialProperty.DiffuseMap);
        }

        /// <summary>
        /// Gets the shader property name for the emission color.
        /// </summary>
        /// <param name="materialMapperContext">The material mapping context.</param>
        /// <returns>The shader property name for the emission color.</returns>
        public override string GetEmissionColorName(MaterialMapperContext materialMapperContext)
        {
            return GetMappedProperty(GenericMaterialProperty.EmissionColor);
        }

        /// <summary>
        /// Gets the shader property name for glossiness or roughness.
        /// </summary>
        /// <param name="materialMapperContext">The material mapping context.</param>
        /// <returns>The shader property name for glossiness or roughness.</returns>
        public override string GetGlossinessOrRoughnessName(MaterialMapperContext materialMapperContext)
        {
            return GetMappedProperty(GenericMaterialProperty.GlossinessOrRoughness);
        }

        /// <summary>
        /// Gets the shader property name for the glossiness or roughness texture.
        /// </summary>
        /// <param name="materialMapperContext">The material mapping context.</param>
        /// <returns>The shader property name for the glossiness or roughness texture.</returns>
        public override string GetGlossinessOrRoughnessTextureName(MaterialMapperContext materialMapperContext)
        {
            return GetMappedProperty(GenericMaterialProperty.GlossinessOrRoughnessMap);
        }

        /// <summary>
        /// Gets the shader property name for the metallic value.
        /// </summary>
        /// <param name="materialMapperContext">The material mapping context.</param>
        /// <returns>The shader property name for the metallic value.</returns>
        public override string GetMetallicName(MaterialMapperContext materialMapperContext)
        {
            return GetMappedProperty(GenericMaterialProperty.Metallic);
        }

        /// <summary>
        /// Gets the shader property name for the metallic texture.
        /// </summary>
        /// <param name="materialMapperContext">The material mapping context.</param>
        /// <returns>The shader property name for the metallic texture.</returns>
        public override string GetMetallicTextureName(MaterialMapperContext materialMapperContext)
        {
            return GetMappedProperty(GenericMaterialProperty.EmissionMap);
        }

        /// <summary>
        /// Determines if this mapper is compatible with the provided material context.
        /// </summary>
        /// <param name="materialMapperContext">The material mapping context.</param>
        /// <returns>True if compatible, otherwise false.</returns>
        public override bool IsCompatible(MaterialMapperContext materialMapperContext)
        {
            if (materialMapperContext == null)
            {
                return _isCompatible;
            }
            return _isCompatible && _compatibleSetups.HasFlag(materialMapperContext.Material.MaterialShadingSetup);
        }

        /// <summary>
        /// Maps material properties using coroutines for asynchronous processing.
        /// </summary>
        /// <param name="materialMapperContext">The material mapping context.</param>
        /// <returns>An enumerable of coroutine steps.</returns>
        public override IEnumerable MapCoroutine(MaterialMapperContext materialMapperContext)
        {
            materialMapperContext.VirtualMaterial = new VirtualMaterial();

            foreach (var item in CheckTransparencyMapTexture(materialMapperContext))
            {
                yield return item;
            }
            foreach (var item in CheckSpecularMapTexture(materialMapperContext))
            {
                yield return item;
            }
            foreach (var item in CheckDiffuseMapTexture(materialMapperContext))
            {
                yield return item;
            }
            foreach (var item in CheckDiffuseColor(materialMapperContext))
            {
                yield return item;
            }
            foreach (var item in CheckNormalMapTexture(materialMapperContext))
            {
                yield return item;
            }
            foreach (var item in CheckEmissionColor(materialMapperContext))
            {
                yield return item;
            }
            foreach (var item in CheckEmissionMapTexture(materialMapperContext))
            {
                yield return item;
            }
            foreach (var item in CheckOcclusionMapTexture(materialMapperContext))
            {
                yield return item;
            }
            foreach (var item in CheckGlossinessMapTexture(materialMapperContext))
            {
                yield return item;
            }
            foreach (var item in CheckGlossinessValue(materialMapperContext))
            {
                yield return item;
            }
            foreach (var item in CheckMetallicGlossMapTexture(materialMapperContext))
            {
                yield return item;
            }
            foreach (var item in CheckMetallicValue(materialMapperContext))
            {
                yield return item;
            }
            foreach (var item in CheckDisplacementTexture(materialMapperContext))
            {
                yield return item;
            }
            BuildMaterial(materialMapperContext);
        }

        /// <summary>
        /// Applies a diffuse map texture to the material.
        /// </summary>
        /// <param name="textureLoadingContext">The texture loading context.</param>
        /// <returns>An enumerable of coroutine steps.</returns>
        protected virtual IEnumerable ApplyDiffuseMapTexture(TextureLoadingContext textureLoadingContext)
        {
            ApplyTexture(textureLoadingContext, GenericMaterialProperty.DiffuseMap);
            yield break;
        }

        /// <summary>
        /// Applies a displacement texture to the material.
        /// </summary>
        /// <param name="textureLoadingContext">The texture loading context.</param>
        /// <returns>An enumerable of coroutine steps.</returns>
        protected virtual IEnumerable ApplyDisplacementTexture(TextureLoadingContext textureLoadingContext)
        {
            ApplyTexture(textureLoadingContext, GenericMaterialProperty.DisplacementMap);
            yield break;
        }

        /// <summary>
        /// Applies an emission map texture to the material.
        /// </summary>
        /// <param name="textureLoadingContext">The texture loading context.</param>
        /// <returns>An enumerable of coroutine steps.</returns>
        protected virtual IEnumerable ApplyEmissionMapTexture(TextureLoadingContext textureLoadingContext)
        {
            ApplyTexture(textureLoadingContext, GenericMaterialProperty.EmissionMap);
            yield break;
        }

        /// <summary>
        /// Applies a glossiness or roughness map texture to the material.
        /// </summary>
        /// <param name="textureLoadingContext">The texture loading context.</param>
        /// <returns>An enumerable of coroutine steps.</returns>
        protected virtual IEnumerable ApplyGlossinessMapTexture(TextureLoadingContext textureLoadingContext)
        {
            ApplyTexture(textureLoadingContext, GenericMaterialProperty.GlossinessOrRoughnessMap);
            yield break;
        }

        /// <summary>
        /// Applies shader keywords for a given material property.
        /// </summary>
        /// <param name="materialMapperContext">The material mapping context.</param>
        /// <param name="genericMaterialProperty">The generic material property.</param>
        protected virtual void ApplyKeywords(MaterialMapperContext materialMapperContext, GenericMaterialProperty genericMaterialProperty)
        {
            if (UseShaderVariantCollection && materialMapperContext.VirtualMaterial.GenericPropertyIsSet(genericMaterialProperty))
            {
                var finalIndex = GenericPropertyIndices[(int)genericMaterialProperty];
                if (finalIndex < 0)
                {
                    throw new Exception("Unused material property requested.");
                }
                var keywords = _keywords[finalIndex];
                if (!string.IsNullOrWhiteSpace(keywords))
                {
                    var keywordsArray = keywords.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var keyword in keywordsArray)
                    {
                        materialMapperContext.VirtualMaterial.EnableKeyword(keyword);
                    }
                }
            }
        }

        /// <summary>
        /// Applies a metallic gloss map texture to the material.
        /// </summary>
        /// <param name="textureLoadingContext">The texture loading context.</param>
        /// <returns>An enumerable of coroutine steps.</returns>
        protected virtual IEnumerable ApplyMetallicGlossMapTexture(TextureLoadingContext textureLoadingContext)
        {
            ApplyTexture(textureLoadingContext, GenericMaterialProperty.MetallicMap);
            yield break;
        }

        /// <summary>
        /// Applies a normal map texture to the material.
        /// </summary>
        /// <param name="textureLoadingContext">The texture loading context.</param>
        /// <returns>An enumerable of coroutine steps.</returns>
        protected virtual IEnumerable ApplyNormalMapTexture(TextureLoadingContext textureLoadingContext)
        {
            ApplyTexture(textureLoadingContext, GenericMaterialProperty.NormalMap);
            yield break;
        }

        /// <summary>
        /// Applies an occlusion map texture to the material.
        /// </summary>
        /// <param name="textureLoadingContext">The texture loading context.</param>
        /// <returns>An enumerable of coroutine steps.</returns>
        protected virtual IEnumerable ApplyOcclusionMapTexture(TextureLoadingContext textureLoadingContext)
        {
            ApplyTexture(textureLoadingContext, GenericMaterialProperty.OcclusionMap);
            yield break;
        }

        /// <summary>
        /// Applies a specular map texture to the material.
        /// </summary>
        /// <param name="textureLoadingContext">The texture loading context.</param>
        /// <returns>An enumerable of coroutine steps.</returns>
        protected virtual IEnumerable ApplySpecularMapTexture(TextureLoadingContext textureLoadingContext)
        {
            ApplyTexture(textureLoadingContext, GenericMaterialProperty.SpecularMap);
            yield break;
        }

        /// <summary>
        /// Applies a texture to the material for a specific generic property.
        /// </summary>
        /// <param name="textureLoadingContext">The texture loading context.</param>
        /// <param name="genericMaterialProperty">The generic material property.</param>
        protected virtual void ApplyTexture(TextureLoadingContext textureLoadingContext, GenericMaterialProperty genericMaterialProperty)
        {
            if (textureLoadingContext.UnityTexture != null)
            {
                textureLoadingContext.Context.AddUsedTexture(textureLoadingContext.UnityTexture);
                ApplyKeywords(textureLoadingContext.MaterialMapperContext, genericMaterialProperty);
            }
            textureLoadingContext.MaterialMapperContext.VirtualMaterial.SetProperty(GetMappedProperty(genericMaterialProperty), textureLoadingContext.UnityTexture, genericMaterialProperty);
        }

        /// <summary>
        /// Applies a transparency map texture to the material.
        /// </summary>
        /// <param name="textureLoadingContext">The texture loading context.</param>
        /// <returns>An enumerable of coroutine steps.</returns>
        protected virtual IEnumerable ApplyTransparencyMapTexture(TextureLoadingContext textureLoadingContext)
        {
            ApplyTexture(textureLoadingContext, GenericMaterialProperty.TransparencyMap);
            yield break;
        }

        /// <summary>
        /// Checks and applies the diffuse color to the material.
        /// </summary>
        /// <param name="materialMapperContext">The material mapping context.</param>
        /// <returns>An enumerable of coroutine steps.</returns>
        protected virtual IEnumerable CheckDiffuseColor(MaterialMapperContext materialMapperContext)
        {
            var value = materialMapperContext.Material.GetGenericColorValueMultiplied(GenericMaterialProperty.DiffuseColor, materialMapperContext);
            value.a *= materialMapperContext.Material.GetGenericFloatValueMultiplied(GenericMaterialProperty.AlphaValue);
            materialMapperContext.VirtualMaterial.HasAlpha |= value.a < 1f;
            materialMapperContext.VirtualMaterial.SetProperty(GetMappedProperty(GenericMaterialProperty.DiffuseColor), materialMapperContext.Context.Options.ApplyGammaCurveToMaterialColors ? value.gamma : value);
            ApplyKeywords(materialMapperContext, GenericMaterialProperty.DiffuseColor);
            yield break;
        }

        /// <summary>
        /// Checks and loads the diffuse map texture.
        /// </summary>
        /// <param name="materialMapperContext">The material mapping context.</param>
        /// <returns>An enumerable of coroutine steps.</returns>
        protected virtual IEnumerable CheckDiffuseMapTexture(MaterialMapperContext materialMapperContext)
        {
            var diffuseTexturePropertyName = materialMapperContext.Material.GetGenericPropertyName(GenericMaterialProperty.DiffuseMap);
            var textureValue = materialMapperContext.Material.GetTextureValue(diffuseTexturePropertyName);
            foreach (var item in LoadTextureWithCoroutineCallbacks(materialMapperContext, TextureType.Diffuse, textureValue, CheckTextureOffsetAndScalingCoroutine, ApplyDiffuseMapTexture))
            {
                yield return item;
            }
        }

        /// <summary>
        /// Checks and loads the displacement texture.
        /// </summary>
        /// <param name="materialMapperContext">The material mapping context.</param>
        /// <returns>An enumerable of coroutine steps.</returns>
        protected virtual IEnumerable CheckDisplacementTexture(MaterialMapperContext materialMapperContext)
        {
            var displacementMapTextureName = materialMapperContext.Material.GetGenericPropertyName(GenericMaterialProperty.DisplacementMap);
            var textureValue = materialMapperContext.Material.GetTextureValue(displacementMapTextureName);
            foreach (var item in LoadTextureWithCoroutineCallbacks(materialMapperContext, TextureType.Displacement, textureValue, CheckTextureOffsetAndScalingCoroutine, ApplyDisplacementTexture))
            {
                yield return item;
            }
        }

        /// <summary>
        /// Checks and applies the emission color to the material.
        /// </summary>
        /// <param name="materialMapperContext">The material mapping context.</param>
        /// <returns>An enumerable of coroutine steps.</returns>
        protected virtual IEnumerable CheckEmissionColor(MaterialMapperContext materialMapperContext)
        {
            var value = materialMapperContext.Material.GetGenericColorValueMultiplied(GenericMaterialProperty.EmissionColor, materialMapperContext);
            materialMapperContext.VirtualMaterial.SetProperty(GetMappedProperty(GenericMaterialProperty.EmissionColor), materialMapperContext.Context.Options.ApplyGammaCurveToMaterialColors ? value.gamma : value);
            ApplyKeywords(materialMapperContext, GenericMaterialProperty.EmissionColor);
            yield break;
        }

        /// <summary>
        /// Checks and loads the emission map texture.
        /// </summary>
        /// <param name="materialMapperContext">The material mapping context.</param>
        /// <returns>An enumerable of coroutine steps.</returns>
        protected virtual IEnumerable CheckEmissionMapTexture(MaterialMapperContext materialMapperContext)
        {
            var emissionTexturePropertyName = materialMapperContext.Material.GetGenericPropertyName(GenericMaterialProperty.EmissionMap);
            var textureValue = materialMapperContext.Material.GetTextureValue(emissionTexturePropertyName);
            foreach (var item in LoadTextureWithCoroutineCallbacks(materialMapperContext, TextureType.Emission, textureValue, CheckTextureOffsetAndScalingCoroutine, ApplyEmissionMapTexture))
            {
                yield return item;
            }
        }

        /// <summary>
        /// Checks and loads the glossiness or roughness map texture.
        /// </summary>
        /// <param name="materialMapperContext">The material mapping context.</param>
        /// <returns>An enumerable of coroutine steps.</returns>
        protected virtual IEnumerable CheckGlossinessMapTexture(MaterialMapperContext materialMapperContext)
        {
            var auxiliaryMapTextureName = materialMapperContext.Material.GetGenericPropertyName(GenericMaterialProperty.GlossinessOrRoughnessMap);
            var textureValue = materialMapperContext.Material.GetTextureValue(auxiliaryMapTextureName);
            foreach (var item in LoadTextureWithCoroutineCallbacks(materialMapperContext, TextureType.GlossinessOrRoughness, textureValue, CheckTextureOffsetAndScalingCoroutine, ApplyGlossinessMapTexture))
            {
                yield return item;
            }
        }

        /// <summary>
        /// Checks and applies the glossiness or roughness value to the material.
        /// </summary>
        /// <param name="materialMapperContext">The material mapping context.</param>
        /// <returns>An enumerable of coroutine steps.</returns>
        protected virtual IEnumerable CheckGlossinessValue(MaterialMapperContext materialMapperContext)
        {
            var value = materialMapperContext.Material.GetGenericFloatValueMultiplied(GenericMaterialProperty.GlossinessOrRoughness, materialMapperContext);
            materialMapperContext.VirtualMaterial.SetProperty(GetMappedProperty(GenericMaterialProperty.GlossinessOrRoughness), value);
            ApplyKeywords(materialMapperContext, GenericMaterialProperty.GlossinessOrRoughness);
            yield break;
        }

        /// <summary>
        /// Checks and loads the metallic gloss map texture.
        /// </summary>
        /// <param name="materialMapperContext">The material mapping context.</param>
        /// <returns>An enumerable of coroutine steps.</returns>
        protected virtual IEnumerable CheckMetallicGlossMapTexture(MaterialMapperContext materialMapperContext)
        {
            var metallicGlossMapTextureName = materialMapperContext.Material.GetGenericPropertyName(GenericMaterialProperty.MetallicMap);
            var textureValue = materialMapperContext.Material.GetTextureValue(metallicGlossMapTextureName);
            foreach (var item in LoadTextureWithCoroutineCallbacks(materialMapperContext, TextureType.Metalness, textureValue, CheckTextureOffsetAndScalingCoroutine, ApplyMetallicGlossMapTexture))
            {
                yield return item;
            }
        }

        /// <summary>
        /// Checks and applies the metallic value to the material.
        /// </summary>
        /// <param name="materialMapperContext">The material mapping context.</param>
        /// <returns>An enumerable of coroutine steps.</returns>
        protected virtual IEnumerable CheckMetallicValue(MaterialMapperContext materialMapperContext)
        {
            var value = materialMapperContext.Material.GetGenericFloatValueMultiplied(GenericMaterialProperty.Metallic, materialMapperContext);
            materialMapperContext.VirtualMaterial.SetProperty(GetMappedProperty(GenericMaterialProperty.Metallic), value);
            ApplyKeywords(materialMapperContext, GenericMaterialProperty.Metallic);
            yield break;
        }

        /// <summary>
        /// Checks and loads the normal map texture.
        /// </summary>
        /// <param name="materialMapperContext">The material mapping context.</param>
        /// <returns>An enumerable of coroutine steps.</returns>
        protected virtual IEnumerable CheckNormalMapTexture(MaterialMapperContext materialMapperContext)
        {
            var normalMapTexturePropertyName = materialMapperContext.Material.GetGenericPropertyName(GenericMaterialProperty.NormalMap);
            var textureValue = materialMapperContext.Material.GetTextureValue(normalMapTexturePropertyName);
            foreach (var item in LoadTextureWithCoroutineCallbacks(materialMapperContext, TextureType.NormalMap, textureValue, CheckTextureOffsetAndScalingCoroutine, ApplyNormalMapTexture))
            {
                yield return item;
            }
        }

        /// <summary>
        /// Checks and loads the occlusion map texture.
        /// </summary>
        /// <param name="materialMapperContext">The material mapping context.</param>
        /// <returns>An enumerable of coroutine steps.</returns>
        protected virtual IEnumerable CheckOcclusionMapTexture(MaterialMapperContext materialMapperContext)
        {
            var occlusionMapTextureName = materialMapperContext.Material.GetGenericPropertyName(GenericMaterialProperty.OcclusionMap);
            var textureValue = materialMapperContext.Material.GetTextureValue(occlusionMapTextureName);
            foreach (var item in LoadTextureWithCoroutineCallbacks(materialMapperContext, TextureType.Occlusion, textureValue, CheckTextureOffsetAndScalingCoroutine, ApplyOcclusionMapTexture))
            {
                yield return item;
            }
        }

        /// <summary>
        /// Checks and loads the specular map texture.
        /// </summary>
        /// <param name="materialMapperContext">The material mapping context.</param>
        /// <returns>An enumerable of coroutine steps.</returns>
        protected virtual IEnumerable CheckSpecularMapTexture(MaterialMapperContext materialMapperContext)
        {
            var specularTexturePropertyName = materialMapperContext.Material.GetGenericPropertyName(GenericMaterialProperty.SpecularMap);
            var textureValue = materialMapperContext.Material.GetTextureValue(specularTexturePropertyName);
            foreach (var item in LoadTextureWithCoroutineCallbacks(materialMapperContext, TextureType.Specular, textureValue, CheckTextureOffsetAndScalingCoroutine, ApplySpecularMapTexture))
            {
                yield return item;
            }
        }

        /// <summary>
        /// Checks and loads the transparency map texture.
        /// </summary>
        /// <param name="materialMapperContext">The material mapping context.</param>
        /// <returns>An enumerable of coroutine steps.</returns>
        protected virtual IEnumerable CheckTransparencyMapTexture(MaterialMapperContext materialMapperContext)
        {
            materialMapperContext.VirtualMaterial.HasAlpha |= materialMapperContext.Material.UsesAlpha;
            var transparencyTexturePropertyName = materialMapperContext.Material.GetGenericPropertyName(GenericMaterialProperty.TransparencyMap);
            var textureValue = materialMapperContext.Material.GetTextureValue(transparencyTexturePropertyName);
            foreach (var item in LoadTextureWithCoroutineCallbacks(materialMapperContext, TextureType.Transparency, textureValue, CheckTextureOffsetAndScalingCoroutine, ApplyTransparencyMapTexture))
            {
                yield return item;
            }
        }

        /// <summary>
        /// Gets the mapped shader property name for a generic material property.
        /// </summary>
        /// <param name="genericMaterialProperty">The generic material property.</param>
        /// <returns>The mapped shader property name.</returns>
        protected virtual string GetMappedProperty(GenericMaterialProperty genericMaterialProperty)
        {
            var finalIndex = GenericPropertyIndices[(int)genericMaterialProperty];
            if (finalIndex < 0)
            {
                throw new Exception("Unused material property requested.");
            }
            return _values[finalIndex];
        }

        /// <summary>
        /// Gets the material preset, falling back to the default preset if none is specified.
        /// </summary>
        /// <param name="materialPreset">The material preset to use.</param>
        /// <returns>The selected material preset.</returns>
        protected virtual Material GetMaterialPreset(Material materialPreset)
        {
            if (materialPreset == null)
            {
                if (MaterialPreset == null)
                {
                    throw new Exception("Customizable Material Mapper Material Preset not set.");
                }
                return MaterialPreset;
            }
            return materialPreset;
        }
    }
}
