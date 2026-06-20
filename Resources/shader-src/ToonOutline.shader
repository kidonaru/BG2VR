Shader "BG2VR/ToonOutline"
{
    // アニメ調アウトライン（inverted-hull）。本体モデルとは別レンダラーとして描く専用 shader。
    // 裏面（Cull Front）を法線方向に world space で膨張させ、solid 色で塗る＝シルエット輪郭になる。
    // 単一パス（LightMode 空）＝URP の eye main pass（プロップ・先頭1パス採用）でも fork の
    // CommandBuffer overlay（手・FindRenderablePass=0）でも描画される（同 shader へのパス追加方式が
    // 両経路とも不可なため別レンダラー化＝spec 2026-06-20）。
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth ("Outline Width (m)", Float) = 0.002
        // Cull は実行時に設定（inverted-hull は裏面＝Front。手は invertCulling+mirror で per-hand 出し分けの可能性）。
        _Cull ("Cull Mode", Float) = 1
        // ZTest も実行時に設定（手=GreaterEqual の reversed-Z / プロップ=LessEqual＝本体 toon と同じ出し分け）。
        _ZTest ("ZTest", Float) = 4
        _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            // LightMode 空＝URP/HDRP context 不要。CommandBuffer.DrawRenderer / URP SRPDefaultUnlit の両方で描画可能。
            Cull [_Cull]
            ZTest [_ZTest]
            ZWrite [_ZWrite]

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

            float4 _OutlineColor;
            float _OutlineWidth;

            v2f vert (appdata v)
            {
                v2f o;
                // world space で法線方向に膨張＝スケール非依存（uniform scale のプロップ・負 X mirror の手とも
                // inverse-transpose 法線で太さが安定する）。
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 worldNormal = normalize(UnityObjectToWorldNormal(v.normal));
                worldPos += worldNormal * _OutlineWidth;
                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return _OutlineColor; // solid（LDR・Bloom 既定色は暗色＝発光しない）
            }
            ENDCG
        }
    }
    Fallback Off
}
