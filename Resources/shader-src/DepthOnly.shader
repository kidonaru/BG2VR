Shader "BG2VR/DepthOnly"
{
    // 選択的深度プリパス用の「深度のみ書き込み」shader（ColorMask 0＝色を一切書かない）。
    // VrControllerOccludeUi が ON のとき、fork の DrawEyeOverlay が UI 描画の直前に
    // 「コントローラ層(29)だけ」をこの material で描き直し、UI(ZTest) の遮蔽源を
    // コントローラのみに限定する（机/キャラ等シーンは遮蔽源にしない＝選択的深度）。
    // _ZTest は実行時に reversed-Z（D3D 等）なら GEqual(7)、非 reversed なら LEqual(4) を設定する
    //（depth-only clear した far から見て「より近い側」を残すため。eye RT は usesReversedZBuffer に従う）。
    Properties
    {
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 7 // GEqual=7（reversed-Z 既定）/ LEqual=4
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "IgnoreProjector"="True" }

        Pass
        {
            ZWrite On
            ZTest [_ZTest]   // 近い側を残す（clear=far の上にコントローラ深度を書く・自己遮蔽も最も近い面が残る）
            ColorMask 0      // 色は書かない＝post 済み RT の color を温存
            Cull Off         // 手モデルの mirror（negative X scale）でも両面で深度を埋める
            Lighting Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return fixed4(0, 0, 0, 0); // ColorMask 0 で破棄される（深度のみ書く）
            }
            ENDCG
        }
    }
}
