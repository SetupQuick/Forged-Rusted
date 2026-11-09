// Transparent water for URP: vertex-colour tinted, small animated
// ripples, sky reflections (reflection probe / skybox) with a Fresnel
// falloff, sun glints, receives shadows, visible from above and below.
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

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
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

                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);

                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);

                half4 color = input.color;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    color.rgb = SRGBToLinear(color.rgb);
                #endif
                output.color = color * _Tint;

                output.fogFactor = ComputeFogFactor(position.positionCS.z);
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

                // Sky reflection from the reflection probe / skybox.
                half3 reflected = reflect(-viewDirection, normal);
                half3 reflection = GlossyEnvironmentReflection(reflected, 0.05h, 1.0h);

                half3 color = lerp(body, reflection, saturate(fresnel + 0.15) * _ReflectionStrength);

                // Sun glints.
                half3 halfVector = normalize(light.direction + viewDirection);
                half specular = pow(saturate(dot(normal, halfVector)), 256) * _SpecularStrength;
                color += light.color * specular * attenuation;

                color = MixFog(color, input.fogFactor);

                half alpha = saturate(_Opacity + fresnel * 0.3) * _Tint.a;

                // Seen from below, the surface is a bright, mostly opaque
                // ceiling (like total internal reflection) instead of a
                // clear window to the sky.
                if (!isFrontFace)
                {
                    color = lerp(input.color.rgb * (ambient * 0.5 + light.color * 0.2), color, 0.2);
                    color = MixFog(color, input.fogFactor);
                    alpha = 0.85 * _Tint.a;
                }

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
