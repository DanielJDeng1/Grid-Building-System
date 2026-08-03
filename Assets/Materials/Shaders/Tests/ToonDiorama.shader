// =====================================================================================
// Toon Diorama Shader — "Cute Diorama with Atmospheric Contrast"
// Unity 6 (6000.x) URP — Forward+ Rendering Path
// -------------------------------------------------------------------------------------
// - 3-step toon diffuse ramp (Highlight / Midtone / Shadow), threshold + smoothness
// - Optional 1D ramp texture override
// - Color-shifted shadows (lerp toward a cool tint instead of going black)
// - HDR fresnel rim light, tuned for readability against dark floors
// - Toon (stepped) specular highlight
// - Full main light + additional light (point/spot) support via Forward+ light loop
// - Emission map with intensity multiplier
// - Lightmap / SH ambient integration
// - ShadowCaster, DepthOnly, DepthNormals passes included
// =====================================================================================

Shader "Custom/ToonDiorama"
{
    Properties
    {
        [Header(Surface)]
        _BaseMap("Albedo (RGB)", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)

        [Space(10)]
        [Toggle(_NORMALMAP)] _UseNormalMap("Use Normal Map", Float) = 0
        _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Float) = 1.0

        [Header(Alpha Clipping)]
        [Toggle(_ALPHATEST_ON)] _AlphaClipToggle("Alpha Clip", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5

        [Header(Toon Ramp Diffuse Banding)]
        [Toggle(_USE_RAMP_TEXTURE)] _UseRampTexture("Use Ramp Texture Instead of Thresholds", Float) = 0
        _RampTex("Ramp Texture (1D, sampled left-to-right by NdotL)", 2D) = "white" {}
        _ShadowThreshold("Shadow / Midtone Threshold", Range(-1,1)) = 0.0
        _HighlightThreshold("Midtone / Highlight Threshold", Range(-1,1)) = 0.5
        _ToonSmoothness("Band Smoothness (0 = crisp, 1 = soft)", Range(0.001, 1.0)) = 0.05
        _HighlightColor("Highlight Tint", Color) = (1.15, 1.08, 0.92, 1)
        _MidtoneColor("Midtone Tint", Color) = (1,1,1,1)

        [Header(Shadow Color Shift)]
        _ShadowColor("Shadow Tint Color (Indigo / Teal / Violet)", Color) = (0.14, 0.12, 0.34, 1)
        _ShadowColorStrength("Shadow Tint Strength", Range(0,1)) = 0.85
        _AmbientTint("Ambient/SH Influence In Shadow", Range(0,1)) = 0.5

        [Header(Rim Light Fresnel)]
        [HDR] _RimColor("Rim Color (HDR)", Color) = (0.6, 0.9, 1.0, 1)
        _RimPower("Rim Power", Range(0.1, 10)) = 3.0
        _RimThreshold("Rim Threshold", Range(0,1)) = 0.4
        _RimIntensity("Rim Intensity", Range(0,5)) = 1.5
        [Toggle] _RimLitSideOnly("Rim Only On Lit Side", Float) = 0

        [Header(Specular Highlight Toon)]
        [Toggle(_SPECULAR_TOON)] _UseSpecular("Enable Toon Specular", Float) = 1
        _SpecularColor("Specular Color", Color) = (1,1,1,1)
        _SpecularSize("Specular Size", Range(0.001,1)) = 0.08
        _SpecularSmoothness("Specular Edge Smoothness", Range(0.001,0.5)) = 0.02

        [Header(Emission)]
        [Toggle(_EMISSION)] _UseEmission("Enable Emission", Float) = 0
        [HDR] _EmissionColor("Emission Color", Color) = (0,0,0,1)
        _EmissionMap("Emission Map", 2D) = "black" {}
        _EmissionIntensity("Emission Intensity", Range(0, 20)) = 1.0

        [Header(Surface Options)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull Mode", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "Queue" = "Geometry"
        }

        Cull [_Cull]

        // =================================================================================
        // SHARED INCLUDE BLOCK — properties, textures, helpers shared by all passes
        // =================================================================================
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

        TEXTURE2D(_BaseMap);        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap);        SAMPLER(sampler_BumpMap);
        TEXTURE2D(_RampTex);        SAMPLER(sampler_RampTex);
        TEXTURE2D(_EmissionMap);    SAMPLER(sampler_EmissionMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;

            half _BumpScale;

            half _AlphaClipToggle;
            half _Cutoff;

            half _UseRampTexture;
            half _ShadowThreshold;
            half _HighlightThreshold;
            half _ToonSmoothness;
            half4 _HighlightColor;
            half4 _MidtoneColor;

            half4 _ShadowColor;
            half  _ShadowColorStrength;
            half  _AmbientTint;

            half4 _RimColor;
            half  _RimPower;
            half  _RimThreshold;
            half  _RimIntensity;
            half  _RimLitSideOnly;

            half4 _SpecularColor;
            half  _SpecularSize;
            half  _SpecularSmoothness;

            half4 _EmissionColor;
            half  _EmissionIntensity;
            half  _UseEmission;
        CBUFFER_END

        // -------------------------------------------------------------------------------
        // Core 3-step toon ramp. Returns a color multiplier for a given NdotL response.
        // -------------------------------------------------------------------------------
        half3 ToonRamp(half ndotlAtten)
        {
            half3 rampResult;
            if (_UseRampTexture > 0.5)
            {
                half2 uv = half2(saturate(ndotlAtten * 0.5 + 0.5), 0.5);
                rampResult = SAMPLE_TEXTURE2D(_RampTex, sampler_RampTex, uv).rgb;
            }
            else
            {
                half shadowToMid = smoothstep(_ShadowThreshold - _ToonSmoothness, _ShadowThreshold + _ToonSmoothness, ndotlAtten);
                half midToHigh    = smoothstep(_HighlightThreshold - _ToonSmoothness, _HighlightThreshold + _ToonSmoothness, ndotlAtten);

                half3 band = lerp(half3(0,0,0), _MidtoneColor.rgb, shadowToMid);
                band = lerp(band, _HighlightColor.rgb, midToHigh);
                rampResult = band;
            }
            return rampResult;
        }

        // Toon (stepped) Blinn-Phong specular
        half ToonSpecular(half3 normalWS, half3 viewDirWS, half3 lightDirWS, half ndotlAtten)
        {
            half3 halfDir = normalize(lightDirWS + viewDirWS);
            half ndoth = saturate(dot(normalWS, halfDir));
            half spec = smoothstep(1.0 - _SpecularSize - _SpecularSmoothness, 1.0 - _SpecularSize + _SpecularSmoothness, ndoth);
            // Only show specular where the surface is actually lit
            spec *= step(0.001, ndotlAtten);
            return spec;
        }
        ENDHLSL

        // =================================================================================
        // PASS 1 — ForwardLit (Forward+ : Main light + per-pixel additional light loop)
        // =================================================================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5

            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _USE_RAMP_TEXTURE
            #pragma shader_feature_local _SPECULAR_TOON
            #pragma shader_feature_local _EMISSION

            // --- URP / Forward+ (clustered) keywords ---
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                half3  normalWS    : TEXCOORD2;
                half4  tangentWS   : TEXCOORD3;
                half3  viewDirWS   : TEXCOORD4;
                half   fogFactor   : TEXCOORD5;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 6);
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   nrmInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS   = nrmInputs.normalWS;
                output.tangentWS  = half4(nrmInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.viewDirWS  = GetWorldSpaceViewDir(posInputs.positionWS);
                output.uv         = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor  = ComputeFogFactor(posInputs.positionCS.z);

                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
                OUTPUT_SH(output.normalWS.xyz, output.vertexSH);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // --- Surface data ---
                half4 albedoTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 albedo = albedoTex * _BaseColor;

                #if defined(_ALPHATEST_ON)
                    clip(albedo.a - _Cutoff);
                #endif

                half3 normalWS = normalize(input.normalWS);
                #if defined(_NORMALMAP)
                    half3 tangentWS   = input.tangentWS.xyz;
                    half3 bitangentWS = cross(normalWS, tangentWS) * input.tangentWS.w;
                    half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                    normalWS = normalize(tangentWS * normalTS.x + bitangentWS * normalTS.y + normalWS * normalTS.z);
                #endif

                half3 viewDirWS = normalize(input.viewDirWS);

                // --- Baked GI (lightmap or light-probe SH) ---
                half3 bakedGI = SAMPLE_GI(input.lightmapUV, input.vertexSH, normalWS);

                // --- Shadow coordinate for the main light (works for cascade & screen space) ---
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                // --- InputData: Forward+ needs this in scope (LIGHT_LOOP_BEGIN reads
                // inputData.normalizedScreenSpaceUV to find this pixel's light tile) ---
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewDirWS;
                inputData.shadowCoord = shadowCoord;
                inputData.fogCoord = input.fogFactor;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                half3 litColor = half3(0,0,0);
                half3 specularAccum = half3(0,0,0);

                // === MAIN DIRECTIONAL LIGHT ===
                {
                    half ndotl = dot(normalWS, mainLight.direction);
                    half atten = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                    half ndotlAtten = ndotl * atten;

                    half3 ramp = ToonRamp(ndotlAtten);
                    litColor += ramp * mainLight.color;

                    #if defined(_SPECULAR_TOON)
                        specularAccum += ToonSpecular(normalWS, viewDirWS, mainLight.direction, ndotlAtten) * mainLight.color;
                    #endif
                }

                // === FORWARD+ ADDITIONAL LIGHTS (point / spot) ===
                #if defined(_ADDITIONAL_LIGHTS)
                    uint pixelLightCount = GetAdditionalLightsCount();

                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1));

                        half ndotl = dot(normalWS, light.direction);
                        half atten = light.shadowAttenuation * light.distanceAttenuation;
                        half ndotlAtten = ndotl * atten;

                        half3 ramp = ToonRamp(ndotlAtten);
                        litColor += ramp * light.color;

                        #if defined(_SPECULAR_TOON)
                            specularAccum += ToonSpecular(normalWS, viewDirWS, light.direction, ndotlAtten) * light.color;
                        #endif
                    LIGHT_LOOP_END
                #endif

                // --- Ambient contribution, blended with the shadow tint so probes still read ---
                half3 ambient = bakedGI * lerp(1.0h, _ShadowColor.rgb, _AmbientTint);
                litColor += ambient;

                // --- Apply base albedo ---
                half3 diffuse = albedo.rgb * litColor;

                // --- Shadow color shift: lerp the darkest areas toward the cool shadow tint ---
                half sceneShadowMask = saturate(1.0h - dot(litColor, half3(0.333, 0.333, 0.333)));
                diffuse = lerp(diffuse, albedo.rgb * _ShadowColor.rgb, sceneShadowMask * _ShadowColorStrength);

                // --- Toon specular ---
                diffuse += specularAccum * _SpecularColor.rgb;

                // --- Fresnel rim light ---
                half fresnel = pow(1.0h - saturate(dot(normalWS, viewDirWS)), _RimPower);
                half rimMask = smoothstep(_RimThreshold - 0.1h, _RimThreshold + 0.1h, fresnel);
                if (_RimLitSideOnly > 0.5h)
                {
                    half mainNdotL = saturate(dot(normalWS, mainLight.direction));
                    rimMask *= mainNdotL;
                }
                half3 rim = _RimColor.rgb * rimMask * _RimIntensity;

                half3 color = diffuse + rim;

                // --- Emission ---
                #if defined(_EMISSION)
                    half3 emissionTex = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb;
                    color += emissionTex * _EmissionColor.rgb * _EmissionIntensity;
                #endif

                // --- Fog ---
                color = MixFog(color, input.fogFactor);

                return half4(color, albedo.a);
            }
            ENDHLSL
        }

        // =================================================================================
        // PASS 2 — ShadowCaster
        // =================================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(input.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowPassFragment(Varyings input) : SV_TARGET
            {
                #if defined(_ALPHATEST_ON)
                    half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                    clip(alpha - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        // =================================================================================
        // PASS 3 — DepthOnly
        // =================================================================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthOnlyFragment(Varyings input) : SV_TARGET
            {
                #if defined(_ALPHATEST_ON)
                    half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                    clip(alpha - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        // =================================================================================
        // PASS 4 — DepthNormals (feeds SSAO / post effects)
        // =================================================================================
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv         : TEXCOORD0;
                float4 positionCS : SV_POSITION;
                half3  normalWS   : TEXCOORD1;
                half4  tangentWS  : TEXCOORD2;
            };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);

                VertexNormalInputs nrmInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.normalWS  = half3(nrmInputs.normalWS);
                output.tangentWS = half4(nrmInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_TARGET
            {
                #if defined(_ALPHATEST_ON)
                    half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                    clip(alpha - _Cutoff);
                #endif

                half3 normalWS = normalize(input.normalWS);
                #if defined(_NORMALMAP)
                    half3 tangentWS   = input.tangentWS.xyz;
                    half3 bitangentWS = cross(normalWS, tangentWS) * input.tangentWS.w;
                    half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                    normalWS = normalize(tangentWS * normalTS.x + bitangentWS * normalTS.y + normalWS * normalTS.z);
                #endif

                return half4(NormalizeNormalPerPixel(normalWS), 0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
