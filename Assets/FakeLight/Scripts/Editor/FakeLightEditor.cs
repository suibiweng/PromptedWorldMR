using UnityEditor;
using UnityEngine;

namespace FakeLightSystem
{
    [CustomEditor(typeof(FakeLight))]
    [CanEditMultipleObjects]
    public class FakeLightEditor : Editor
    {
        private FakeLight FakeLightScript;

        private SerializedProperty InheritFromLightProperty;

        private SerializedProperty LightTypeProperty;
        private SerializedProperty LightColorProperty;
        private SerializedProperty LightIntensityProperty;
        private SerializedProperty LightIntensityMultiplierProperty;

        private SerializedProperty LightAngleProperty;
        private SerializedProperty LightRangeProperty;
        private SerializedProperty LightRangeMultiplierProperty;

        private SerializedProperty LightDistanceAttenuationProperty;
        private SerializedProperty LightSideSoftnessProperty;
        private SerializedProperty LightConeSharpnessProperty;
        private SerializedProperty LightTipRadiusProperty;
        private SerializedProperty LightFrontGlareIntensityProperty;
        private SerializedProperty LightRearGlareIntensityProperty;
        private SerializedProperty LightPositionalOffsetProperty;
        private SerializedProperty LightRotationalOffsetProperty;

        private SerializedProperty LightFogEnabledProperty;
        private SerializedProperty LightFogTilingProperty;
        private SerializedProperty LightFogSpeedProperty;
        private SerializedProperty LightFogIntensityProperty;

        private SerializedProperty LightNoiseEnabledProperty;
        private SerializedProperty LightNoiseTilingProperty;
        private SerializedProperty LightNoiseSpeedProperty;
        private SerializedProperty LightNoiseIntensityProperty;

        private SerializedProperty LightGeometryFadeEnabledProperty;
        private SerializedProperty LightGeometryFadeIntensityProperty;

        private SerializedProperty LightCameraFadeEnabledProperty;
        private SerializedProperty LightCameraFadeIntensityProperty;

        private void OnEnable()
        {
            FakeLightScript = (FakeLight)target;

            InheritFromLightProperty = serializedObject.FindProperty("InheritFromLight");

            LightTypeProperty = serializedObject.FindProperty("LightType");
            LightColorProperty = serializedObject.FindProperty("LightColor");
            LightIntensityProperty = serializedObject.FindProperty("LightIntensity");
            LightIntensityMultiplierProperty = serializedObject.FindProperty("LightIntensityMultiplier");

            LightAngleProperty = serializedObject.FindProperty("LightAngle");
            LightRangeProperty = serializedObject.FindProperty("LightRange");
            LightRangeMultiplierProperty = serializedObject.FindProperty("LightRangeMultiplier");

            LightDistanceAttenuationProperty = serializedObject.FindProperty("LightDistanceAttenuation");
            LightSideSoftnessProperty = serializedObject.FindProperty("LightSideSoftness");
            LightConeSharpnessProperty = serializedObject.FindProperty("LightConeSharpness");
            LightTipRadiusProperty = serializedObject.FindProperty("LightTipRadius");
            LightFrontGlareIntensityProperty = serializedObject.FindProperty("LightFrontGlareIntensity");
            LightRearGlareIntensityProperty = serializedObject.FindProperty("LightRearGlareIntensity");
            LightPositionalOffsetProperty = serializedObject.FindProperty("LightPositionalOffset");
            LightRotationalOffsetProperty = serializedObject.FindProperty("LightRotationalOffset");

            LightFogEnabledProperty = serializedObject.FindProperty("LightFogEnabled");
            LightFogTilingProperty = serializedObject.FindProperty("LightFogTiling");
            LightFogSpeedProperty = serializedObject.FindProperty("LightFogSpeed");
            LightFogIntensityProperty = serializedObject.FindProperty("LightFogIntensity");

            LightNoiseEnabledProperty = serializedObject.FindProperty("LightNoiseEnabled");
            LightNoiseTilingProperty = serializedObject.FindProperty("LightNoiseTiling");
            LightNoiseSpeedProperty = serializedObject.FindProperty("LightNoiseSpeed");
            LightNoiseIntensityProperty = serializedObject.FindProperty("LightNoiseIntensity");

            LightGeometryFadeEnabledProperty = serializedObject.FindProperty("LightGeometryFadeEnabled");
            LightGeometryFadeIntensityProperty = serializedObject.FindProperty("LightGeometryFadeIntensity");

            LightCameraFadeEnabledProperty = serializedObject.FindProperty("LightCameraFadeEnabled");
            LightCameraFadeIntensityProperty = serializedObject.FindProperty("LightCameraFadeIntensity");
        }

        public override void OnInspectorGUI()
        {
            GUILayout.Space(10);

            Light LightComponent = FakeLightScript.GetComponent<Light>();

            EditorGUILayout.PropertyField(InheritFromLightProperty, new GUIContent("Inherit from Light"));
            GUILayout.Space(5);

            if (FakeLightScript.InheritFromLight && LightComponent == null)
            {
                if (GUILayout.Button("Add Light Component"))
                {
                    LightComponent = FakeLightScript.gameObject.AddComponent<Light>();
                }
            }

            GUILayout.Space(10);

            if (FakeLightScript.InheritFromLight && LightComponent != null)
            {
                GUILayout.Label("General", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(LightIntensityMultiplierProperty, new GUIContent("Intensity Multiplier"));

                GUILayout.Space(10);

                GUILayout.Label("Shape", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(LightRangeMultiplierProperty, new GUIContent("Range Multiplier"));
            }
            else
            {
                GUILayout.Label("General", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(LightTypeProperty, new GUIContent("Type"));

                GUILayout.Space(10);

                EditorGUILayout.PropertyField(LightColorProperty, new GUIContent("Color"));
                EditorGUILayout.PropertyField(LightIntensityProperty, new GUIContent("Intensity"));

                GUILayout.Space(10);

                GUILayout.Label("Shape", EditorStyles.boldLabel);
                if (FakeLightScript.LightType == FakeLight.LightTypeEnum.Spot)
                {
                    EditorGUILayout.PropertyField(LightAngleProperty, new GUIContent("Angle"));
                }
                EditorGUILayout.PropertyField(LightRangeProperty, new GUIContent("Range"));
            }

            GUILayout.Space(10);

            GUILayout.Label("Advanced", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(LightSideSoftnessProperty, new GUIContent("Side Softness"));
            EditorGUILayout.PropertyField(LightDistanceAttenuationProperty, new GUIContent("Distance Attenutation"));

            if (FakeLightScript.LightType == FakeLight.LightTypeEnum.Spot)
            {
                EditorGUILayout.PropertyField(LightConeSharpnessProperty, new GUIContent("Cone Sharpness"));
                EditorGUILayout.PropertyField(LightTipRadiusProperty, new GUIContent("Tip Radius"));
                GUILayout.Space(5);
                EditorGUILayout.PropertyField(LightFrontGlareIntensityProperty, new GUIContent("Front Glare Intensity"));
                EditorGUILayout.PropertyField(LightRearGlareIntensityProperty, new GUIContent("Rear Glare Intensity"));
                GUILayout.Space(5);
                EditorGUILayout.PropertyField(LightPositionalOffsetProperty, new GUIContent("Positional Offset"));
                EditorGUILayout.PropertyField(LightRotationalOffsetProperty, new GUIContent("Rotational Offset"));
            }
            else
            {
                GUILayout.Space(5);
                EditorGUILayout.PropertyField(LightPositionalOffsetProperty, new GUIContent("Positional Offset"));
            }

            GUILayout.Space(10);

            GUILayout.Label("Fog", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(LightFogEnabledProperty, new GUIContent("Enabled"));
            if (FakeLightScript.LightFogEnabled == false)
            {
                GUI.enabled = false;
            }
            EditorGUILayout.PropertyField(LightFogTilingProperty, new GUIContent("Tiling"));
            EditorGUILayout.PropertyField(LightFogSpeedProperty, new GUIContent("Speed"));
            EditorGUILayout.PropertyField(LightFogIntensityProperty, new GUIContent("Intensity"));
            GUI.enabled = true;

            GUILayout.Space(10);

            GUILayout.Label("Noise", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(LightNoiseEnabledProperty, new GUIContent("Enabled"));
            if (FakeLightScript.LightNoiseEnabled == false)
            {
                GUI.enabled = false;
            }
            EditorGUILayout.PropertyField(LightNoiseTilingProperty, new GUIContent("Tiling"));
            EditorGUILayout.PropertyField(LightNoiseSpeedProperty, new GUIContent("Speed"));
            EditorGUILayout.PropertyField(LightNoiseIntensityProperty, new GUIContent("Intensity"));
            GUI.enabled = true;

            GUILayout.Space(10);

            GUILayout.Label("Camera Fade", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(LightCameraFadeEnabledProperty, new GUIContent("Enabled"));
            if (FakeLightScript.LightCameraFadeEnabled == false)
            {
                GUI.enabled = false;
            }
            EditorGUILayout.PropertyField(LightCameraFadeIntensityProperty, new GUIContent("Intensity"));
            GUI.enabled = true;

            GUILayout.Space(10);

            GUILayout.Label("Geometry Fade", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(LightGeometryFadeEnabledProperty, new GUIContent("Enabled"));
            if (FakeLightScript.LightGeometryFadeEnabled == false)
            {
                GUI.enabled = false;
            }
            EditorGUILayout.PropertyField(LightGeometryFadeIntensityProperty, new GUIContent("Intensity"));
            GUI.enabled = true;

            serializedObject.ApplyModifiedProperties();
        }
    }
}