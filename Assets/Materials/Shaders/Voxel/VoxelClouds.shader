// Blocky voxel clouds: flat-shaded boxes lit by the sun/moon and sky
// colours, slightly see-through, fading out towards the edge of the
// cloud field so there is no hard border. Not affected by distance fog
// (they are far above the terrain) and cast no shadows.
Shader "ForgedRusted/Voxel Clouds"
{
    Properties
    {
        _Opacity ("Opacity", Range(0, 1)) = 0.85
        _FadeDistance ("Fade Distance", Float) = 300
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-50"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Clouds"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "VoxelCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Opacity;
                float _FadeDistance;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normal = normalize(input.normalWS);
                Light light = GetMainLight();

                // Tops catch the light, bottoms are grey-blue.
                half3 sky = _VoxelSkyZenith.a > 0 ? _VoxelSkyZenith.rgb : half3(0.6, 0.7, 0.9);
                half3 horizon = _VoxelSkyHorizon.a > 0 ? _VoxelSkyHorizon.rgb : sky;
                half diffuse = saturate(dot(normal, light.direction)) * 0.6 + 0.4;
                half3 color = lerp(horizon * 0.75, sky * 0.5 + horizon * 0.6, normal.y * 0.5 + 0.5);
                color += light.color * diffuse * 0.45;

                float distance = length(input.positionWS.xz - _WorldSpaceCameraPos.xz);
                half fade = saturate(1.0 - (distance - _FadeDistance * 0.6) / (_FadeDistance * 0.4));

                // Blend into the horizon far away.
                color = lerp(horizon, color, saturate(fade * 1.5));

                return half4(color, _Opacity * fade);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
