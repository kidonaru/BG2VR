using UnityEngine;
using UnityVRMod.Core;

namespace BG2VR.HandLighting
{
    /// <summary>
    /// 手モデル専用 Toon shader（BG2VR/HandToonOverlay）に自前 directional light の方向と色を
    /// global uniform で push する runner。Bar `Directional Light_sun` 実測値（実機 bridge 2026-06-19・
    /// color=(1, 0.941, 0.910) / intensity=0.63 / rot=(73.06, 201.93, 264.60)）を採用＝キャストと馴染む温かい白光。
    ///
    /// 設計判断: Unity Light component を spawn せず global uniform 経由にする理由＝
    /// 1) HandToonOverlay は LightMode 空 pass で global uniform を直接読む（per-material 設定不要）
    /// 2) Light component を rig 子に置くと scene の全 layer を照らす可能性（cullingMask=1&lt;&lt;28 でも sloppy な
    ///    URP 内 light enumeration で副作用がありえる）。global uniform なら shader 側でのみ参照され副作用ゼロ
    /// 3) VR モデル overlay channel（fork の SetVrModelOverlay）と同じく毎フレ push する形式に統一
    ///
    /// layer 28 (HandLighting) の契約再定義（2026-06-19・plan-review 反映）:
    /// 本 runner の global uniform 経路は scene light と完全独立＝当初の「scene 光からの isolation」目的は撤廃。
    /// layer 28 の存在意義は **fork SetVrModelOverlay の overlay 描画チャネル選択**（main pass 除外+overlay 描画）
    /// のみ。`HandLayerResolver` / `EyeCullingPolicy` / `VrLayers.HandLightingMask` 等の依存は維持するが、
    /// 設計動機は「light 隔離」でなく「overlay 描画チャネル」であることを示す。
    ///
    /// gating: VRModCore.IsVrActive=false は SetVrModelOverlay(0) で fork の overlay を解除し、
    /// uniform も neutral（dir=(0,1,0) / color=black）で push＝rig teardown→再生成サイクルの最初の数フレが
    /// 前セッションの stale 値で想定外の色になるのを防ぐ（neutral push の主目的）。
    /// </summary>
    internal sealed class HandLightingRunner : MonoBehaviour
    {
        // Bar (HoleScene) の `Directional Light_sun`（cullingMask=0x100=layer 8 only＝キャスト専用主光源）の
        // 実測値（bridge 採取 2026-06-19）。キャストと馴染む温かい白光。
        // 「光が来る方向」= -Light.forward（directional は forward が光の進行方向）。
        // rot=(73.06, 201.93, 264.60) → forward を計算して world space で固定（rig 子でなく world 直＝
        // プレイヤー回転で光向きは変わらない）。
        private static readonly Quaternion LightRotation = Quaternion.Euler(73.06f, 201.93f, 264.60f);
        private static readonly Color LightColor = new Color(1.000f, 0.941f, 0.910f, 1f);
        private const float LightIntensity = 0.63f;

        // shader uniform 名（HandToonOverlay と一致＝片方変更時はもう片方も直す）。
        private static readonly int s_lightDirId = Shader.PropertyToID("_BG2VR_HandLightDir");
        private static readonly int s_lightColorId = Shader.PropertyToID("_BG2VR_HandLightColor");

        private void Update()
        {
            // fork に VR モデル overlay layer を push（PostProcess 有無と独立に layer 28 が常時最前面 overlay 描画）。
            // VR 非 active 時は明示的にクリア（rig teardown 後の stale を残さない）。
            VRModCore.SetVrModelOverlay(VRModCore.IsVrActive ? VrLayers.HandLightingMask : 0);

            if (!VRModCore.IsVrActive)
            {
                // neutral 値で push＝手 Material 経由の見た目を flat に（light 当たらず shade color のみ）。
                Shader.SetGlobalVector(s_lightDirId, new Vector4(0f, 1f, 0f, 0f));
                Shader.SetGlobalColor(s_lightColorId, Color.black);
                return;
            }

            // directional の forward は「光が進む方向」。shader 側は「光が来る方向」を期待する＝符号反転。
            Vector3 lightDir = -(LightRotation * Vector3.forward);
            Shader.SetGlobalVector(s_lightDirId, new Vector4(lightDir.x, lightDir.y, lightDir.z, 0f));
            // color × intensity を rgb に乗せて shader へ（shader は _BG2VR_HandLightColor.rgb のみ参照＝alpha 未使用）。
            Shader.SetGlobalColor(s_lightColorId, LightColor * LightIntensity);
        }
    }
}
