Shader "Custom/EntityDitherLitURP"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1,1,1,1)

        _CelSteps ("Cel Steps", Range(2,8)) = 5
        _DitherStrength ("Dither Strength", Range(0,1)) = 0.1
        _CelStrength ("Cel Strength", Range(0,1)) = 0.25

        _AmbientGI ("Ambient GI Strength", Range(0,1)) = 0.15

        [Header(Entity Settings)]
        _EntityMode ("Entity Mode", Range(0,1)) = 0

        _EntityTint ("Entity Tint", Color) = (0.4,0.8,1,1)
        _RimColor ("Rim Color", Color) = (0.5,0.9,1,1)

        _RimStrength ("Rim Strength", Range(0,2)) = 0.25
        _RimPower ("Rim Power", Range(1,8)) = 4
    }


    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
        }


        Pass
        {
            Name "ForwardLit"

            Tags
            {
                "LightMode"="UniversalForward"
            }


            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT


            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"


            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);



            CBUFFER_START(UnityPerMaterial)

            float4 _BaseColor;

            float _CelSteps;
            float _DitherStrength;
            float _CelStrength;

            float _AmbientGI;

            float _EntityMode;

            float4 _EntityTint;
            float4 _RimColor;

            float _RimStrength;
            float _RimPower;


            CBUFFER_END



            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };


            struct Varyings
            {
                float4 positionCS : SV_POSITION;

                float3 positionWS : TEXCOORD0;

                float3 normalWS : TEXCOORD1;

                float2 uv : TEXCOORD2;
            };



            Varyings Vert(Attributes input)
            {
                Varyings output;


                VertexPositionInputs position =
                    GetVertexPositionInputs(
                        input.positionOS.xyz
                    );


                VertexNormalInputs normal =
                    GetVertexNormalInputs(
                        input.normalOS
                    );


                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = normal.normalWS;
                output.uv = input.uv;


                return output;
            }



            float Bayer4x4(float2 pixel)
            {
                int x = ((int)pixel.x) & 3;
                int y = ((int)pixel.y) & 3;


                if(y==0)
                {
                    if(x==0)return 0;
                    if(x==1)return 8;
                    if(x==2)return 2;
                    return 10;
                }

                if(y==1)
                {
                    if(x==0)return 12;
                    if(x==1)return 4;
                    if(x==2)return 14;
                    return 6;
                }

                if(y==2)
                {
                    if(x==0)return 3;
                    if(x==1)return 11;
                    if(x==2)return 1;
                    return 9;
                }

                if(x==0)return 15;
                if(x==1)return 7;
                if(x==2)return 13;

                return 5;
            }



            half4 Frag(Varyings input) : SV_Target
            {
                SurfaceData surfaceData = (SurfaceData)0;


                surfaceData.albedo =
                    SAMPLE_TEXTURE2D(
                        _BaseMap,
                        sampler_BaseMap,
                        input.uv
                    ).rgb
                    *
                    _BaseColor.rgb;


                surfaceData.metallic = 0;
                surfaceData.specular = float3(0,0,0);
                surfaceData.smoothness = 0;
                surfaceData.normalTS = float3(0,0,1);
                surfaceData.occlusion = 1;
                surfaceData.emission = float3(0,0,0);
                surfaceData.alpha = 1;



                InputData inputData = (InputData)0;


                inputData.positionWS =
                    input.positionWS;


                inputData.normalWS =
                    NormalizeNormalPerPixel(
                        input.normalWS
                    );


                inputData.viewDirectionWS =
                    SafeNormalize(
                        GetWorldSpaceViewDir(
                            input.positionWS
                        )
                    );


                inputData.shadowCoord =
                    TransformWorldToShadowCoord(
                        input.positionWS
                    );


                inputData.bakedGI =
                    SampleSH(
                        inputData.normalWS
                    )
                    *
                    _AmbientGI;


                inputData.normalizedScreenSpaceUV =
                    GetNormalizedScreenSpaceUV(
                        input.positionCS
                    );


                inputData.vertexLighting = 0;
                inputData.fogCoord = 0;



                half4 color =
                    UniversalFragmentPBR(
                        inputData,
                        surfaceData
                    );



                // Cel shading

                float luminance =
                    dot(
                        color.rgb,
                        float3(
                            0.299,
                            0.587,
                            0.114
                        )
                    );


                float threshold =
                    Bayer4x4(
                        input.positionCS.xy
                    )
                    /
                    16.0;


                float stepped =
                    floor(
                        (
                            luminance +
                            threshold *
                            _DitherStrength
                        )
                        *
                        _CelSteps
                    )
                    /
                    _CelSteps;


                float target =
                    lerp(
                        luminance,
                        stepped,
                        _CelStrength
                    );


                float ratio =
                    target /
                    max(
                        luminance,
                        0.001
                    );


                ratio =
                    clamp(
                        ratio,
                        0.75,
                        1.15
                    );


                color.rgb *= ratio;



                // Entity effect

                if(_EntityMode > 0.5)
                {
                    float rim =
                        1 -
                        saturate(
                            dot(
                                inputData.viewDirectionWS,
                                inputData.normalWS
                            )
                        );


                    rim =
                        pow(
                            rim,
                            _RimPower
                        );


                    color.rgb +=
                        _RimColor.rgb *
                        rim *
                        _RimStrength;


                    color.rgb =
                        lerp(
                            color.rgb,
                            color.rgb *
                            _EntityTint.rgb,
                            0.15
                        );
                }



                return color;
            }


            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}