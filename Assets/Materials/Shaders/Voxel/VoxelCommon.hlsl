// Shared globals and helpers for the voxel shaders. All values are set
// from code (VoxelAtmosphere, UnderwaterEffect, WindSettings), never per
// material, so one SetGlobal call updates every chunk.
#ifndef FORGED_RUSTED_VOXEL_COMMON_INCLUDED
#define FORGED_RUSTED_VOXEL_COMMON_INCLUDED

// Wind: xy = direction on the ground plane, z = strength (0 = off),
// w = time in seconds (wraps every two hours).
float4 _VoxelWind;

// Under water: x = 1 while the camera is under water, y = depth 0..1
// (0 at the surface, 1 deep down), z = height of the water surface
// above the camera (world metres).
float4 _VoxelUnderwater;

// Sky (day/night cycle).
half4 _VoxelSkyZenith;
half4 _VoxelSkyHorizon;
float4 _VoxelSunDirection;   // xyz towards the sun, w = sun disc brightness
float4 _VoxelMoonDirection;  // xyz towards the moon, w = moon disc brightness
half4 _VoxelSunDiscColor;
half _VoxelStars;            // 0 by day, 1 on a clear night

// Moves a vertex with the wind. weight comes from the mesh (uv0.x):
// 0 = fixed (stone, trunks), ~0.35 = leaves, 1 = tips of grass.
// Depends only on world position and weight, so vertices shared by
// neighbouring faces move together and no cracks open.
float3 VoxelWindOffset(float3 positionWS, float weight)
{
    if (weight <= 0.0 || _VoxelWind.z <= 0.0)
        return float3(0, 0, 0);

    float t = _VoxelWind.w;
    float2 dir = _VoxelWind.xy;

    // Slow gusts travel across the land along the wind direction.
    float along = dot(positionWS.xz, dir);
    float gust = sin(along * 0.05 - t * 0.9) * 0.5 + 0.5;
    gust = 0.35 + 0.65 * gust * gust;

    // Quick flutter, different per spot.
    float phase = dot(positionWS.xz, float2(0.73, 0.41)) + positionWS.y * 0.3;
    float flutter = sin(t * 2.3 + phase) * 0.6 + sin(t * 3.9 + phase * 1.7) * 0.4;

    float amount = weight * _VoxelWind.z * gust;

    float3 offset;
    offset.xz = dir * (0.6 + 0.4 * flutter) * amount * 0.10;
    offset.y = flutter * amount * 0.015;
    return offset;
}

// Fraction of light left after travelling this many metres through water.
half3 VoxelWaterAbsorption(float metres)
{
    return (half3)exp(-max(metres, 0.0) * float3(0.28, 0.09, 0.06));
}

// Cheap animated caustics (bright wobbly lines), 0..1.
half VoxelCaustics(float3 positionWS)
{
    float t = _VoxelWind.w;
    float2 p = positionWS.xz * 1.6 + positionWS.y * 0.2;
    float a = sin(p.x * 1.3 + t * 1.1) + sin(p.y * 1.7 - t * 0.9);
    float b = sin((p.x + p.y) * 1.1 + t * 1.4) + sin((p.x - p.y) * 1.5 - t * 1.2);
    float v = 1.0 - abs(a * 0.25 + b * 0.25);
    return (half)(v * v * v * v);
}

#endif
