Shader "BG2VR/HandToonOverlay"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _Color ("Tint Color", Color) = (1,1,1,1)
        _1st_ShadeColor ("Shade Color", Color) = (0.7, 0.6, 0.5, 1)
        _BaseColor_Step ("Shade Threshold (NdotL)", Range(0, 1)) = 0.5
        // 影と光の境界のフェード幅。0=ハードな 2-tone（step 同等）／大きいほどなめらか。
        // 実行時に per-hand/per-prop の Config 値を書く（手は既定 0＝従来どおりハード）。
        _ShadeFeather ("Shade Feather (0=hard)", Range(0, 0.5)) = 0
        _RimColor ("Rim Color", Color) = (1, 0.9, 0.8, 0.5)
        _RimLight_Power ("Rim Power", Range(0.5, 10)) = 2.0
        _MatCap_Sampler ("MatCap Texture", 2D) = "black" {}
        _MatCap_Intensity ("MatCap Intensity", Range(0, 2)) = 1.0
        // 発光（emission）。既定 0＝手は完全に不変（frag に +0）。発光プロップ（サイリウム）用に
        // runtime で HDR 値（>1）を書き込むと内部 HDR バッファ経由で Bloom に拾われ発光する。
        [HDR] _EmissionColor ("Emission (HDR)", Color) = (0,0,0,1)
        // Cull は実行時に per-hand 設定（mirror=negative X scale 側=Front / 通常側=Back）＝ControllerModelRunner。
        _Cull ("Cull Mode", Float) = 0
        // ZTest = 7 (GreaterEqual): reversed-Z（D3D far=0）で「近い方が勝つ」深度テスト。ZWrite On と併用で
        // 手の凹形状（指↔手のひら）の自己オクルージョンが成立し裏面透けを解消する。実行時に Resolver が同値を上書き。
        // LEqual は reversed-Z で near を弾き手が全消失・Always は深度ソート不能で透ける（実機検証 2026-06-19）。
        _ZTest ("ZTest", Float) = 7
        _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            // LightMode 空＝URP/HDRP の RenderPipeline context 不要。CommandBuffer.DrawRenderer 経由で描画可能。
            // BG2VR fork の DrawEyeOverlay（post 後 CommandBuffer overlay）で機能する唯一の pass 形式。
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
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 viewDir : TEXCOORD2;
                float3 viewNormal : TEXCOORD3;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float4 _1st_ShadeColor;
            float _BaseColor_Step;
            float _ShadeFeather;
            float4 _RimColor;
            float _RimLight_Power;
            sampler2D _MatCap_Sampler;
            float _MatCap_Intensity;
            // 発光色（HDR）。既定 0 で手は無影響。サイリウム発光時のみ runtime で >1 を書く。
            half4 _EmissionColor;

            // BG2VR HandLightingRunner が Shader.SetGlobalVector/Color で push する自前 directional light。
            // world space の方向（光が来る方向 = -light.forward）と color*intensity。
            uniform float4 _BG2VR_HandLightDir;
            uniform float4 _BG2VR_HandLightColor;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir = normalize(_WorldSpaceCameraPos - worldPos);
                // View-space normal で MatCap UV を作る（球面ハイライト風）。
                o.viewNormal = mul((float3x3)UNITY_MATRIX_V, o.worldNormal);
                return o;
            }

            // 戻り型は half4（fixed4 ではない）。一部コンパイラ/プラットフォームで fixed が [0,1] に飽和し
            // HDR emission(>1) が内部 HDR バッファへ届かず Bloom に拾われなくなる実装差を避ける（必須）。
            half4 frag (v2f i) : SV_Target
            {
                float3 N = normalize(i.worldNormal);
                float3 L = normalize(_BG2VR_HandLightDir.xyz);
                float3 V = normalize(i.viewDir);

                // base = tex × tint
                fixed4 albedo = tex2D(_MainTex, i.uv) * _Color;

                // 2-tone shade: NdotL が threshold 以上で base、未満で shade tint で base を乗算。
                // _ShadeFeather で境界をソフト化。w=max(feather,1e-3) で feather=0 でもほぼハード step に
                // degrade（min==max の 0/0 を回避）＝手の既定（feather 0）は従来 step とほぼ同一。
                float ndl = saturate(dot(N, L));
                float w = max(_ShadeFeather, 1e-3);
                float lit = smoothstep(_BaseColor_Step - w, _BaseColor_Step + w, ndl);
                fixed3 baseLit = lerp(_1st_ShadeColor.rgb * albedo.rgb, albedo.rgb, lit);

                // light color × intensity を反映（global uniform）
                baseLit *= _BG2VR_HandLightColor.rgb;

                // Rim: view normal の grazing angle で発光（カスト UTS 互換）
                float rim = pow(1.0 - saturate(dot(N, V)), _RimLight_Power);
                fixed3 rimAdd = rim * _RimColor.rgb * _RimColor.a;

                // MatCap: view-space normal を UV にして球面 tex を sample → blend
                // MatCap は view-space normal でサンプル＝素体 UV 非依存。Cast 由来 matcap_skin tex (256x256・
                // 球面ハイライト) をそのまま継承可能（手 UV と一致しなくても問題なし）。
                // 強度は HandMatCapIntensity Config で live 反映（F10 で実機調整可能）。
                float2 mcuv = i.viewNormal.xy * 0.5 + 0.5;
                fixed3 mcap = tex2D(_MatCap_Sampler, mcuv).rgb * _MatCap_Intensity;

                // emission を加算（既定 0＝手は不変 / サイリウム発光部のみ HDR 値が乗る）。
                half3 final = baseLit + rimAdd + mcap + _EmissionColor.rgb;
                return half4(final, albedo.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
