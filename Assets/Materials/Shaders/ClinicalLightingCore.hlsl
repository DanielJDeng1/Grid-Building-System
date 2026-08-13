#ifndef CLINICAL_LIGHTING_CORE_INCLUDED
#define CLINICAL_LIGHTING_CORE_INCLUDED

// v6: pivot to high-contrast toon shading (2-tone diffuse + hard specular streak),
// matching the reference look: bright top surfaces, a tight bright highlight along
// curvature, and a darker but still-readable shadow tone - not pure black.
//
// Key differences from the reference toon shader you sent (kept deliberately):
//  1. Diffuse from ALL lights (main + additional) is summed into one physically
//     correct accumulator FIRST, and the lit/shadow band decision is made ONCE from
//     that total. The reference file bands each light separately and adds the bands,
//     which stacks oddly once more than one light is in play.
//  2. Ambient is always added as a floor AFTER the band decision, never hardcoded to
//     zero - this is what was making backsides/undersides go almost black.
//  3. Band edges are anti-aliased via fwidth() (same technique the reference file
//     used, kept because it's genuinely good), so edges don't shimmer/crawl as the
//     camera or light moves.

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

// Anti-aliased hard threshold - the core toon-band primitive, reused for the
// diffuse lit/shadow split, the specular highlight, and the rim.
half ToonThreshold(half x, half threshold, half softness)
{
    half aa = max(fwidth(x), 1e-4h) * softness;
    return smoothstep(threshold - aa, threshold + aa, x);
}

half3 EvaluateClinicalToonLighting(
    float3 worldPos,
    half3 worldNormal,
    half3 viewDirWS,
    half3 albedo,
    half specularExponent,
    half3 shadowColor,
    half shadowThreshold,
    half shadowSoftness,
    half3 specColor,
    half specThreshold,
    half specSoftness,
    half specIntensity,
    half3 rimColor,
    half rimPower,
    half rimThreshold,
    half rimSoftness,
    half rimIntensity,
    half ambientIntensity,
    half grimeAmount,
    half grimeScale,
    float4 shadowCoord)
{
    half grime = GrimeNoise(worldPos, worldNormal, grimeScale);
    half3 grimedAlbedo = albedo * lerp(1.0h, grime, grimeAmount);

    // ---- Accumulate diffuse + specular mask across ALL lights first ----
    half3 diffuseAccum = 0;
    half specMask = 0;

    Light mainLight = GetMainLight(shadowCoord);
    half mainAtten = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
    diffuseAccum += mainLight.color * saturate(dot(worldNormal, mainLight.direction)) * mainAtten;

    half3 mainHalf = normalize(mainLight.direction + viewDirWS);
    specMask += pow(saturate(dot(worldNormal, mainHalf)), specularExponent) * mainAtten;

    #ifdef _ADDITIONAL_LIGHTS
    uint additionalLightsCount = GetAdditionalLightsCount();
    for (uint i = 0u; i < additionalLightsCount; i++)
    {
        Light light = GetAdditionalLight(i, worldPos);
        half atten = light.distanceAttenuation * light.shadowAttenuation;
        diffuseAccum += light.color * saturate(dot(worldNormal, light.direction)) * atten;

        half3 hDir = normalize(light.direction + viewDirWS);
        specMask += pow(saturate(dot(worldNormal, hDir)), specularExponent) * atten;
    }
    #endif

    // ---- One band decision for the whole diffuse term ----
    half diffuseMask = dot(diffuseAccum, half3(0.2126h, 0.7152h, 0.0722h));
    half litBand = ToonThreshold(diffuseMask, shadowThreshold, shadowSoftness);

    half3 shadowTone = grimedAlbedo * shadowColor;
    half3 litTone = grimedAlbedo * diffuseAccum;
    half3 result = lerp(shadowTone, litTone, litBand);

    // ---- Ambient floor, added AFTER the band decision - never zero, so shadow
    // faces stay a readable cool tone instead of going dead. ----
    result += grimedAlbedo * SampleSH(worldNormal) * ambientIntensity;

    // ---- Hard specular highlight band ----
    half specBand = ToonThreshold(specMask, specThreshold, specSoftness);
    result += specBand * specColor * specIntensity;

    // ---- Rim, banded the same way, visible on every side (not gated to "lit") ----
    half rimRaw = pow(1.0h - saturate(dot(worldNormal, viewDirWS)), rimPower);
    half rimBand = ToonThreshold(rimRaw, rimThreshold, rimSoftness);
    result += rimBand * rimColor * rimIntensity;

    return result;
}

#endif
