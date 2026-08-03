#ifndef CLINICAL_LIGHTING_CORE_INCLUDED
#define CLINICAL_LIGHTING_CORE_INCLUDED

// v5: core rewrite. Previously this hand-rolled N.L/specular math directly, which
// was fragile and evidently not giving a strong/correct enough light response.
// This version builds on UniversalFragmentPBR - the same lighting function URP's own
// stock Lit shader uses - so main light, all additional/Forward+ lights, shadows, and
// ambient are guaranteed to behave correctly. Rim + grime are layered on top as a
// thin stylization pass; they don't touch the core light math at all.
//
// Also fixes the specific bug pointed out in the reference toon shader: that shader
// hardcodes `inputData.bakedGI = half3(0,0,0)`, so anything not hit dead-on by the
// main light has zero fallback light - hence the almost-black undersides and
// inconsistent look. Here bakedGI is real ambient (SampleSH), so unlit faces still
// read as lit, just cooler/dimmer - no dead black sides.

half Hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

half GrimeNoise(float3 worldPos, half3 worldNormal, half scale)
{
    half2 blend = half2(abs(worldNormal.x) + abs(worldNormal.z), abs(worldNormal.y));
    float2 uvXZ = worldPos.xz * scale;
    float2 uvXY = worldPos.xy * scale;
    half nA = Hash21(floor(uvXZ)) * 0.6h + Hash21(floor(uvXZ * 3.7h)) * 0.4h;
    half nB = Hash21(floor(uvXY)) * 0.6h + Hash21(floor(uvXY * 3.7h)) * 0.4h;
    return lerp(nB, nA, saturate(blend.x));
}

// NOTE: requires _SPECULAR_SETUP to be #defined before Lighting.hlsl is included
// (done in the .shader file) so that SurfaceData.specular actually tints the highlight
// instead of being ignored by the metallic workflow.
half3 EvaluateClinicalLighting(
    float3 worldPos,
    half3 worldNormal,
    half3 viewDirWS,
    half3 albedo,
    half smoothness,
    half3 specColor,
    half celBands,
    half rimPower,
    half3 rimColor,
    half rimIntensity,
    half ambientIntensity,
    half grimeAmount,
    half grimeScale,
    float4 shadowCoord,
    float4 positionCS)
{
    half grime = GrimeNoise(worldPos, worldNormal, grimeScale);
    half3 grimedAlbedo = albedo * lerp(1.0h, grime, grimeAmount);

    InputData inputData = (InputData)0;
    inputData.positionWS = worldPos;
    inputData.normalWS = worldNormal;
    inputData.viewDirectionWS = viewDirWS;
    inputData.shadowCoord = shadowCoord;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    inputData.shadowMask = half4(1, 1, 1, 1);
    inputData.bakedGI = SampleSH(worldNormal) * ambientIntensity;

    SurfaceData surfaceData = (SurfaceData)0;
    surfaceData.albedo = grimedAlbedo;
    surfaceData.alpha = 1.0h;
    surfaceData.metallic = 0.0h;
    surfaceData.smoothness = smoothness;
    surfaceData.occlusion = 1.0h;
    surfaceData.specular = specColor;
    surfaceData.emission = 0.0h;
    surfaceData.normalTS = half3(0, 0, 1);
    surfaceData.clearCoatMask = 0.0h;
    surfaceData.clearCoatSmoothness = 0.0h;

    half4 pbr = UniversalFragmentPBR(inputData, surfaceData);
    half3 result = pbr.rgb;

    // ---- Optional posterize of the final shaded result (off by default, celBands=0) ----
    // Applied AFTER real PBR shading, not instead of it - so even with banding on,
    // the underlying light response (direction, falloff, ambient) stays correct;
    // this just quantizes the brightness into steps rather than replacing the model.
    if (celBands > 0.5h)
    {
        half luma = dot(result, half3(0.2126h, 0.7152h, 0.0722h));
        half bandedLuma = floor(luma * celBands + 0.5h) / celBands;
        result *= bandedLuma / max(luma, 1e-4h);
    }

    // ---- Rim: intentionally NOT gated to the lit side, so it reads as a consistent
    // edge highlight on every side of the object instead of vanishing on the back
    // (that vanishing-on-the-back behavior was part of what made v3/v4 feel dead). ----
    half rim = pow(1.0h - saturate(dot(worldNormal, viewDirWS)), rimPower);
    result += rim * rimColor * rimIntensity;

    return result;
}

#endif
