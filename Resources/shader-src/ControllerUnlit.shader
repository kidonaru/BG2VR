Shader "BG2VR/ControllerUnlit"
{
    // VR コントローラ render model 用の unlit-opaque-textured shader。
    // ゲーム本体はライト依存 shader が VR eye パイプラインで真っ黒になり（実測 2026-06-08）、
    // unlit-opaque-textured な既製 shader は strip 済のため、AssetBundle で同梱する。
    // ZWrite On + Cull Back で solid（ボタン面が正しく描画）、_ZTest 既定 Always で UI より手前。
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 8 // Always=8 / LEqual=4
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2 // Back=2 / Off=0 / Front=1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" "IgnoreProjector"="True" }

        Pass
        {
            ZWrite On
            ZTest [_ZTest]
            // 既定 Back（コントローラは solid・既存挙動不変）。手は negative X scale で mirror するため
            // winding が反転して裏面化する → 手マテリアルは実行時に Off を設定（unlit フラットなので両面描画で破綻なし）。
            Cull [_Cull]
            Lighting Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv) * _Color;
            }
            ENDCG
        }
    }
}
