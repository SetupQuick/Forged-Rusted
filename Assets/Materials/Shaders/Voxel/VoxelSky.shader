// Procedural sky for the day/night cycle: zenith-to-horizon gradient,
// warm glow around the sun at sunrise/sunset, square (voxel) sun and
// moon discs, twinkling stars at night. No textures; every value comes
// from globals set by VoxelAtmosphere. Under water it shows the water
// colour so the sky can never be seen from inside a lake.
Shader "ForgedRusted/Voxel Sky"
{
    Properties
    {
        _SunSize ("Sun Size", Range(0.01, 0.2)) = 0.06
        _MoonSize ("Moon Size", Range(0.01, 0.2)) = 0.045
        _StarDensity ("Star Density", Range(0, 0.02)) = 0.004
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Background"
            "Queue" = "Background"
            "PreviewType" = "Skybox"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "VoxelCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _SunSize;
                half _MoonSize;
                half _StarDensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            // Square disc: distance measured in the plane facing the body.
            half SquareDisc(float3 direction, float3 body, half size, out half glow)
            {
                float facing = dot(direction, body);
                float3 up = abs(body.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 right = normalize(cross(up, body));
                float3 top = cross(body, right);
                float2 p = float2(dot(direction, right), dot(direction, top)) / max(facing, 1e-3);
                float d = max(abs(p.x), abs(p.y));

                glow = facing > 0 ? saturate(1.0 - length(p) / (size * 6.0)) : 0;
                return facing > 0 && d < size ? 1 : 0;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);

                if (_VoxelUnderwater.x > 0)
                    return half4(unity_FogColor.rgb, 1);

                if (_VoxelSkyZenith.a <= 0)
                    return half4(0.55, 0.7, 0.95, 1); // atmosphere not running

                // Gradient: horizon band, zenith above; below the horizon
                // fades to a darker version (seen only from mountains).
                float height = direction.y;
                half3 color = lerp(_VoxelSkyHorizon.rgb, _VoxelSkyZenith.rgb, saturate(pow(saturate(height), 0.55)));
                color = height < 0 ? lerp(_VoxelSkyHorizon.rgb, _VoxelSkyHorizon.rgb * 0.55, saturate(-height * 4)) : color;

                // Warm glow around a low sun.
                float3 sun = _VoxelSunDirection.xyz;
                half sunGlow;
                half sunDisc = SquareDisc(direction, sun, _SunSize, sunGlow);
                half low = saturate(1.0 - abs(sun.y) * 3.0);
                color += _VoxelSunDiscColor.rgb * sunGlow * sunGlow * (0.25 + low * 0.6) * saturate(_VoxelSunDirection.w + 0.2);

                // Stars, slowly turning with the night.
                half stars = 0;
                if (_VoxelStars > 0.01 && height > 0)
                {
                    float angle = atan2(sun.y, sun.z);
                    float s = sin(angle), c = cos(angle);
                    float3 d = float3(direction.x, direction.y * c - direction.z * s, direction.y * s + direction.z * c);
                    float3 cell = floor(d * 220.0);
                    float h = Hash(cell);
                    if (h > 1.0 - _StarDensity)
                    {
                        float twinkle = 0.6 + 0.4 * sin(_VoxelWind.w * (2.0 + h * 5.0) + h * 100.0);
                        stars = twinkle * saturate(height * 4.0) * _VoxelStars;
                    }
                }
                color += stars;

                // Discs drawn over the stars, hidden below the horizon.
                color = lerp(color, _VoxelSunDiscColor.rgb * 3.0, sunDisc * _VoxelSunDirection.w * saturate(height * 20 + 1));

                half moonGlow;
                half moonDisc = SquareDisc(direction, _VoxelMoonDirection.xyz, _MoonSize, moonGlow);
                color += half3(0.5, 0.6, 0.8) * moonGlow * moonGlow * 0.15 * _VoxelMoonDirection.w;
                color = lerp(color, half3(0.85, 0.88, 0.95) * 1.4, moonDisc * _VoxelMoonDirection.w * saturate(height * 20 + 1));

                return half4(color, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
