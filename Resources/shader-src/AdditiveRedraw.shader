Shader "BG2VR/AdditiveRedraw"
{
    // ScriptableRenderPass (AfterRenderingOpaques) で加算透過材質を再描画する shader。
    // ZWrite On で depth を書くことで後続の skybox pass による上書きを防ぐ。
    // emission luminance が低いフラグメントは clip して skybox に穴を空けない。
    // HW ZTest LEqual で壁遮蔽。
    Properties
    {
        _BaseMap ("Base Texture", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        [HDR] _EmissionColor ("Emission Color (HDR)", Color) = (0,0,0,0)
        _EmissionMap ("Emission Map", 2D) = "black" {}
        _ClipThreshold ("Clip Threshold", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }

        Pass
        {
            Blend One One
            ZWrite On
            ZTest LEqual
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            sampler2D _BaseMap;
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _EmissionColor;
            sampler2D _EmissionMap;
            float _ClipThreshold;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 base = tex2D(_BaseMap, i.uv) * _BaseColor;
                half3 emission = tex2D(_EmissionMap, i.uv).rgb * _EmissionColor.rgb;
                half3 color = base.rgb + emission;

                // emission luminance が低いフラグメントは discard（skybox に穴を空けない）
                half lum = dot(color, half3(0.299, 0.587, 0.114));
                clip(lum - _ClipThreshold);

                return half4(color, 0);
            }
            ENDCG
        }
    }
    Fallback Off
}
