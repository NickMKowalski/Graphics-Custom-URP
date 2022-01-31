#ifndef CAPSULE_SHADOWS_LIGHT_LOOP_DEF
#define CAPSULE_SHADOWS_LIGHT_LOOP_DEF

#include "Packages/com.unity.render-pipelines.core/Runtime/Lighting/CapsuleShadows/Shaders/CapsuleShadows.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/CapsuleShadows/CapsuleOccluderData.cs.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"

StructuredBuffer<CapsuleOccluderData> _CapsuleOccluderDatas;

uint GetDefaultCapsuleShadowFeatureBits()
{
#ifdef DEBUG_DISPLAY
    uint featureBits = 0;
    switch (_DebugCapsuleShadowMethod) {
    case CAPSULESHADOWMETHOD_ELLIPSOID:
        featureBits |= CAPSULE_SHADOW_FEATURE_ELLIPSOID;
        break;
    case CAPSULESHADOWMETHOD_FLATTEN_THEN_CLOSEST_SPHERE:
        featureBits |= CAPSULE_SHADOW_FEATURE_FLATTEN;
        break;
    }
    if (_DebugCapsuleFadeSelfShadow) {
        featureBits |= CAPSULE_SHADOW_FEATURE_FADE_SELF_SHADOW;
    }
    return featureBits;
#else
    return CAPSULE_SHADOW_FEATURE_FLATTEN | CAPSULE_SHADOW_FEATURE_FADE_SELF_SHADOW;
#endif
}

float EvaluateCapsuleShadow(
    float3 lightPosOrAxis,
    bool lightIsPunctual,
    float lightCosTheta,
    float shadowRange,
    PositionInputs posInput,
    float3 normalWS,
    uint renderLayer)
{
    float3 surfaceToLightVec = lightPosOrAxis;
    if (lightIsPunctual)
        surfaceToLightVec -= posInput.positionWS;

    uint featureBits = GetDefaultCapsuleShadowFeatureBits();

    uint capsuleCount, capsuleStart;
#ifndef LIGHTLOOP_DISABLE_TILE_AND_CLUSTER
    GetCountAndStart(posInput, LIGHTCATEGORY_CAPSULE_DIRECT_SHADOW, capsuleStart, capsuleCount);
#else   // LIGHTLOOP_DISABLE_TILE_AND_CLUSTER
    capsuleCount = _CapsuleDirectShadowCount; 
    capsuleStart = 0;
#endif

    bool fastPath = false;
#if SCALARIZE_LIGHT_LOOP
    uint capsuleStartLane0;
    fastPath = IsFastPath(capsuleStart, capsuleStartLane0);
    if (fastPath)
        capsuleStart = capsuleStartLane0;
#endif

    // Scalarized loop. All capsules that are in a tile/cluster touched by any pixel in the wave are loaded (scalar load), only the one relevant to current thread/pixel are processed.
    // For clarity, the following code will follow the convention: variables starting with s_ are meant to be wave uniform (meant for scalar register),
    // v_ are variables that might have different value for each thread in the wave (meant for vector registers).
    // This will perform more loads than it is supposed to, however, the benefits should offset the downside, especially given that light data accessed should be largely coherent.
    // Note that the above is valid only if wave intriniscs are supported.
    uint v_capsuleListOffset = 0;
    uint v_capsuleIdx = capsuleStart;

    float visibility = 1.f;
    while (v_capsuleListOffset < capsuleCount)
    {
        v_capsuleIdx = FetchIndex(capsuleStart, v_capsuleListOffset);
#if SCALARIZE_LIGHT_LOOP
        uint s_capsuleIdx = ScalarizeElementIndex(v_capsuleIdx, fastPath);
#else
        uint s_capsuleIdx = v_capsuleIdx;
#endif
        if (s_capsuleIdx == -1)
            break;

        CapsuleOccluderData s_capsuleData = _CapsuleOccluderDatas[s_capsuleIdx];

        // If current scalar and vector capsule index match, we process the capsule. The v_capsuleListOffset for current thread is increased.
        // Note that the following should really be ==, however, since helper lanes are not considered by WaveActiveMin, such helper lanes could
        // end up with a unique v_capsuleIdx value that is smaller than s_capsuleIdx hence being stuck in a loop. All the active lanes will not have this problem.
        if (s_capsuleIdx >= v_capsuleIdx)
        {
            v_capsuleListOffset++;

            if (IsMatchingLightLayer(s_capsuleData.lightLayers, renderLayer))
            {
                float3 surfaceToCapsuleVec = s_capsuleData.centerRWS - posInput.positionWS;
                float occlusion = EvaluateCapsuleOcclusion(
                    featureBits,
                    surfaceToLightVec,
                    lightIsPunctual,
                    lightCosTheta,
                    surfaceToCapsuleVec,
                    s_capsuleData.axisDirWS,
                    s_capsuleData.offset,
                    s_capsuleData.radius,
                    shadowRange,
                    normalWS);

                // combine visibility by multiplying term from each capsule
                visibility *= max(1.f - occlusion, 0.f);
            }
        }
    }
    return visibility;
}

float EvaluateCapsuleAmbientOcclusion(PositionInputs posInput, float3 normalWS)
{
    uint capsuleShadowCount = _CapsuleIndirectShadowCountAndFlags & 0x00ffffffU;
    uint aoMethod = _CapsuleIndirectShadowCountAndFlags >> 24;
    if (capsuleShadowCount == 0)
        return 1.f;

    uint capsuleCount, capsuleStart;
#ifndef LIGHTLOOP_DISABLE_TILE_AND_CLUSTER
    GetCountAndStart(posInput, LIGHTCATEGORY_CAPSULE_INDIRECT_SHADOW, capsuleStart, capsuleCount);
#else   // LIGHTLOOP_DISABLE_TILE_AND_CLUSTER
    capsuleCount = capsuleShadowCount; 
    capsuleStart = 0;
#endif

    bool fastPath = false;
#if SCALARIZE_LIGHT_LOOP
    uint capsuleStartLane0;
    fastPath = IsFastPath(capsuleStart, capsuleStartLane0);
    if (fastPath)
        capsuleStart = capsuleStartLane0;
#endif

    // Scalarized loop. All capsules that are in a tile/cluster touched by any pixel in the wave are loaded (scalar load), only the one relevant to current thread/pixel are processed.
    // For clarity, the following code will follow the convention: variables starting with s_ are meant to be wave uniform (meant for scalar register),
    // v_ are variables that might have different value for each thread in the wave (meant for vector registers).
    // This will perform more loads than it is supposed to, however, the benefits should offset the downside, especially given that light data accessed should be largely coherent.
    // Note that the above is valid only if wave intriniscs are supported.
    uint v_capsuleListOffset = 0;
    uint v_capsuleIdx = capsuleStart;

    float visibility = 1.f;
    while (v_capsuleListOffset < capsuleCount)
    {
        v_capsuleIdx = FetchIndex(capsuleStart, v_capsuleListOffset);
#if SCALARIZE_LIGHT_LOOP
        uint s_capsuleIdx = ScalarizeElementIndex(v_capsuleIdx, fastPath);
#else
        uint s_capsuleIdx = v_capsuleIdx;
#endif
        if (s_capsuleIdx == -1)
            break;

        CapsuleOccluderData s_capsuleData = _CapsuleOccluderDatas[_CapsuleDirectShadowCount + s_capsuleIdx];

        // If current scalar and vector capsule index match, we process the capsule. The v_capsuleListOffset for current thread is increased.
        // Note that the following should really be ==, however, since helper lanes are not considered by WaveActiveMin, such helper lanes could
        // end up with a unique v_capsuleIdx value that is smaller than s_capsuleIdx hence being stuck in a loop. All the active lanes will not have this problem.
        if (s_capsuleIdx >= v_capsuleIdx)
        {
            v_capsuleListOffset++;

            // get the closest position on the (infinite) capsule axis
            float3 surfaceToCapsuleVec = s_capsuleData.centerRWS - posInput.positionWS;
            float closestT = RayClosestPoint(surfaceToCapsuleVec, s_capsuleData.axisDirWS, float3(0.f, 0.f, 0.f));

            // get the closest interior sphere to the surface
            float clampedClosestT = clamp(closestT, -s_capsuleData.offset, s_capsuleData.offset);
            float3 surfaceToSphereVec = surfaceToCapsuleVec + clampedClosestT*s_capsuleData.axisDirWS;
            float sphereDistance = length(surfaceToSphereVec);
            float capsuleBoundaryDistance = sphereDistance - s_capsuleData.radius;

            // apply range-based falloff
            float occlusion = smoothstep(1.0f, 0.75f, capsuleBoundaryDistance/_CapsuleAmbientOcclusionRange);
            if (occlusion > 0.f)
            {
                // compute AO from this closest interior sphere
                // ref: https://iquilezles.org/www/articles/sphereao/sphereao.htm
                float3 surfaceToSphereDir = surfaceToSphereVec/sphereDistance;
                float cosAlpha = dot(normalWS, surfaceToSphereDir);
                float sphereAO = saturate(cosAlpha*Sq(s_capsuleData.radius/sphereDistance));

                if (aoMethod == CAPSULEAMBIENTOCCLUSIONMETHOD_LINE_INTEGRAL)
                {
                    // cosine-weighted occlusion from a thick line along the capsule axis
                    float t1 = -s_capsuleData.offset - closestT;
                    float t2 = s_capsuleData.offset - closestT;
                    float capDistance = max(min(-t1, t2), 0.f);
                    float lineIntegral = LineDiffuseOcclusion(
                        surfaceToCapsuleVec + closestT*s_capsuleData.axisDirWS,
                        s_capsuleData.axisDirWS,
                        -s_capsuleData.offset - closestT,
                        s_capsuleData.offset - closestT,
                        normalWS);
                    float thickLineAO = s_capsuleData.radius*lineIntegral;

                    // assume that 50% of the sphere occlusion is independent of the line (for long capsules with hemispherical caps)
                    // but ensure that the result is always at least at much as only using the sphere (for short capsules)
                    occlusion *= clamp(thickLineAO + 0.5f*sphereAO, sphereAO, 1.f);
                }
                else
                {
                    occlusion *= sphereAO;
                }
            }

            // combine visibility by multiplying term from each capsule
            visibility *= max(1.f - occlusion, 0.f);
        }
    }
    return visibility;
}

#endif
