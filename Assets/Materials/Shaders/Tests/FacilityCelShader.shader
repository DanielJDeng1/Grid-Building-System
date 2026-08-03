// FacilityCelShader
//
// One shader for both "ordinary facility geometry" and "entities that read as slightly
// wrong" - the difference is tuning, not two separate shaders. A fresh material looks like
// plain institutional dressing by default (Rim Intensity = 0); dial Rim Intensity up on
// anything that should feel subtly off. Stays fully Opaque throughout - no Surface Type
// switching, no ZWrite considerations.
//
// Lighting is stepped (toon/cel banding) with a dithered transition between steps instead of
// a hard edge, echoing a low-res monitor/CCTV read rather than a smooth PBR falloff.
Shader "Custom/FacilityCelShader"
{
    Properties
    {
        [Header(Base)]
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1, 1, 1, 1)

        [Header(Stepped Lighting)]
        [IntRange] _ShadowSteps("Shadow Steps", Range(1, 6)) = 2
        _ShadowColor("Shadow Tint", Color) = (0.55, 0.6, 0.68, 1)
        _ShadowSmoothness("Step Edge Softness", Range(0.001, 0.3)) = 0.05

        [Header(Dither)]
        _DitherScale("Dither Scale (world units)", Float) = 4.0
        _DitherStrength("Dither Strength", Range(0, 1)) = 1.0

        // Rim Intensity defaults to 0, disabling the "entity wrongness" effect.
        [Header(Rim)]
        _RimColor("Rim Color", Color) = (0.6, 1.0, 0.75, 1)
        _RimIntensity("Rim Intensity", Range(0, 5)) = 0
        _RimPower("Rim Power", Range(0.5, 8)) = 3

        [Header(Ambient)]
        _AmbientStrength("Ambient/SH Strength", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _ShadowSteps;
            half4 _ShadowColor;
            half _ShadowSmoothness;
            half _DitherScale;
            half _DitherStrength;
            half4 _RimColor;
            half _RimIntensity;
            half _RimPower;
            half _AmbientStrength;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        // Classic 4x4 Bayer matrix, normalized 0..1. Used to break a hard toon-shading step
        // edge into a dithered transition band instead of a crisp line - matches the
        // low-res/monitor read described for this project's aesthetic.
        static const half bayerMatrix[16] =
        {
             0.0/16,  8.0/16,  2.0/16, 10.0/16,
            12.0/16,  4.0/16, 14.0/16,  6.0/16,
             3.0/16, 11.0/16,  1.0/16,  9.0/16,
            15.0/16,  7.0/16, 13.0/16,  5.0/16
        };

        // World-space dither lookup (not screen-space) so the pattern is stable relative to
        // the object rather than swimming with camera movement - appropriate for a lighting
        // step edge, which should read as a fixed material property, not a screen effect.
        half WorldSpaceDither(float3 positionWS)
        {
            uint2 cell = uint2(fmod(abs(positionWS.x) * _DitherScale, 4), fmod(abs(positionWS.z) * _DitherScale, 4));
            return bayerMatrix[cell.y * 4 + cell.x];
        }

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
            float4 shadowCoord : TEXCOORD3;
            float fogFactor    : TEXCOORD4;
        };

        Varyings ForwardVert(Attributes input)
        {
            Varyings output;

            VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
            VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

            output.positionCS = positionInputs.positionCS;
            output.positionWS = positionInputs.positionWS;
            output.normalWS = normalInputs.normalWS;
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            output.shadowCoord = GetShadowCoord(positionInputs);
            output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);

            return output;
        }

        half4 ForwardFrag(Varyings input) : SV_Target
        {
            half4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
            half3 albedo = baseMap.rgb * _BaseColor.rgb;

            float3 normalWS = normalize(input.normalWS);
            float3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));

            Light mainLight = GetMainLight(input.shadowCoord);
            half NdotL = dot(normalWS, mainLight.direction);

            // Stepped lighting: quantize NdotL into _ShadowSteps bands, then dither the band
            // edge in world space instead of leaving a hard cel-shading line.
            half dither = (WorldSpaceDither(input.positionWS) - 0.5) * _DitherStrength;
            half litRatio = saturate(NdotL * 0.5 + 0.5 + dither * _ShadowSmoothness);
            half stepped = floor(litRatio * _ShadowSteps) / max(_ShadowSteps - 1, 1);
            stepped = saturate(stepped);

            half3 litColor = albedo * mainLight.color * mainLight.shadowAttenuation;
            half3 shadowedColor = albedo * _ShadowColor.rgb;
            half3 steppedLighting = lerp(shadowedColor, litColor, stepped);

            // Flat, mostly-uniform ambient rather than moody pooled ambient occlusion - keeps
            // the base facility read as clinical/bright rather than atmospheric.
            half3 ambient = SampleSH(normalWS) * albedo * _AmbientStrength;

            half3 finalColor = steppedLighting + ambient;

            // Rim highlight - defaults to invisible (_RimIntensity = 0). Dial up per-material
            // for anything that should read as subtly wrong against an otherwise mundane room.
            half fresnel = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _RimPower);
            finalColor += _RimColor.rgb * fresnel * _RimIntensity;

            finalColor = MixFog(finalColor, input.fogFactor);

            return half4(finalColor, 1.0);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex ForwardVert
            #pragma fragment ForwardFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog
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

            float3 _LightDirection;

            Varyings ShadowVert(Attributes input)
            {
                Varyings output = (Varyings)0;

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                float3 positionWS = positionInputs.positionWS;
                float3 normalWS = normalInputs.normalWS;

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            Varyings DepthVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionCS = GetVertexPositionInputs(input.positionOS.xyz).positionCS;
                return output;
            }

            half4 DepthFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
