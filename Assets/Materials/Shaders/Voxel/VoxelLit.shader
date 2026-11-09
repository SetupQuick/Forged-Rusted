// Opaque voxel terrain for URP.
// Vertex colour RGB = albedo (block colour * biome tint * per-voxel
// variation), vertex alpha = baked ambient occlusion.
// Lighting: warm main light with soft shadows plus a sky/ground
// hemisphere light that the AO darkens - a cheap stand-in for global
// illumination. The hemisphere colours are set globally by
// VoxelAtmosphere (falls back to Unity's ambient light probe).
// No textures, so it stays cheap to render.
// Wind: uv0.x is a per-vertex wind weight (leaves, grass); every pass
// applies the same offset so shadows and depth match the moving shape.
Shader "ForgedRusted/Voxel Lit"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 1
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

        CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            half _AmbientStrength;
        CBUFFER_END

        // Set by VoxelAtmosphere (global, not per material).
        half4 _VoxelSkyColor;
        half4 _VoxelGroundColor;
        half _VoxelAoDirectStrength;

        #include "VoxelCommon.hlsl"

        float3 VoxelPositionWS(float3 positionOS, float windWeight)
        {
            float3 positionWS = TransformObjectToWorld(positionOS);
            return positionWS + VoxelWindOffset(positionWS, windWeight);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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

                float3 positionWS = VoxelPositionWS(input.positionOS.xyz, input.uv.x);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);

                half4 color = input.color;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    // Albedo is authored in sRGB; alpha (AO) is linear.
                    color.rgb = SRGBToLinear(color.rgb);
                #endif
                color.rgb *= _Tint.rgb;
                output.color = color;

                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normal = normalize(input.normalWS);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light light = GetMainLight(shadowCoord);

                half diffuse = saturate(dot(normal, light.direction));
                half attenuation = light.shadowAttenuation * light.distanceAttenuation;
                half ao = input.color.a;

                // Sky light from above, warm bounce light from below.
                half3 ambient;
                if (_VoxelSkyColor.a > 0)
                    ambient = lerp(_VoxelGroundColor.rgb, _VoxelSkyColor.rgb, normal.y * 0.5 + 0.5);
                else
                    ambient = SampleSH(normal);

                ambient *= _AmbientStrength * ao;

                // AO affects direct light only a little (contact shadow).
                half directAo = lerp(1.0, ao, _VoxelAoDirectStrength > 0 ? _VoxelAoDirectStrength : 0.4);
                half3 direct = light.color * diffuse * attenuation * directAo;

                // Under water: light fades with depth and the sun throws
                // moving caustics on everything below the surface.
                UNITY_BRANCH
                if (_VoxelUnderwater.x > 0 && input.positionWS.y < _VoxelUnderwater.z)
                {
                    half caustic = VoxelCaustics(input.positionWS);
                    direct *= 0.6 + caustic * 1.0 * saturate(normal.y + 0.3);

                    // Water absorbs red first, then green: light that went
                    // down through a few metres of water is blue-green.
                    half3 absorbed = VoxelWaterAbsorption(_VoxelUnderwater.z - input.positionWS.y);
                    direct *= absorbed;
                    ambient *= sqrt(absorbed);
                }

                half3 lighting = ambient + direct;

                half3 color = input.color.rgb * lighting;
                color = MixFog(color, input.fogFactor);

                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            float4 Vert(Attributes input) : SV_POSITION
            {
                float3 positionWS = VoxelPositionWS(input.positionOS.xyz, input.uv.x);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirection = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirection = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                return positionCS;
            }

            half4 Frag() : SV_Target
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
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Vert(float4 positionOS : POSITION, float2 uv : TEXCOORD0) : SV_POSITION
            {
                return TransformWorldToHClip(VoxelPositionWS(positionOS.xyz, uv.x));
            }

            half Frag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(VoxelPositionWS(input.positionOS.xyz, input.uv.x));
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
