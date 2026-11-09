// Transparent water for URP: vertex-colour tinted, small animated
// ripples and gentle waves (uv0.x = 1 on the open surface), reflections
// of the day/night sky gradient with a Fresnel falloff, sun/moon glints,
// receives shadows.
// From below (camera under water) the surface is what real water looks
// like from inside: a bright window straight up and a dark, mirror-like
// ceiling at shallow angles (total internal reflection) - not the sky.
Shader "ForgedRusted/Voxel Water"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.68
        _SpecularStrength ("Specular Strength", Range(0, 4)) = 2
        _ReflectionStrength ("Reflection Strength", Range(0, 1)) = 0.75
        _RippleStrength ("Ripple Strength", Range(0, 0.5)) = 0.12
        _RippleSpeed ("Ripple Speed", Range(0, 4)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Opacity;
                half _SpecularStrength;
                half _ReflectionStrength;
                half _RippleStrength;
                half _RippleSpeed;
            CBUFFER_END

            half4 _VoxelSkyColor;
            half4 _VoxelGroundColor;

            #include "VoxelCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                half fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                // Slow swell on the open surface (only vertices marked by
                // the mesher, so edges against blocks stay put).
                float t = _VoxelWind.w;
                float swell = sin(positionWS.x * 0.35 + t * 1.2) * 0.5 + sin(positionWS.z * 0.42 - t * 0.9) * 0.5;
                positionWS.y += swell * 0.035 * input.uv.x * (0.5 + _VoxelWind.z * 0.5);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);

                half4 color = input.color;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    color.rgb = SRGBToLinear(color.rgb);
                #endif
                output.color = color * _Tint;

                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            // Cheap ripples: a few crossing sine waves give a moving slope.
            half3 RippleNormal(float3 positionWS, half3 normal)
            {
                float t = _Time.y * _RippleSpeed;
                float2 p = positionWS.xz;

                float2 slope =
                    float2(cos(p.x * 1.7 + t * 1.3), cos(p.y * 1.9 - t * 1.1)) * 0.5 +
                    float2(cos((p.x + p.y) * 3.1 + t * 2.0), cos((p.x - p.y) * 2.7 - t * 1.7)) * 0.3;

                half3 n = normalize(half3(-slope.x * _RippleStrength, 1, -slope.y * _RippleStrength));

                // Only the top surface ripples.
                return normal.y > 0.5 ? n : normal;
            }

            half4 Frag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                half3 normal = normalize(input.normalWS);
                normal = RippleNormal(input.positionWS, normal);
                normal = isFrontFace ? normal : -normal;

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light light = GetMainLight(shadowCoord);
                half attenuation = light.shadowAttenuation * light.distanceAttenuation;

                half3 viewDirection = normalize(GetWorldSpaceViewDir(input.positionWS));
                half nDotV = saturate(dot(normal, viewDirection));
                half fresnel = 0.04 + 0.96 * pow(1.0 - nDotV, 5);

                half3 ambient = _VoxelSkyColor.a > 0 ? _VoxelSkyColor.rgb : SampleSH(normal);
                half diffuse = saturate(dot(normal, light.direction));
                half3 body = input.color.rgb * (ambient * 0.8 + light.color * diffuse * attenuation * 0.6);

                // Reflection of the sky gradient (follows the day/night
                // cycle; a baked reflection probe would not).
                half3 reflected = reflect(-viewDirection, normal);
                half3 reflection;
                if (_VoxelSkyZenith.a > 0)
                    reflection = lerp(_VoxelSkyHorizon.rgb, _VoxelSkyZenith.rgb, saturate(reflected.y * 1.5));
                else
                    reflection = GlossyEnvironmentReflection(reflected, 0.05h, 1.0h);

                half3 color = lerp(body, reflection, saturate(fresnel + 0.15) * _ReflectionStrength);

                // Sun glints.
                half3 halfVector = normalize(light.direction + viewDirection);
                half specular = pow(saturate(dot(normal, halfVector)), 256) * _SpecularStrength;
                color += light.color * specular * attenuation;

                color = MixFog(color, input.fogFactor);

                half alpha = saturate(_Opacity + fresnel * 0.3) * _Tint.a;

                if (!isFrontFace && _VoxelUnderwater.x <= 0)
                {
                    // Back faces seen from above water (rare: water next
                    // to an air gap). Mostly opaque, like before.
                    color = lerp(input.color.rgb * (ambient * 0.5 + light.color * 0.2), color, 0.2);
                    color = MixFog(color, input.fogFactor);
                    alpha = 0.85 * _Tint.a;
                }
                else if (!isFrontFace)
                {
                    // From inside the water. Within ~48 degrees of straight
                    // up light comes through (Snell's window), tinted by
                    // the water; further out the surface mirrors the dark
                    // water below. Opaque, so the sky never shows through.
                    // normal is the rippled one turned towards the camera,
                    // so the edge of the window wobbles with the waves.
                    half up = saturate(dot(viewDirection, normal));
                    half window = smoothstep(0.64, 0.74, up);

                    half3 deep = unity_FogColor.rgb * 0.45;
                    half3 lit = (_VoxelSkyZenith.a > 0 ? _VoxelSkyZenith.rgb : ambient) * 0.6 + light.color * 0.35;
                    lit *= input.color.rgb * 1.6 + 0.2;
                    lit = lerp(lit, unity_FogColor.rgb * 2.5, 0.35); // seen through the water

                    // Ripples make the window wobble and sparkle.
                    half sparkle = pow(saturate(dot(-normal, light.direction)), 64) * attenuation;
                    color = lerp(deep, lit, window) + light.color * sparkle * window * 0.6;
                    color = MixFog(color, input.fogFactor);
                    alpha = 1.0;
                }

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
