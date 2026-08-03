Shader "Stylized/ManagementTest"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)

        _Ambient ("Ambient Strength", Range(0,1)) = 0.25
        _Wrap ("Wrap Lighting", Range(0,1)) = 0.35

        _ShadowThreshold ("Shadow Threshold", Range(0,1)) = 0.35
        _ShadowSoftness ("Shadow Softness", Range(0.01,1)) = 0.25

        _RimColor ("Rim Color", Color) = (1,1,1,1)
        _RimStrength ("Rim Strength", Range(0,2)) = 0.25
        _RimPower ("Rim Power", Range(1,8)) = 4

        _SpecColor ("Specular Color", Color) = (1,1,1,1)
        _SpecStrength ("Specular Strength", Range(0,1)) = 0.15
        _SpecPower ("Specular Power", Range(1,128)) = 32
    }


    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }


        Pass
        {
            Name "ForwardLit"

            Tags
            {
                "LightMode"="UniversalForward"
            }


            HLSLPROGRAM

            #pragma target 4.5

            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _FORWARD_PLUS


            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"


            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);


            CBUFFER_START(UnityPerMaterial)

            float4 _BaseColor;

            float _Ambient;
            float _Wrap;

            float _ShadowThreshold;
            float _ShadowSoftness;

            float4 _RimColor;
            float _RimStrength;
            float _RimPower;

            float4 _SpecColor;
            float _SpecStrength;
            float _SpecPower;

            CBUFFER_END



            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };


            struct Varyings
            {
                float4 positionCS : SV_POSITION;

                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };



            Varyings Vert(Attributes IN)
            {
                Varyings OUT;


                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);


                OUT.positionWS =
                    TransformObjectToWorld(
                        IN.positionOS.xyz
                    );


                OUT.positionCS =
                    TransformWorldToHClip(
                        OUT.positionWS
                    );


                OUT.normalWS =
                    TransformObjectToWorldNormal(
                        IN.normalOS
                    );


                OUT.uv = IN.uv;


                return OUT;
            }



            float3 CalculateDiffuse(
                float3 normal,
                float3 lightDirection,
                float3 lightColor,
                float attenuation
            )
            {
                float ndotl =
                    dot(
                        normal,
                        lightDirection
                    );


                float wrapped =
                    saturate(
                        (ndotl + _Wrap)
                        /
                        (1 + _Wrap)
                    );


                float ramp =
                    smoothstep(
                        _ShadowThreshold - _ShadowSoftness,
                        _ShadowThreshold + _ShadowSoftness,
                        wrapped
                    );


                ramp =
                    lerp(
                        _Ambient,
                        1,
                        ramp
                    );


                return
                    lightColor *
                    ramp *
                    attenuation;
            }



            float3 CalculateSpecular(
                float3 normal,
                float3 viewDirection,
                float3 lightDirection
            )
            {
                float3 halfVector =
                    normalize(
                        viewDirection +
                        lightDirection
                    );


                float spec =
                    pow(
                        saturate(
                            dot(
                                normal,
                                halfVector
                            )
                        ),
                        _SpecPower
                    );


                return
                    _SpecColor.rgb *
                    spec *
                    _SpecStrength;
            }




            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);


                float4 albedo =
                    SAMPLE_TEXTURE2D(
                        _BaseMap,
                        sampler_BaseMap,
                        IN.uv
                    )
                    *
                    _BaseColor;



                float3 normal =
                    normalize(
                        IN.normalWS
                    );


                float3 viewDirection =
                    normalize(
                        GetWorldSpaceViewDir(
                            IN.positionWS
                        )
                    );



                float3 lighting =
                    float3(
                        _Ambient,
                        _Ambient,
                        _Ambient
                    );



                Light mainLight =
                    GetMainLight();



                lighting +=
                    CalculateDiffuse(
                        normal,
                        mainLight.direction,
                        mainLight.color,
                        mainLight.shadowAttenuation
                    );



                lighting +=
                    CalculateSpecular(
                        normal,
                        viewDirection,
                        mainLight.direction
                    );



                #if defined(_ADDITIONAL_LIGHTS)

                uint count =
                    GetAdditionalLightsCount();


                for(uint i = 0; i < count; i++)
                {
                    Light light =
                        GetAdditionalLight(
                            i,
                            IN.positionWS
                        );


                    lighting +=
                        CalculateDiffuse(
                            normal,
                            light.direction,
                            light.color,
                            light.distanceAttenuation
                        );
                }

                #endif



                float rim =
                    pow(
                        1 -
                        saturate(
                            dot(
                                normal,
                                viewDirection
                            )
                        ),
                        _RimPower
                    );


                lighting +=
                    _RimColor.rgb *
                    rim *
                    _RimStrength;



                return float4(
                    albedo.rgb *
                    lighting,
                    1
                );
            }


            ENDHLSL
        }
    }
}