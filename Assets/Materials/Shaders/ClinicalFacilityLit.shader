Shader "ClinicalFacility/FacilityToon"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (0.8, 0.82, 0.85, 1)

        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Float) = 1.0

        [Header(Toon Diffuse)]
        _ShadowColor ("Shadow Color", Color) = (0.35, 0.38, 0.32, 1)
        _ShadowThreshold ("Shadow Threshold", Range(0, 3)) = 0.45
        _ShadowSoftness ("Shadow Edge Softness", Range(0.01, 4)) = 1.0

        [Header(Specular Highlight)]
        _SpecColor ("Specular Color", Color) = (1.0, 0.98, 0.85, 1)
        _SpecExponent ("Specular Tightness", Range(4, 512)) = 120
        _SpecThreshold ("Specular Threshold", Range(0, 1)) = 0.5
        _SpecSoftness ("Specular Edge Softness", Range(0.01, 4)) = 1.0
        _SpecIntensity ("Specular Intensity", Range(0, 4)) = 1.5

        [Header(Rim)]
        _RimColor ("Rim Color", Color) = (0.9, 0.95, 0.7, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _RimThreshold ("Rim Threshold", Range(0, 1)) = 0.55
        _RimSoftness ("Rim Edge Softness", Range(0.01, 4)) = 1.5
        _RimIntensity ("Rim Intensity", Range(0, 2)) = 0.35

        [Header(Fill)]
        _AmbientIntensity ("Ambient Intensity", Range(0, 2)) = 0.6

        [Header(Surface Detail)]
        _GrimeAmount ("Grime Amount", Range(0, 0.3)) = 0.04
        _GrimeScale ("Grime Scale", Range(0.05, 5)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "ClinicalLightingCore.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                half3  tangentWS  : TEXCOORD3;
                half3  bitangentWS: TEXCOORD4;
                float4 shadowCoord: TEXCOORD5;
            };

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _BumpScale;

                half4 _ShadowColor;
                half _ShadowThreshold;
                half _ShadowSoftness;

                half4 _SpecColor;
                half _SpecExponent;
                half _SpecThreshold;
                half _SpecSoftness;
                half _SpecIntensity;

                half4 _RimColor;
                half _RimPower;
                half _RimThreshold;
                half _RimSoftness;
                half _RimIntensity;

                half _AmbientIntensity;
                half _GrimeAmount;
                half _GrimeScale;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS = normInputs.normalWS;
                OUT.tangentWS = normInputs.tangentWS;
                OUT.bitangentWS = normInputs.bitangentWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.shadowCoord = GetShadowCoord(posInputs);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);
                half3x3 tangentToWorld = half3x3(IN.tangentWS, IN.bitangentWS, IN.normalWS);
                half3 normalWS = normalize(mul(normalTS, tangentToWorld));

                half3 viewDirWS = normalize(GetCameraPositionWS() - IN.positionWS);

                half3 lit = EvaluateClinicalToonLighting(
                    IN.positionWS,
                    normalWS,
                    viewDirWS,
                    baseSample.rgb,
                    _SpecExponent,
                    _ShadowColor.rgb,
                    _ShadowThreshold,
                    _ShadowSoftness,
                    _SpecColor.rgb,
                    _SpecThreshold,
                    _SpecSoftness,
                    _SpecIntensity,
                    _RimColor.rgb,
                    _RimPower,
                    _RimThreshold,
                    _RimSoftness,
                    _RimIntensity,
                    _AmbientIntensity,
                    _GrimeAmount,
                    _GrimeScale,
                    IN.shadowCoord);

                lit = MixFog(lit, ComputeFogFactor(TransformWorldToHClip(IN.positionWS).z));

                return half4(lit, baseSample.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
