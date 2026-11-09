// Transparent water for URP: vertex colour tinted, lit by the main
// light, receives shadows, visible from above and below.
Shader "ForgedRusted/Voxel Water"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.72
        _SpecularStrength ("Specular Strength", Range(0, 2)) = 0.6
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
            CBUFFER_END

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

            half4 Frag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                half3 normal = normalize(input.normalWS);
                normal = isFrontFace ? normal : -normal;

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light light = GetMainLight(shadowCoord);

                half attenuation = light.shadowAttenuation * light.distanceAttenuation;
                half diffuse = saturate(dot(normal, light.direction));

                half3 viewDirection = normalize(GetWorldSpaceViewDir(input.positionWS));
                half3 halfVector = normalize(light.direction + viewDirection);
                half specular = pow(saturate(dot(normal, halfVector)), 64) * _SpecularStrength;

                half3 lighting = SampleSH(normal) + light.color * diffuse * attenuation;
                half3 color = input.color.rgb * lighting + light.color * specular * attenuation;
                color = MixFog(color, input.fogFactor);

                return half4(color, _Opacity * _Tint.a);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
