using UnityEditor;
using UnityEngine;
using TriLibCore.Mappers;

namespace TriLibCore.Editor
{
    [CustomEditor(typeof(CustomizableMaterialMapper))]
    public class CustomizableMaterialMapperEditor : UnityEditor.Editor
    {
        private bool _showAdvancedPresets;

        private static readonly string[] PropertyDescriptions =
        {
            "Diffuse Color",
            "Diffuse Texture",
            "Specular Color",
            "Specular Texture",
            "NormalMap Texture",
            "NormalMap Strength",
            "Alpha (Opacity) Texture",
            "Alpha (Opacity) Value",
            "Occlusion (AO) Texture",
            "Occlusion (AO) Strength",
            "Emissive Color",
            "Emissive Texture",
            "Metallic Value",
            "Metallic Texture",
            "Smoothness/Roughness Value",
            "Smoothness/Roughness Texture",
            "Displacement (Parallax) Texture",
            "Displacement (Parallax) Strength",
            "UV Horizontal Offset Value",
            "UV Vertical Offset Value"
        };

        public override void OnInspectorGUI()
        {
            GUILayout.BeginVertical("Material Presets", "window");
            EditorGUILayout.HelpBox("Define the Material Preset this Material Mapper should use for different setups.\nIf only the 'Default' preset is defined, it will be used for all setups.", MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_materialPreset"), new GUIContent("Default", "Select the Material preset this Material Mapper will instantiate by default."));
            _showAdvancedPresets = EditorGUILayout.BeginToggleGroup("Display Optional Presets", _showAdvancedPresets);
            if (_showAdvancedPresets)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_noMetallicMaterialPreset"),
                    new GUIContent("No Metallic Texture",
                    "Material preset for a material without metallic textures."));

                EditorGUILayout.PropertyField(serializedObject.FindProperty("_cutoutMaterialPreset"),
                    new GUIContent("Cutout",
                    "Material preset for a transparent cutout material."));

                EditorGUILayout.PropertyField(serializedObject.FindProperty("_cutoutNoMetallicMaterialPreset"),
                    new GUIContent("Cutout (No Metallic Texture)",
                    "Material preset for a transparent cutout material without metallic textures."));

                EditorGUILayout.PropertyField(serializedObject.FindProperty("_transparentMaterialPreset"),
                    new GUIContent("Transparent",
                    "Material preset for a fully transparent material."));

                EditorGUILayout.PropertyField(serializedObject.FindProperty("_transparentNoMetallicMaterialPreset"),
                    new GUIContent("Transparent (No Metallic Texture)",
                    "Material preset for a transparent material without metallic textures."));

                EditorGUILayout.PropertyField(serializedObject.FindProperty("_transparentComposeMaterialPreset"),
                    new GUIContent("Transparent Compose",
                    "Material preset for a composed transparent material (alpha + cutout)."));

                EditorGUILayout.PropertyField(serializedObject.FindProperty("_transparentComposeNoMetallicMaterialPreset"),
                    new GUIContent("Transparent Compose (No Metallic Texture)",
                    "Material preset for a composed transparent material (alpha + cutout) without metallic textures."));

                EditorGUILayout.PropertyField(serializedObject.FindProperty("DisableAlpha"), new GUIContent("Disable Alpha", "Enable this toggle if you want to disable alpha material presets usage."));
            }
            EditorGUILayout.EndToggleGroup();
            GUILayout.EndVertical();
            EditorGUILayout.Space();
            GUILayout.BeginVertical("Properties Mapping", "window");
            EditorGUILayout.HelpBox("Here you can map TriLib's generic material properties to the corresponding properties in your output material.\nEnter the names of the output material properties that match each generic property (only if your material actually uses them).", MessageType.Info);
            var values = serializedObject.FindProperty("_values");
            for (var i = 0; i < CustomizableMaterialMapper.GenericPropertiesCount; i++)
            {
                var value = values.GetArrayElementAtIndex(i);
                EditorGUILayout.PropertyField(value, new GUIContent(PropertyDescriptions[i]));
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
            GUILayout.BeginVertical("Shader Variant Collection", "window");
            var useShaderVariantCollection = serializedObject.FindProperty("_useShaderVariantCollection");
            EditorGUILayout.PropertyField(useShaderVariantCollection, new GUIContent("Use Shader Variant Collection", "Enable this toggle to indicate that this mapper uses Shader Variant collections."));
            if (useShaderVariantCollection.boolValue)
            {
                EditorGUILayout.HelpBox("Here you can type the material keywords TriLib should enable when the given material properties are found.\nYou can enter as many keywords you want, separated by comma (,) where it that matches each generic property (only if your material actually uses them).", MessageType.Info);
                var keywords = serializedObject.FindProperty("_keywords");
                for (var i = 0; i < CustomizableMaterialMapper.GenericPropertiesCount; i++)
                {
                    var keyword = keywords.GetArrayElementAtIndex(i);
                    EditorGUILayout.PropertyField(keyword, new GUIContent(PropertyDescriptions[i]));
                }
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
            GUILayout.BeginVertical("Misc. Settings", "window");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_compatibleSetups"), new GUIContent("Compatible Modes", ""));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("CheckingOrder"), new GUIContent("Checking Order", "Define the checking priority for this Material Mapper here. Mappers with higher priorities will be evaluated before those with lower ones."));
            var convertMaterialTextures = serializedObject.FindProperty("_convertMaterialTextures");
            EditorGUILayout.PropertyField(convertMaterialTextures, new GUIContent("Convert Material Textures", "Indicates whether this Material Mapper does 'Metallic/Smoothness/Specular/Roughness/Emission' automatic texture creation."));
            if (!convertMaterialTextures.boolValue)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_extractMetallicAndSmoothness"), new GUIContent("Extract Metallic and Smoothness", "Enable this toggle if you want TriLib to extract Metallic/Smooth channels from model textures into new textures."));
            }
            EditorGUILayout.EndVertical();
            serializedObject.ApplyModifiedProperties();
        }
    }
}