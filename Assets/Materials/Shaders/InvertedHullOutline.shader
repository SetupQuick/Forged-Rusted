Shader "Custom/InvertedHullOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 1, 0, 1)
        _OutlineWidth ("Outline Width", Range(0.001, 0.05)) = 0.015
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Transparent+100" }
        Cull Front // Render inside-out geometry for the silhouette
        ZWrite On
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            fixed4 _OutlineColor;
            float _OutlineWidth;

            v2f vert(appdata v)
            {
                v2f o;
                // Push vertices outward along vertex normals
                float3 norm = normalize(v.normal);
                v.vertex.xyz += norm * _OutlineWidth;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return _OutlineColor;
            }
            ENDCG
        }
    }
}