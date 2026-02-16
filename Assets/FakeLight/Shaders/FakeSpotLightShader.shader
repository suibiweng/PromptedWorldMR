Shader "FakeLight/FakeSpotLightShader"
{
    Properties
    {
        _Color("Color",Color) = (1,1,1,1)
        _Intensity("Intensity",float) = 1
        _SideSoftness("Side Softness",Range(0,5)) = 2
        _DistanceAttenuation("Distance Attenuation",Range(0,1)) = 0.5
        _ConeSharpness("Cone Sharpness",Range(0,1)) = 0.5
        _FrontGlareIntensity("Front Glare Intensity",Range(0,1)) = 1
        _RearGlareIntensity("Rear Glare Intensity",Range(0,1)) = 0.75
        _TipCutOff("Tip Cut-Off",Range(0,1)) = 0
        _NoiseEnabled("Noise Enabled",float) = 0
        _NoiseTexture("Noise Texture",2D) = "white" {}
        _NoiseIntensity("Noise Intensity",float) = 0.2
        _NoiseSpeed("Noise Speed",float) = 50
        _FogEnabled2("Fog Enabled",float) = 0
        _FogTexture("Fog Texture",2D) = "white" {}
        _FogIntensity("Fog Intensity",float) = 0.5
        _FogSpeed("Fog Speed",Vector) = (0,-0.1,0,0)
        _CameraFadeEnabled ("Camera Fade Enabled", float) = 1
        _CameraFadeIntensity("Camera Fade", float) = 0.25
        _GeometryFadeEnabled ("Geometry Fade Enabled",float) = 0
        _GeometryFadeIntensity("Geometry Fade",float) = 0.5
        [HideInInspector]
        _TimeScale("TimeScale",float) = 1
    }

    SubShader
    {
        PackageRequirements {"com.unity.render-pipelines.high-definition"}

        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "HDRenderPipeline"}

        Pass
        {
            Cull Off
            Blend SrcAlpha One, One One
            ZTest LEqual
            ZWrite Off

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 objectPos : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                float3 coneDir : TEXCOORD3;

                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 ComputeScreenPos(float4 pos, float projectionSign)
            {
              float4 o = pos * 0.5f;
              o.xy = float2(o.x, o.y * projectionSign) + o.w;
              o.zw = pos.zw;
              return o;
            }

            v2f vert(appdata v)
            {
                v2f o;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.objectPos = v.vertex.xyz;
                o.worldPos = GetAbsolutePositionWS(TransformObjectToWorld(o.objectPos));
                o.screenPos = ComputeScreenPos(o.vertex, _ProjectionParams.x);

                float3 TipPosition = GetAbsolutePositionWS(TransformObjectToWorld(float3(0,0,0)));
                float3 EndPosition = GetAbsolutePositionWS(TransformObjectToWorld(float3(0,0,1)));

                o.coneDir = normalize(EndPosition - TipPosition);

                return o;
            }

            TEXTURE2D(_NoiseTexture);
            SAMPLER(sampler_NoiseTexture);

            TEXTURE2D(_FogTexture);
            SAMPLER(sampler_FogTexture);

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4,_Color)
                UNITY_DEFINE_INSTANCED_PROP(float,_Intensity)
                UNITY_DEFINE_INSTANCED_PROP(float,_SideSoftness)
                UNITY_DEFINE_INSTANCED_PROP(float,_DistanceAttenuation)
                UNITY_DEFINE_INSTANCED_PROP(float,_ConeSharpness)
                UNITY_DEFINE_INSTANCED_PROP(float,_FrontGlareIntensity)
                UNITY_DEFINE_INSTANCED_PROP(float,_RearGlareIntensity)
                UNITY_DEFINE_INSTANCED_PROP(float,_TipCutOff)

                UNITY_DEFINE_INSTANCED_PROP(float,_NoiseEnabled)
                UNITY_DEFINE_INSTANCED_PROP(float,_NoiseIntensity)
                UNITY_DEFINE_INSTANCED_PROP(float,_NoiseSpeed)

                UNITY_DEFINE_INSTANCED_PROP(float,_FogEnabled2)
                UNITY_DEFINE_INSTANCED_PROP(float,_FogIntensity)
                UNITY_DEFINE_INSTANCED_PROP(float3,_FogSpeed)

                UNITY_DEFINE_INSTANCED_PROP(float,_CameraFadeEnabled)
                UNITY_DEFINE_INSTANCED_PROP(float,_CameraFadeIntensity)

                UNITY_DEFINE_INSTANCED_PROP(float,_GeometryFadeEnabled)
                UNITY_DEFINE_INSTANCED_PROP(float,_GeometryFadeIntensity)

                UNITY_DEFINE_INSTANCED_PROP(float4,_NoiseTexture_ST)
                UNITY_DEFINE_INSTANCED_PROP(float4,_FogTexture_ST)

                UNITY_DEFINE_INSTANCED_PROP(float,_TimeScale)
            UNITY_INSTANCING_BUFFER_END(Props)

            half2 RotateUV(half2 UV, half2 Center, half Rotation)
            {
                UV -= Center;
                half Sine = sin(Rotation);
                half Cosine = cos(Rotation);

                half2x2 RotationMatrix = half2x2(Cosine, -Sine, Sine, Cosine);
                RotationMatrix *= 0.5;
                RotationMatrix += 0.5;
                RotationMatrix = RotationMatrix * 2 - 1;

                UV = mul(UV, RotationMatrix);
                UV += Center;

                return UV;
            }

            half Triplanar(float3 Position, float4 TextureST)
            {
                half SampleA = SAMPLE_TEXTURE2D(_FogTexture,sampler_FogTexture, Position.zy * TextureST.xy + TextureST.zw).r;
                half SampleB = SAMPLE_TEXTURE2D(_FogTexture,sampler_FogTexture, Position.xz * TextureST.xy + TextureST.zw).r;
                half SampleC = SAMPLE_TEXTURE2D(_FogTexture,sampler_FogTexture, Position.xy * TextureST.xy + TextureST.zw).r;

                return (SampleA) + (SampleB) + (SampleC);
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                half4 Color = UNITY_ACCESS_INSTANCED_PROP(Props,_Color);
                half Intensity = UNITY_ACCESS_INSTANCED_PROP(Props,_Intensity);
                half SideSoftness = UNITY_ACCESS_INSTANCED_PROP(Props,_SideSoftness);
                half DistanceAttenuation = UNITY_ACCESS_INSTANCED_PROP(Props,_DistanceAttenuation);
                half ConeSharpness = UNITY_ACCESS_INSTANCED_PROP(Props,_ConeSharpness);
                half FrontGlareIntensity = UNITY_ACCESS_INSTANCED_PROP(Props,_FrontGlareIntensity);
                half RearGlareIntensity = UNITY_ACCESS_INSTANCED_PROP(Props,_RearGlareIntensity);
                half TipCutOff = UNITY_ACCESS_INSTANCED_PROP(Props,_TipCutOff);

                float NoiseEnabled = UNITY_ACCESS_INSTANCED_PROP(Props,_NoiseEnabled);
                half NoiseIntensity = UNITY_ACCESS_INSTANCED_PROP(Props,_NoiseIntensity);
                half NoiseSpeed = UNITY_ACCESS_INSTANCED_PROP(Props,_NoiseSpeed);

                float FogEnabled = UNITY_ACCESS_INSTANCED_PROP(Props,_FogEnabled2);
                half FogIntensity = UNITY_ACCESS_INSTANCED_PROP(Props,_FogIntensity);
                half3 FogSpeed = UNITY_ACCESS_INSTANCED_PROP(Props,_FogSpeed);

                float CameraFadeEnabled = UNITY_ACCESS_INSTANCED_PROP(Props,_CameraFadeEnabled);
                half CameraFadeIntensity = UNITY_ACCESS_INSTANCED_PROP(Props,_CameraFadeIntensity);

                float GeometryFadeEnabled = UNITY_ACCESS_INSTANCED_PROP(Props,_GeometryFadeEnabled);
                half GeometryFadeIntensity = UNITY_ACCESS_INSTANCED_PROP(Props,_GeometryFadeIntensity);

                half4 NoiseTextureST = UNITY_ACCESS_INSTANCED_PROP(Props,_NoiseTexture_ST);
                half4 FogTextureST = UNITY_ACCESS_INSTANCED_PROP(Props,_FogTexture_ST);

                half TimeScale = UNITY_ACCESS_INSTANCED_PROP(Props,_TimeScale);

                float2 ScreenUV = i.screenPos.xy / i.screenPos.w;

                float2 FlattenObjectSpaceNormal = normalize(float2(i.objectPos.x,i.objectPos.y));
                float3 ObjectSpaceNormal = normalize(float3(FlattenObjectSpaceNormal.x,FlattenObjectSpaceNormal.y,-0.5));
                float3 WorldSpaceNormal = TransformObjectToWorldNormal(ObjectSpaceNormal);
                float3 WorldViewDirection = normalize(i.worldPos - _WorldSpaceCameraPos);

                half OneMinusLinearFallOff = saturate(i.objectPos.z);
                half LinearFallOff = 1 - OneMinusLinearFallOff;
                
                half Attenuation = saturate(1 / pow((OneMinusLinearFallOff * 10) + 1, DistanceAttenuation)) * LinearFallOff;

                half Fresnel = saturate(abs(dot(WorldSpaceNormal,WorldViewDirection)));
                half ConeSharpnessAttenuation = pow(LinearFallOff, ConeSharpness) * SideSoftness;

                half SpotDirectionDot = dot(i.coneDir,WorldViewDirection);
                half SpotDirectionDotPowered = SpotDirectionDot * SpotDirectionDot * SpotDirectionDot * SpotDirectionDot * SpotDirectionDot;
                half AbsoluteSpotDirectionDotPowered = abs(SpotDirectionDotPowered);

                if (SpotDirectionDot > 0)
                {
                    AbsoluteSpotDirectionDotPowered *= RearGlareIntensity;
                }
                else
                {
                    AbsoluteSpotDirectionDotPowered *= FrontGlareIntensity;
                }

                half CutOff = 1;
                if (OneMinusLinearFallOff <= TipCutOff)
                {
                    CutOff = 0;
                }

                AbsoluteSpotDirectionDotPowered = clamp(1 - AbsoluteSpotDirectionDotPowered,0.25,99999);

                half Noise = 1;
                if (NoiseEnabled >= 0.99)
                {
                    half2 NoiseUV = half2(ScreenUV.x * (_ScreenParams.x / _ScreenParams.y), ScreenUV.y) * NoiseTextureST.xy + NoiseTextureST.zw;
                    Noise = lerp(1, pow(SAMPLE_TEXTURE2D(_NoiseTexture,sampler_NoiseTexture, RotateUV(NoiseUV, half2(3, 3), round(_Time.y / TimeScale * NoiseSpeed))).r, 0.2), NoiseIntensity);
                }

                half Fog = 1;
                if (FogEnabled >= 0.99)
                {
                    Fog = lerp(1.0,Triplanar(i.worldPos + (_Time.y * -FogSpeed),FogTextureST),FogIntensity);
                }

                half GeometryFade = 1;
                if (GeometryFadeEnabled >= 0.99)
                {
                    float GeometryDepth = LinearEyeDepth(SampleCameraDepth(ScreenUV),_ZBufferParams);

                    GeometryFade = sqrt(saturate((GeometryDepth - i.screenPos.w) / GeometryFadeIntensity));
                }
                
                half CameraFade = 1;
                if (CameraFadeEnabled >= 0.99)
                {
                    CameraFade = saturate((i.screenPos.w - _ProjectionParams.y) / CameraFadeIntensity);
                }

                return Color * Intensity * (Attenuation * saturate(pow(Fresnel,ConeSharpnessAttenuation * AbsoluteSpotDirectionDotPowered)) * CutOff) * GeometryFade * CameraFade * Noise * Fog;
            }
            ENDHLSL
        }
    }


    SubShader
    {
        PackageRequirements {"com.unity.render-pipelines.universal"}

        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline"}

        Pass
        {
            Cull Off
            Blend SrcAlpha One, One One
            ZTest LEqual
            ZWrite Off

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 objectPos : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                float3 coneDir : TEXCOORD3;

                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.objectPos = v.vertex.xyz;
                o.worldPos = TransformObjectToWorld(o.objectPos);
                o.screenPos = ComputeScreenPos(o.vertex);

                float3 TipPosition = TransformObjectToWorld(float3(0,0,0));
                float3 EndPosition = TransformObjectToWorld(float3(0,0,1));

                o.coneDir = normalize(EndPosition - TipPosition);

                return o;
            }

            TEXTURE2D(_NoiseTexture);
            SAMPLER(sampler_NoiseTexture);

            TEXTURE2D(_FogTexture);
            SAMPLER(sampler_FogTexture);

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4,_Color)
                UNITY_DEFINE_INSTANCED_PROP(float,_Intensity)
                UNITY_DEFINE_INSTANCED_PROP(float,_SideSoftness)
                UNITY_DEFINE_INSTANCED_PROP(float,_DistanceAttenuation)
                UNITY_DEFINE_INSTANCED_PROP(float,_ConeSharpness)
                UNITY_DEFINE_INSTANCED_PROP(float,_FrontGlareIntensity)
                UNITY_DEFINE_INSTANCED_PROP(float,_RearGlareIntensity)
                UNITY_DEFINE_INSTANCED_PROP(float,_TipCutOff)

                UNITY_DEFINE_INSTANCED_PROP(float,_NoiseEnabled)
                UNITY_DEFINE_INSTANCED_PROP(float,_NoiseIntensity)
                UNITY_DEFINE_INSTANCED_PROP(float,_NoiseSpeed)

                UNITY_DEFINE_INSTANCED_PROP(float,_FogEnabled2)
                UNITY_DEFINE_INSTANCED_PROP(float,_FogIntensity)
                UNITY_DEFINE_INSTANCED_PROP(float3,_FogSpeed)

                UNITY_DEFINE_INSTANCED_PROP(float,_CameraFadeEnabled)
                UNITY_DEFINE_INSTANCED_PROP(float,_CameraFadeIntensity)

                UNITY_DEFINE_INSTANCED_PROP(float,_GeometryFadeEnabled)
                UNITY_DEFINE_INSTANCED_PROP(float,_GeometryFadeIntensity)

                UNITY_DEFINE_INSTANCED_PROP(float4,_NoiseTexture_ST)
                UNITY_DEFINE_INSTANCED_PROP(float4,_FogTexture_ST)

                UNITY_DEFINE_INSTANCED_PROP(float,_TimeScale)
            UNITY_INSTANCING_BUFFER_END(Props)

            half2 RotateUV(half2 UV, half2 Center, half Rotation)
            {
                UV -= Center;
                half Sine = sin(Rotation);
                half Cosine = cos(Rotation);

                half2x2 RotationMatrix = half2x2(Cosine, -Sine, Sine, Cosine);
                RotationMatrix *= 0.5;
                RotationMatrix += 0.5;
                RotationMatrix = RotationMatrix * 2 - 1;

                UV = mul(UV, RotationMatrix);
                UV += Center;

                return UV;
            }

            half Triplanar(float3 Position, float4 TextureST)
            {
                half SampleA = SAMPLE_TEXTURE2D(_FogTexture,sampler_FogTexture, Position.zy * TextureST.xy + TextureST.zw).r;
                half SampleB = SAMPLE_TEXTURE2D(_FogTexture,sampler_FogTexture, Position.xz * TextureST.xy + TextureST.zw).r;
                half SampleC = SAMPLE_TEXTURE2D(_FogTexture,sampler_FogTexture, Position.xy * TextureST.xy + TextureST.zw).r;

                return (SampleA) + (SampleB) + (SampleC);
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                half4 Color = UNITY_ACCESS_INSTANCED_PROP(Props,_Color);
                half Intensity = UNITY_ACCESS_INSTANCED_PROP(Props,_Intensity);
                half SideSoftness = UNITY_ACCESS_INSTANCED_PROP(Props,_SideSoftness);
                half DistanceAttenuation = UNITY_ACCESS_INSTANCED_PROP(Props,_DistanceAttenuation);
                half ConeSharpness = UNITY_ACCESS_INSTANCED_PROP(Props,_ConeSharpness);
                half FrontGlareIntensity = UNITY_ACCESS_INSTANCED_PROP(Props,_FrontGlareIntensity);
                half RearGlareIntensity = UNITY_ACCESS_INSTANCED_PROP(Props,_RearGlareIntensity);
                half TipCutOff = UNITY_ACCESS_INSTANCED_PROP(Props,_TipCutOff);

                float NoiseEnabled = UNITY_ACCESS_INSTANCED_PROP(Props,_NoiseEnabled);
                half NoiseIntensity = UNITY_ACCESS_INSTANCED_PROP(Props,_NoiseIntensity);
                half NoiseSpeed = UNITY_ACCESS_INSTANCED_PROP(Props,_NoiseSpeed);

                float FogEnabled = UNITY_ACCESS_INSTANCED_PROP(Props,_FogEnabled2);
                half FogIntensity = UNITY_ACCESS_INSTANCED_PROP(Props,_FogIntensity);
                half3 FogSpeed = UNITY_ACCESS_INSTANCED_PROP(Props,_FogSpeed);

                float CameraFadeEnabled = UNITY_ACCESS_INSTANCED_PROP(Props,_CameraFadeEnabled);
                half CameraFadeIntensity = UNITY_ACCESS_INSTANCED_PROP(Props,_CameraFadeIntensity);

                float GeometryFadeEnabled = UNITY_ACCESS_INSTANCED_PROP(Props,_GeometryFadeEnabled);
                half GeometryFadeIntensity = UNITY_ACCESS_INSTANCED_PROP(Props,_GeometryFadeIntensity);

                half4 NoiseTextureST = UNITY_ACCESS_INSTANCED_PROP(Props,_NoiseTexture_ST);
                half4 FogTextureST = UNITY_ACCESS_INSTANCED_PROP(Props,_FogTexture_ST);

                half TimeScale = UNITY_ACCESS_INSTANCED_PROP(Props,_TimeScale);

                float2 ScreenUV = i.screenPos.xy / i.screenPos.w;

                float2 FlattenObjectSpaceNormal = normalize(float2(i.objectPos.x,i.objectPos.y));
                float3 ObjectSpaceNormal = normalize(float3(FlattenObjectSpaceNormal.x,FlattenObjectSpaceNormal.y,-0.5));
                float3 WorldSpaceNormal = TransformObjectToWorldNormal(ObjectSpaceNormal);
                float3 WorldViewDirection = normalize(i.worldPos - _WorldSpaceCameraPos);

                half OneMinusLinearFallOff = saturate(i.objectPos.z);
                half LinearFallOff = 1 - OneMinusLinearFallOff;
                
                half Attenuation = saturate(1 / pow((OneMinusLinearFallOff * 10) + 1, DistanceAttenuation)) * LinearFallOff;

                half Fresnel = saturate(abs(dot(WorldSpaceNormal,WorldViewDirection)));
                half ConeSharpnessAttenuation = pow(LinearFallOff, ConeSharpness) * SideSoftness;

                half SpotDirectionDot = dot(i.coneDir,WorldViewDirection);
                half SpotDirectionDotPowered = SpotDirectionDot * SpotDirectionDot * SpotDirectionDot * SpotDirectionDot * SpotDirectionDot;
                half AbsoluteSpotDirectionDotPowered = abs(SpotDirectionDotPowered);

                if (SpotDirectionDot > 0)
                {
                    AbsoluteSpotDirectionDotPowered *= RearGlareIntensity;
                }
                else
                {
                    AbsoluteSpotDirectionDotPowered *= FrontGlareIntensity;
                }

                half CutOff = 1;
                if (OneMinusLinearFallOff <= TipCutOff)
                {
                    CutOff = 0;
                }

                AbsoluteSpotDirectionDotPowered = clamp(1 - AbsoluteSpotDirectionDotPowered,0.25,99999);

                half Noise = 1;
                if (NoiseEnabled >= 0.99)
                {
                    half2 NoiseUV = half2(ScreenUV.x * (_ScreenParams.x / _ScreenParams.y), ScreenUV.y) * NoiseTextureST.xy + NoiseTextureST.zw;
                    Noise = lerp(1, pow(SAMPLE_TEXTURE2D(_NoiseTexture,sampler_NoiseTexture, RotateUV(NoiseUV, half2(3, 3), round(_Time.y / TimeScale * NoiseSpeed))).r, 0.2), NoiseIntensity);
                }

                half Fog = 1;
                if (FogEnabled >= 0.99)
                {
                    Fog = lerp(1.0,Triplanar(i.worldPos + (_Time.y * -FogSpeed),FogTextureST),FogIntensity);
                }

                half GeometryFade = 1;
                if (GeometryFadeEnabled >= 0.99)
                {
                    float GeometryDepth = LinearEyeDepth(SampleSceneDepth(ScreenUV),_ZBufferParams);

                    GeometryFade = sqrt(saturate((GeometryDepth - i.screenPos.w) / GeometryFadeIntensity));
                }
                
                half CameraFade = 1;
                if (CameraFadeEnabled >= 0.99)
                {
                    CameraFade = saturate((i.screenPos.w - _ProjectionParams.y) / CameraFadeIntensity);
                }

                return Color * Intensity * (Attenuation * saturate(pow(Fresnel,ConeSharpnessAttenuation * AbsoluteSpotDirectionDotPowered)) * CutOff) * GeometryFade * CameraFade * Noise * Fog;
            }
            ENDHLSL
        }
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent"}

        Pass
        {
            Cull Off
            Blend SrcAlpha One, One One
            ZTest LEqual
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 objectPos : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                float3 coneDir : TEXCOORD3;

                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.objectPos = v.vertex.xyz;
                o.worldPos = mul(UNITY_MATRIX_M,float4(v.vertex.xyz,1.0)).xyz;
                o.screenPos = ComputeScreenPos(o.vertex);

                float3 TipPosition = mul(UNITY_MATRIX_M,float4(0,0,0,0)).xyz;
                float3 EndPosition = mul(UNITY_MATRIX_M,float4(0,0,1,0)).xyz;

                o.coneDir = normalize(EndPosition - TipPosition);

                return o;
            }

            sampler2D _NoiseTexture;
            sampler2D _FogTexture;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4,_Color)
                UNITY_DEFINE_INSTANCED_PROP(float,_Intensity)
                UNITY_DEFINE_INSTANCED_PROP(float,_SideSoftness)
                UNITY_DEFINE_INSTANCED_PROP(float,_DistanceAttenuation)
                UNITY_DEFINE_INSTANCED_PROP(float,_ConeSharpness)
                UNITY_DEFINE_INSTANCED_PROP(float,_FrontGlareIntensity)
                UNITY_DEFINE_INSTANCED_PROP(float,_RearGlareIntensity)
                UNITY_DEFINE_INSTANCED_PROP(float,_TipCutOff)

                UNITY_DEFINE_INSTANCED_PROP(float,_NoiseEnabled)
                UNITY_DEFINE_INSTANCED_PROP(float,_NoiseIntensity)
                UNITY_DEFINE_INSTANCED_PROP(float,_NoiseSpeed)

                UNITY_DEFINE_INSTANCED_PROP(float,_FogEnabled2)
                UNITY_DEFINE_INSTANCED_PROP(float,_FogIntensity)
                UNITY_DEFINE_INSTANCED_PROP(float3,_FogSpeed)

                UNITY_DEFINE_INSTANCED_PROP(float,_CameraFadeEnabled)
                UNITY_DEFINE_INSTANCED_PROP(float,_CameraFadeIntensity)

                UNITY_DEFINE_INSTANCED_PROP(float,_GeometryFadeEnabled)
                UNITY_DEFINE_INSTANCED_PROP(float,_GeometryFadeIntensity)

                UNITY_DEFINE_INSTANCED_PROP(float4,_NoiseTexture_ST)
                UNITY_DEFINE_INSTANCED_PROP(float4,_FogTexture_ST)

                UNITY_DEFINE_INSTANCED_PROP(float,_TimeScale)
            UNITY_INSTANCING_BUFFER_END(Props)

            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            half2 RotateUV(half2 UV, half2 Center, half Rotation)
            {
                UV -= Center;
                half Sine = sin(Rotation);
                half Cosine = cos(Rotation);

                half2x2 RotationMatrix = half2x2(Cosine, -Sine, Sine, Cosine);
                RotationMatrix *= 0.5;
                RotationMatrix += 0.5;
                RotationMatrix = RotationMatrix * 2 - 1;

                UV = mul(UV, RotationMatrix);
                UV += Center;

                return UV;
            }

            half Triplanar(float3 Position, float4 TextureST)
            {
                half SampleA = tex2D(_FogTexture,Position.zy * TextureST.xy + TextureST.zw).r;
                half SampleB = tex2D(_FogTexture,Position.xz * TextureST.xy + TextureST.zw).r;
                half SampleC = tex2D(_FogTexture,Position.xy * TextureST.xy + TextureST.zw).r;

                return (SampleA) + (SampleB) + (SampleC);
            }

            float4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);

                half4 Color = UNITY_ACCESS_INSTANCED_PROP(Props,_Color);
                half Intensity = UNITY_ACCESS_INSTANCED_PROP(Props,_Intensity);
                half SideSoftness = UNITY_ACCESS_INSTANCED_PROP(Props,_SideSoftness);
                half DistanceAttenuation = UNITY_ACCESS_INSTANCED_PROP(Props,_DistanceAttenuation);
                half ConeSharpness = UNITY_ACCESS_INSTANCED_PROP(Props,_ConeSharpness);
                half FrontGlareIntensity = UNITY_ACCESS_INSTANCED_PROP(Props,_FrontGlareIntensity);
                half RearGlareIntensity = UNITY_ACCESS_INSTANCED_PROP(Props,_RearGlareIntensity);
                half TipCutOff = UNITY_ACCESS_INSTANCED_PROP(Props,_TipCutOff);

                float NoiseEnabled = UNITY_ACCESS_INSTANCED_PROP(Props,_NoiseEnabled);
                half NoiseIntensity = UNITY_ACCESS_INSTANCED_PROP(Props,_NoiseIntensity);
                half NoiseSpeed = UNITY_ACCESS_INSTANCED_PROP(Props,_NoiseSpeed);

                float FogEnabled = UNITY_ACCESS_INSTANCED_PROP(Props,_FogEnabled2);
                half FogIntensity = UNITY_ACCESS_INSTANCED_PROP(Props,_FogIntensity);
                half3 FogSpeed = UNITY_ACCESS_INSTANCED_PROP(Props,_FogSpeed);

                float CameraFadeEnabled = UNITY_ACCESS_INSTANCED_PROP(Props,_CameraFadeEnabled);
                half CameraFadeIntensity = UNITY_ACCESS_INSTANCED_PROP(Props,_CameraFadeIntensity);

                float GeometryFadeEnabled = UNITY_ACCESS_INSTANCED_PROP(Props,_GeometryFadeEnabled);
                half GeometryFadeIntensity = UNITY_ACCESS_INSTANCED_PROP(Props,_GeometryFadeIntensity);

                half4 NoiseTextureST = UNITY_ACCESS_INSTANCED_PROP(Props,_NoiseTexture_ST);
                half4 FogTextureST = UNITY_ACCESS_INSTANCED_PROP(Props,_FogTexture_ST);

                half TimeScale = UNITY_ACCESS_INSTANCED_PROP(Props,_TimeScale);

                float2 ScreenUV = i.screenPos.xy / i.screenPos.w;

                float2 FlattenObjectSpaceNormal = normalize(float2(i.objectPos.x,i.objectPos.y));
                float3 ObjectSpaceNormal = normalize(float3(FlattenObjectSpaceNormal.x,FlattenObjectSpaceNormal.y,-0.5));
                float3 WorldSpaceNormal = UnityObjectToWorldNormal(ObjectSpaceNormal);
                float3 WorldViewDirection = normalize(i.worldPos - _WorldSpaceCameraPos);

                half OneMinusLinearFallOff = saturate(i.objectPos.z);
                half LinearFallOff = 1 - OneMinusLinearFallOff;
                
                half Attenuation = saturate(1 / pow((OneMinusLinearFallOff * 10) + 1, DistanceAttenuation)) * LinearFallOff;

                half Fresnel = saturate(abs(dot(WorldSpaceNormal,WorldViewDirection)));
                half ConeSharpnessAttenuation = pow(LinearFallOff, ConeSharpness) * SideSoftness;

                half SpotDirectionDot = dot(i.coneDir,WorldViewDirection);
                half SpotDirectionDotPowered = SpotDirectionDot * SpotDirectionDot * SpotDirectionDot * SpotDirectionDot * SpotDirectionDot;
                half AbsoluteSpotDirectionDotPowered = abs(SpotDirectionDotPowered);

                if (SpotDirectionDot > 0)
                {
                    AbsoluteSpotDirectionDotPowered *= RearGlareIntensity;
                }
                else
                {
                    AbsoluteSpotDirectionDotPowered *= FrontGlareIntensity;
                }

                half CutOff = 1;
                if (OneMinusLinearFallOff <= TipCutOff)
                {
                    CutOff = 0;
                }

                AbsoluteSpotDirectionDotPowered = clamp(1 - AbsoluteSpotDirectionDotPowered,0.25,99999);

                half Noise = 1;
                if (NoiseEnabled >= 0.99)
                {
                    half2 NoiseUV = half2(ScreenUV.x * (_ScreenParams.x / _ScreenParams.y), ScreenUV.y) * NoiseTextureST.xy + NoiseTextureST.zw;
                    Noise = lerp(1, pow(tex2D(_NoiseTexture, RotateUV(NoiseUV, half2(3, 3), round(_Time.y / TimeScale * NoiseSpeed))).r, 0.2), NoiseIntensity);
                }

                half Fog = 1;
                if (FogEnabled >= 0.99)
                {
                    Fog = lerp(1.0,Triplanar(i.worldPos + (_Time.y * -FogSpeed),FogTextureST),FogIntensity);
                }

                half GeometryFade = 1;
                if (GeometryFadeEnabled >= 0.99)
                {
                    float GeometryDepth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture,ScreenUV));

                    GeometryFade = sqrt(saturate((GeometryDepth - i.screenPos.w) / GeometryFadeIntensity));
                }
                
                half CameraFade = 1;
                if (CameraFadeEnabled >= 0.99)
                {
                    CameraFade = saturate((i.screenPos.w - _ProjectionParams.y) / CameraFadeIntensity);
                }
                
                return Color * Intensity * (Attenuation * saturate(pow(Fresnel,ConeSharpnessAttenuation * AbsoluteSpotDirectionDotPowered)) * CutOff) * GeometryFade * CameraFade * Noise * Fog;
            }
            ENDHLSL
        }
    }
}
