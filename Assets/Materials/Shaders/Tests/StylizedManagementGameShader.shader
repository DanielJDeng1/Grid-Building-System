Shader "Custom/StylizedManagementGameShader"
{
    Properties
    {
        [Header(Base)]
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)

        [Header(Vivid Color Ramp)]
        _ShadowColor("Shadow Tone", Color) = (0.28,0.16,0.42,1)
        _MidColor("Mid Tone", Color) = (0.95,0.45,0.55,1)
        _HighlightColor("Highlight Tone (push above 1 for glow)", Color) = (1.5,1.25,0.9,1)
        _ShadowToMidPoint("Shadow to Mid Point", Range(0,1)) = 0.35
        _MidToHighlightPoint("Mid to Highlight Point", Range(0,1)) = 0.75
        _RampSoftness("Ramp Edge Softness", Range(0.001,0.5)) = 0.08

        [Header(Sparkle Highlight)]
        _SparkleColor("Sparkle Color (push above 1 for glow)", Color) = (2.2,2.0,1.6,1)
        _SparkleSize("Sparkle Size", Range(0,1)) = 0.9
        _SparkleSoftness("Sparkle Edge Softness", Range(0.001,0.3)) = 0.05

        [Header(Rim Glow)]
        _RimColor("Rim Color (push above 1 for glow)", Color) = (1.4,0.7,1.1,1)
        _RimPower("Rim Power", Range(0.5,8)) = 2.5
        _RimIntensity("Rim Intensity", Range(0,3)) = 1.5

        [Header(Final Punch)]
        _Saturation("Saturation Multiplier", Range(1,2.5)) = 1.5
        _Contrast("Contrast Multiplier", Range(1,2)) = 1.2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _ShadowColor;
                float4 _MidColor;
                float4 _HighlightColor;
                float _ShadowToMidPoint;
                float _MidToHighlightPoint;
                float _RampSoftness;
                float4 _SparkleColor;
                float _SparkleSize;
                float _SparkleSoftness;
                float4 _RimColor;
                float _RimPower;
                float _RimIntensity;
                float _Saturation;
                float _Contrast;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float4 shadowCoord: TEXCOORD3;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS = normInputs.normalWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.shadowCoord = GetShadowCoord(posInputs);
                return OUT;
            }

            float3 ColorRamp(float ndotl)
            {
                float t1 = smoothstep(_ShadowToMidPoint - _RampSoftness, _ShadowToMidPoint + _RampSoftness, ndotl);
                float3 shadowToMid = lerp(_ShadowColor.rgb, _MidColor.rgb, t1);

                float t2 = smoothstep(_MidToHighlightPoint - _RampSoftness, _MidToHighlightPoint + _RampSoftness, ndotl);
                return lerp(shadowToMid, _HighlightColor.rgb, t2);
            }

            float3 Sparkle(float3 normalWS, float3 viewDirWS, float3 lightDirWS, float ndotl)
            {
                float3 halfVec = normalize(lightDirWS + viewDirWS);
                float ndoth = saturate(dot(normalWS, halfVec));
                float mask = smoothstep(_SparkleSize - _SparkleSoftness, _SparkleSize + _SparkleSoftness, ndoth);
                return _SparkleColor.rgb * mask * step(0.001, ndotl);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                float3 albedo = baseTex.rgb * _BaseColor.rgb;

                InputData inputData;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = normalize(IN.normalWS);
                inputData.viewDirectionWS = normalize(GetWorldSpaceViewDir(IN.positionWS));
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowCoord = IN.shadowCoord;

                float3 tonedDiffuse = 0;
                float3 sparkle = 0;

                // ---- Main light ----
                Light mainLight = GetMainLight(inputData.shadowCoord);
                {
                    float atten = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                    float ndotl = saturate(dot(inputData.normalWS, mainLight.direction));
                    tonedDiffuse += mainLight.color * ColorRamp(ndotl) * atten;
                    sparkle += mainLight.color * Sparkle(inputData.normalWS, inputData.viewDirectionWS, mainLight.direction, ndotl) * atten;
                }

                // ---- Additional lights ----
                #if defined(_ADDITIONAL_LIGHTS)
                uint additionalLightsCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(additionalLightsCount)
                    Light light = GetAdditionalLight(lightIndex, inputData.positionWS);
                    float atten = light.distanceAttenuation * light.shadowAttenuation;
                    float ndotl = saturate(dot(inputData.normalWS, light.direction));
                    tonedDiffuse += light.color * ColorRamp(ndotl) * atten;
                    sparkle += light.color * Sparkle(inputData.normalWS, inputData.viewDirectionWS, light.direction, ndotl) * atten;
                LIGHT_LOOP_END
                #endif

                // ---- Rim glow ----
                float fresnel = pow(1.0 - saturate(dot(inputData.normalWS, inputData.viewDirectionWS)), _RimPower);
                float3 rim = _RimColor.rgb * fresnel * _RimIntensity;

                float3 color = albedo * tonedDiffuse + sparkle + rim;

                // ---- Final punch: saturation + contrast ----
                float luma = dot(color, float3(0.2126, 0.7152, 0.0722));
                color = lerp(float3(luma, luma, luma), color, _Saturation);
                color = (color - 0.5) * _Contrast + 0.5;
                color = max(color, 0);

                return half4(color, baseTex.a * _BaseColor.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_shadowcaster

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));

                #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.positionCS = positionCS;
                return OUT;
            }

            half4 ShadowFrag(Varyings IN) : SV_TARGET
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}