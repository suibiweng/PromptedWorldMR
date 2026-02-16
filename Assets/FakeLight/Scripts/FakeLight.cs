using System;
using System.Collections.Generic;
using UnityEngine;

namespace FakeLightSystem
{
    [ExecuteInEditMode]
    public class FakeLight : MonoBehaviour
    {
        private static FakeLight FakeLightManager;
        private static List<FakeLight> FakeLights = new List<FakeLight>();

        private static List<FakeLight> FakeSpotLights = new List<FakeLight>();
        private static List<FakeLight> FakePointLights = new List<FakeLight>();

        private static Mesh SpotlightMesh;
        private static Material SpotlightMaterial;

        private static Mesh PointlightMesh;
        private static Material PointlightMaterial;

        private static MaterialPropertyBlock FakelightMaterialPropertyBlock;

        private static Matrix4x4[] FakeLightMatrices = new Matrix4x4[1023];
        private static Vector4[] FakeLightColors = new Vector4[1023];
        private static float[] FakeLightIntensityValues = new float[1023];
        private static float[] FakeLightDistanceAttenuationValues = new float[1023];
        private static float[] FakeLightSideSoftnessValues = new float[1023];
        private static float[] FakeLightConeSharpnessValues = new float[1023];
        private static float[] FakeLightFrontGlareIntensityValues = new float[1023];
        private static float[] FakeLightRearGlareIntensityValues = new float[1023];
        private static float[] FakeLightTipCutOffValues = new float[1023];

        private static float[] FakeLightNoiseEnabledValues = new float[1023];
        private static Vector4[] FakeLightNoiseTilingValues = new Vector4[1023];
        private static float[] FakeLightNoiseSpeedValues = new float[1023];
        private static float[] FakeLightNoiseIntensityValues = new float[1023];

        private static float[] FakeLightFogEnabledValues = new float[1023];
        private static Vector4[] FakeLightFogTilingValues = new Vector4[1023];
        private static Vector4[] FakeLightFogSpeedValues = new Vector4[1023];
        private static float[] FakeLightFogIntensityValues = new float[1023];

        private static float[] FakeLightCameraFadeEnabledValues = new float[1023];
        private static float[] FakeLightCameraFadeIntensityValues = new float[1023];

        private static float[] FakeLightGeometryFadeEnabledValues = new float[1023];
        private static float[] FakeLightGeometryFadeIntensityValues = new float[1023];

        private static float[] FakeLightTimeScaleValues = new float[1023];

        public enum LightTypeEnum
        {
            Spot,
            Point
        }

        [Tooltip("inherit properties from a light source")]
        public bool InheritFromLight = false;

        [Tooltip("type of light: spot or point")]
        public LightTypeEnum LightType;
        [Tooltip("color of light, duh")]
        public Color LightColor = Color.white;
        [Tooltip("brightness of light")]
        public float LightIntensity = 3.0f;
        [Tooltip("multiplier of intensity from light source")]
        public float LightIntensityMultiplier = 1.0f;
        [Range(1.0f, 179.0f)]
        [Tooltip("angle of spot light")]
        public float LightAngle = 90;
        [Tooltip("range of light")]
        public float LightRange = 1.0f;
        [Tooltip("multiplier of range from light source")]
        public float LightRangeMultiplier = 0.1f;
        [Tooltip("apply positional offset to light")]
        public Vector3 LightPositionalOffset = Vector3.zero;
        [Tooltip("apply rotational offset to light")]
        public Quaternion LightRotationalOffset = Quaternion.identity;

        [Tooltip("fall-off of light from distance")]
        [Range(0, 1)]
        public float LightDistanceAttenuation = 0.5f;
        [Tooltip("softness of sides/edges")]
        [Range(0.0f, 5.0f)]
        public float LightSideSoftness = 3.0f;
        [Tooltip("how much to keep a cone shape")]
        [Range(0.0f, 1.0f)]
        public float LightConeSharpness = 0.5f;
        [Tooltip("boosts brightness of light when looking from front")]
        [Range(0, 1)]
        public float LightFrontGlareIntensity = 0.5f;
        [Tooltip("boosts brightness of light when looking from behind")]
        [Range(0, 1)]
        public float LightRearGlareIntensity = 0.5f;
        [Tooltip("radius of cone tip")]
        public float LightTipRadius = 0.01f;

        [Tooltip("enable screen space noise, helps remove perfection")]
        public bool LightNoiseEnabled = false;
        [Tooltip("size of noise")]
        public float LightNoiseTiling = 1.0f;
        [Tooltip("speed of noise")]
        public float LightNoiseSpeed = 50.0f;
        [Tooltip("intensity of noise")]
        public float LightNoiseIntensity = 0.1f;

        [Tooltip("enable world fog")]
        public bool LightFogEnabled = false;
        [Tooltip("size of fog")]
        public float LightFogTiling = 0.5f;
        [Tooltip("speed of fog in 3D")]
        public Vector3 LightFogSpeed = Vector3.right * 0.1f;
        [Tooltip("intensity of fog")]
        public float LightFogIntensity = 0.75f;

        [Tooltip("enable camera fade, light fades as it intersects the camera, prevents camera clipping")]
        public bool LightCameraFadeEnabled = true;
        [Tooltip("intensity of camera fade")]
        public float LightCameraFadeIntensity = 0.1f;
        [Tooltip("enable geometry fade, light fades as it intersects opaque geometry, prevents geometry clipping (requres depth texture)")]
        public bool LightGeometryFadeEnabled = false;
        [Tooltip("intensity of geometry fade")]
        public float LightGeometryFadeIntensity = 0.5f;

        private void Update()
        {
            if (FakeLightManager == this)
            {
                int FakeLightCount = FakeLights.Count;

                FakeSpotLights.Clear();
                FakePointLights.Clear();

                for (int FakeLightIndex = 0; FakeLightIndex < FakeLightCount; FakeLightIndex++)
                {
                    FakeLight CurrentFakeLight = FakeLights[FakeLightIndex];

                    if (CurrentFakeLight.LightType == LightTypeEnum.Spot)
                    {
                        FakeSpotLights.Add(CurrentFakeLight);
                    }
                    else
                    {
                        FakePointLights.Add(CurrentFakeLight);
                    }
                }

                int FakeSpotLightCount = FakeSpotLights.Count;
                int FakePointLightCount = FakePointLights.Count;

                if (FakelightMaterialPropertyBlock == null)
                {
                    FakelightMaterialPropertyBlock = new MaterialPropertyBlock();
                }

                if (SpotlightMesh == null || SpotlightMaterial == null)
                {
                    SpotlightMesh = Resources.Load<Mesh>("ConeMesh");
                    SpotlightMaterial = Resources.Load<Material>("FakeSpotLight");
                }

                if (PointlightMesh == null || PointlightMaterial == null)
                {
                    PointlightMesh = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
                    PointlightMaterial = Resources.Load<Material>("FakePointLight");
                }

                if (FakeSpotLightCount > 0)
                {
                    if (SpotlightMesh != null && SpotlightMaterial != null && FakelightMaterialPropertyBlock != null)
                    {
                        SpotlightMaterial.SetFloat("_TimeScale", Time.timeScale);

                        for (int BatchStart = 0; BatchStart < FakeSpotLightCount; BatchStart += 1023)
                        {
                            int BatchSize = Mathf.Min(1023, FakeSpotLightCount - BatchStart);

                            for (int Index = 0; Index < BatchSize; Index++)
                            {
                                FakeLight CurrentFakeLight = FakeSpotLights[BatchStart + Index];

                                float CurrentAngle = CurrentFakeLight.LightAngle;
                                float CurrentRange = CurrentFakeLight.LightRange;
                                Color CurrentColor = CurrentFakeLight.LightColor.linear;
                                float CurrentIntensity = CurrentFakeLight.LightIntensity;

                                if (CurrentFakeLight.InheritFromLight)
                                {
                                    if (CurrentFakeLight.GetComponent<Light>() != null)
                                    {
                                        Light CurrentLight = CurrentFakeLight.GetComponent<Light>();

                                        if (CurrentLight.type == UnityEngine.LightType.Spot && CurrentFakeLight.LightType != LightTypeEnum.Spot)
                                        {
                                            CurrentFakeLight.LightType = LightTypeEnum.Spot;
                                        }

                                        if (CurrentLight.type == UnityEngine.LightType.Point && CurrentFakeLight.LightType != LightTypeEnum.Point)
                                        {
                                            CurrentFakeLight.LightType = LightTypeEnum.Point;
                                        }

                                        CurrentAngle = CurrentLight.spotAngle;
                                        CurrentRange = CurrentLight.range * CurrentFakeLight.LightRangeMultiplier;
                                        CurrentColor = CurrentLight.color.linear;

                                        if (CurrentLight.useColorTemperature)
                                        {
                                            CurrentColor *= Mathf.CorrelatedColorTemperatureToRGB(CurrentLight.colorTemperature);
                                        }

                                        CurrentIntensity = CurrentLight.intensity * CurrentFakeLight.LightIntensityMultiplier;
                                    }
                                }

                                float AngleInRadians = CurrentAngle * Mathf.Deg2Rad;
                                float Height = CurrentRange * Mathf.Cos(AngleInRadians / 2);
                                float Radius = Height * Mathf.Tan(AngleInRadians / 2);

                                Vector3 SpotLightScale = new Vector3(Radius * 2, Radius * 2, Height);

                                FakeLightMatrices[Index] = Matrix4x4.TRS(CurrentFakeLight.transform.position + CurrentFakeLight.LightPositionalOffset, CurrentFakeLight.transform.rotation * CurrentFakeLight.LightRotationalOffset, SpotLightScale);

                                FakeLightColors[Index] = CurrentColor;
                                FakeLightIntensityValues[Index] = CurrentIntensity;

                                FakeLightDistanceAttenuationValues[Index] = CurrentFakeLight.LightDistanceAttenuation;
                                FakeLightSideSoftnessValues[Index] = CurrentFakeLight.LightSideSoftness;
                                FakeLightConeSharpnessValues[Index] = CurrentFakeLight.LightConeSharpness;
                                FakeLightFrontGlareIntensityValues[Index] = CurrentFakeLight.LightFrontGlareIntensity;
                                FakeLightRearGlareIntensityValues[Index] = CurrentFakeLight.LightRearGlareIntensity;
                                FakeLightTipCutOffValues[Index] = Mathf.Clamp01(LightTipRadius / Radius);

                                FakeLightNoiseEnabledValues[Index] = CurrentFakeLight.LightNoiseEnabled ? 1.0f : 0.0f;
                                FakeLightNoiseTilingValues[Index] = new Vector4(CurrentFakeLight.LightNoiseTiling, CurrentFakeLight.LightNoiseTiling, 0, 0);
                                FakeLightNoiseSpeedValues[Index] = CurrentFakeLight.LightNoiseSpeed;
                                FakeLightNoiseIntensityValues[Index] = CurrentFakeLight.LightNoiseIntensity;

                                FakeLightFogEnabledValues[Index] = CurrentFakeLight.LightFogEnabled ? 1.0f : 0.0f;
                                FakeLightFogTilingValues[Index] = new Vector4(CurrentFakeLight.LightFogTiling, CurrentFakeLight.LightFogTiling, 0, 0);
                                FakeLightFogSpeedValues[Index] = CurrentFakeLight.LightFogSpeed;
                                FakeLightFogIntensityValues[Index] = CurrentFakeLight.LightFogIntensity;

                                FakeLightCameraFadeEnabledValues[Index] = CurrentFakeLight.LightCameraFadeEnabled ? 1.0f : 0.0f;
                                FakeLightCameraFadeIntensityValues[Index] = CurrentFakeLight.LightCameraFadeIntensity;

                                FakeLightGeometryFadeEnabledValues[Index] = CurrentFakeLight.LightGeometryFadeEnabled ? 1.0f : 0.0f;
                                FakeLightGeometryFadeIntensityValues[Index] = CurrentFakeLight.LightGeometryFadeIntensity;

                                FakeLightTimeScaleValues[Index] = Time.timeScale;
                            }

                            FakelightMaterialPropertyBlock.Clear();

                            FakelightMaterialPropertyBlock.SetVectorArray("_Color", FakeLightColors);
                            FakelightMaterialPropertyBlock.SetFloatArray("_Intensity", FakeLightIntensityValues);
                            FakelightMaterialPropertyBlock.SetFloatArray("_DistanceAttenuation", FakeLightDistanceAttenuationValues);
                            FakelightMaterialPropertyBlock.SetFloatArray("_SideSoftness", FakeLightSideSoftnessValues);
                            FakelightMaterialPropertyBlock.SetFloatArray("_ConeSharpness", FakeLightConeSharpnessValues);
                            FakelightMaterialPropertyBlock.SetFloatArray("_FrontGlareIntensity", FakeLightFrontGlareIntensityValues);
                            FakelightMaterialPropertyBlock.SetFloatArray("_RearGlareIntensity", FakeLightRearGlareIntensityValues);
                            FakelightMaterialPropertyBlock.SetFloatArray("_TipCutOff", FakeLightTipCutOffValues);

                            FakelightMaterialPropertyBlock.SetFloatArray("_NoiseEnabled", FakeLightNoiseEnabledValues);
                            FakelightMaterialPropertyBlock.SetVectorArray("_NoiseTexture_ST", FakeLightNoiseTilingValues);
                            FakelightMaterialPropertyBlock.SetFloatArray("_NoiseSpeed", FakeLightNoiseSpeedValues);
                            FakelightMaterialPropertyBlock.SetFloatArray("_NoiseIntensity", FakeLightNoiseIntensityValues);

                            FakelightMaterialPropertyBlock.SetFloatArray("_FogEnabled2", FakeLightFogEnabledValues);
                            FakelightMaterialPropertyBlock.SetVectorArray("_FogTexture_ST", FakeLightFogTilingValues);
                            FakelightMaterialPropertyBlock.SetVectorArray("_FogSpeed", FakeLightFogSpeedValues);
                            FakelightMaterialPropertyBlock.SetFloatArray("_FogIntensity", FakeLightFogIntensityValues);

                            FakelightMaterialPropertyBlock.SetFloatArray("_CameraFadeEnabled", FakeLightCameraFadeEnabledValues);
                            FakelightMaterialPropertyBlock.SetFloatArray("_CameraFadeIntensity", FakeLightCameraFadeIntensityValues);

                            FakelightMaterialPropertyBlock.SetFloatArray("_GeometryFadeEnabled", FakeLightGeometryFadeEnabledValues);
                            FakelightMaterialPropertyBlock.SetFloatArray("_GeometryFadeIntensity", FakeLightGeometryFadeIntensityValues);

                            FakelightMaterialPropertyBlock.SetFloatArray("_TimeScale", FakeLightTimeScaleValues);


#if UNITY_2021_2_OR_NEWER

                            RenderParams RenderParameters = new RenderParams(SpotlightMaterial)
                            {
                                layer = gameObject.layer,
                                matProps = FakelightMaterialPropertyBlock
                            };



                            Graphics.RenderMeshInstanced(RenderParameters, SpotlightMesh, 0, FakeLightMatrices, BatchSize, 0);
#else

                        Graphics.DrawMeshInstanced(SpotlightMesh, 0, SpotlightMaterial, FakeLightMatrices, BatchSize, FakelightMaterialPropertyBlock, UnityEngine.Rendering.ShadowCastingMode.Off, false, gameObject.layer, null, UnityEngine.Rendering.LightProbeUsage.Off);
                    
#endif
                        }

                    }
                }

                if (FakePointLightCount > 0)
                {
                    if (PointlightMesh != null && PointlightMaterial != null && FakelightMaterialPropertyBlock != null)
                    {
                        PointlightMaterial.SetFloat("_TimeScale", Time.timeScale);

                        for (int BatchStart = 0; BatchStart < FakePointLightCount; BatchStart += 1023)
                        {
                            int BatchSize = Mathf.Min(1023, FakePointLightCount - BatchStart);

                            for (int Index = 0; Index < BatchSize; Index++)
                            {
                                FakeLight CurrentFakeLight = FakePointLights[BatchStart + Index];

                                float CurrentRange = CurrentFakeLight.LightRange;
                                Color CurrentColor = CurrentFakeLight.LightColor.linear;
                                float CurrentIntensity = CurrentFakeLight.LightIntensity;

                                if (CurrentFakeLight.InheritFromLight)
                                {
                                    if (CurrentFakeLight.GetComponent<Light>() != null)
                                    {
                                        Light CurrentLight = CurrentFakeLight.GetComponent<Light>();

                                        if (CurrentLight.type == UnityEngine.LightType.Spot && CurrentFakeLight.LightType != LightTypeEnum.Spot)
                                        {
                                            CurrentFakeLight.LightType = LightTypeEnum.Spot;
                                        }

                                        if (CurrentLight.type == UnityEngine.LightType.Point && CurrentFakeLight.LightType != LightTypeEnum.Point)
                                        {
                                            CurrentFakeLight.LightType = LightTypeEnum.Point;
                                        }

                                        CurrentRange = CurrentLight.range * CurrentFakeLight.LightRangeMultiplier;
                                        CurrentColor = CurrentLight.color.linear;

                                        if (CurrentLight.useColorTemperature)
                                        {
                                            CurrentColor *= Mathf.CorrelatedColorTemperatureToRGB(CurrentLight.colorTemperature);
                                        }

                                        CurrentIntensity = CurrentLight.intensity * CurrentFakeLight.LightIntensityMultiplier;
                                    }
                                }

                                FakeLightMatrices[Index] = Matrix4x4.TRS(CurrentFakeLight.transform.position + CurrentFakeLight.LightPositionalOffset, Quaternion.identity, Vector3.one * CurrentRange * 2.0f);
                                FakeLightColors[Index] = CurrentColor;
                                FakeLightIntensityValues[Index] = CurrentIntensity;
                                FakeLightDistanceAttenuationValues[Index] = CurrentFakeLight.LightDistanceAttenuation;
                                FakeLightSideSoftnessValues[Index] = CurrentFakeLight.LightSideSoftness;

                                FakeLightNoiseEnabledValues[Index] = CurrentFakeLight.LightNoiseEnabled ? 1.0f : 0.0f;
                                FakeLightNoiseTilingValues[Index] = new Vector4(CurrentFakeLight.LightNoiseTiling, CurrentFakeLight.LightNoiseTiling, 0, 0);
                                FakeLightNoiseSpeedValues[Index] = CurrentFakeLight.LightNoiseSpeed;
                                FakeLightNoiseIntensityValues[Index] = CurrentFakeLight.LightNoiseIntensity;

                                FakeLightFogEnabledValues[Index] = CurrentFakeLight.LightFogEnabled ? 1.0f : 0.0f;
                                FakeLightFogTilingValues[Index] = new Vector4(CurrentFakeLight.LightFogTiling, CurrentFakeLight.LightFogTiling, 0, 0);
                                FakeLightFogSpeedValues[Index] = CurrentFakeLight.LightFogSpeed;
                                FakeLightFogIntensityValues[Index] = CurrentFakeLight.LightFogIntensity;

                                FakeLightCameraFadeEnabledValues[Index] = CurrentFakeLight.LightCameraFadeEnabled ? 1.0f : 0.0f;
                                FakeLightCameraFadeIntensityValues[Index] = CurrentFakeLight.LightCameraFadeIntensity;

                                FakeLightGeometryFadeEnabledValues[Index] = CurrentFakeLight.LightGeometryFadeEnabled ? 1.0f : 0.0f;
                                FakeLightGeometryFadeIntensityValues[Index] = CurrentFakeLight.LightGeometryFadeIntensity;

                                FakeLightTimeScaleValues[Index] = Time.timeScale;
                            }

                            FakelightMaterialPropertyBlock.Clear();

                            FakelightMaterialPropertyBlock.SetVectorArray("_Color", FakeLightColors);
                            FakelightMaterialPropertyBlock.SetFloatArray("_Intensity", FakeLightIntensityValues);
                            FakelightMaterialPropertyBlock.SetFloatArray("_DistanceAttenuation", FakeLightDistanceAttenuationValues);
                            FakelightMaterialPropertyBlock.SetFloatArray("_SideSoftness", FakeLightSideSoftnessValues);

                            FakelightMaterialPropertyBlock.SetFloatArray("_NoiseEnabled", FakeLightNoiseEnabledValues);
                            FakelightMaterialPropertyBlock.SetVectorArray("_NoiseTexture_ST", FakeLightNoiseTilingValues);
                            FakelightMaterialPropertyBlock.SetFloatArray("_NoiseSpeed", FakeLightNoiseSpeedValues);
                            FakelightMaterialPropertyBlock.SetFloatArray("_NoiseIntensity", FakeLightNoiseIntensityValues);

                            FakelightMaterialPropertyBlock.SetFloatArray("_FogEnabled2", FakeLightFogEnabledValues);
                            FakelightMaterialPropertyBlock.SetVectorArray("_FogTexture_ST", FakeLightFogTilingValues);
                            FakelightMaterialPropertyBlock.SetVectorArray("_FogSpeed", FakeLightFogSpeedValues);
                            FakelightMaterialPropertyBlock.SetFloatArray("_FogIntensity", FakeLightFogIntensityValues);

                            FakelightMaterialPropertyBlock.SetFloatArray("_CameraFadeEnabled", FakeLightCameraFadeEnabledValues);
                            FakelightMaterialPropertyBlock.SetFloatArray("_CameraFadeIntensity", FakeLightCameraFadeIntensityValues);

                            FakelightMaterialPropertyBlock.SetFloatArray("_GeometryFadeEnabled", FakeLightGeometryFadeEnabledValues);
                            FakelightMaterialPropertyBlock.SetFloatArray("_GeometryFadeIntensity", FakeLightGeometryFadeIntensityValues);

                            FakelightMaterialPropertyBlock.SetFloatArray("_TimeScale", FakeLightTimeScaleValues);

#if UNITY_2021_2_OR_NEWER

                            RenderParams RenderParameters = new RenderParams(PointlightMaterial)
                            {
                                layer = gameObject.layer,
                                matProps = FakelightMaterialPropertyBlock
                            };

                            Graphics.RenderMeshInstanced(RenderParameters, PointlightMesh, 0, FakeLightMatrices, BatchSize, 0);

#else

                        Graphics.DrawMeshInstanced(PointlightMesh, 0, PointlightMaterial, FakeLightMatrices, BatchSize, FakelightMaterialPropertyBlock, UnityEngine.Rendering.ShadowCastingMode.Off, false, gameObject.layer, null, UnityEngine.Rendering.LightProbeUsage.Off);
                    
#endif
                        }
                    }
                }

            }
        }

        private void OnEnable()
        {
            FakeLights.Add(this);

            if (FakeLightManager == null)
            {
                FakeLightManager = this;
            }
        }

        private void OnDisable()
        {
            if (FakeLightManager == this)
            {
                if (SpotlightMaterial != null)
                {
                    SpotlightMaterial.SetFloat("_TimeScale", 1.0f);
                }

                if (PointlightMaterial != null)
                {
                    PointlightMaterial.SetFloat("_TimeScale", 1.0f);
                }
            }

            FakeLights.Remove(this);

            if (FakeLightManager == this)
            {
                if (FakeLights.Count > 0)
                {
                    FakeLightManager = FakeLights[0];
                }
                else
                {
                    FakeLightManager = null;
                }
            }
        }
    }
}