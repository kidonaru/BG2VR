using System.Collections.Generic;
using GB;
using GB.Scene;
using UnityEngine;
using UnityVRMod.Core;

namespace BG2VR.HeadLook
{
    /// <summary>
    /// キャラの顔/視線を HMD に向ける（spec 2026-06-06）。
    /// LateUpdate で Animator が書いた pose の上に Head_skinJT の追加回転を合成し、
    /// 虹彩は EyeUvDriver（mesh_eye uv0 シフト）で追従させる。
    /// 首は毎フレーム Animator が pose を上書きするため復元処理不要（T0 ⑤実証）。
    /// 目（mesh）は能動復元が必要（EyeUvDriver.Restore）。
    /// m_characters は publicize 参照で直接読む。EnvSceneBase.LateUpdate（lip sync/blink）は
    /// bone 非接触＝実行順は問わない（spec §2）。
    /// </summary>
    internal sealed class HeadLookRunner : MonoBehaviour
    {
        // ---- bone 名・ローカル軸（T0 ④実測。spec §2.1） ----
        private const string HeadBoneName = "Head_skinJT";
        private static readonly Vector3 HeadFwdLocal = new Vector3(0f, 1f, 0f);    // 実測: fwd=+Y
        private static readonly Vector3 HeadUpLocal = new Vector3(-1f, 0f, 0f);    // 実測: up=−X
        private static readonly Vector3 HeadRightLocal = new Vector3(0f, 0f, -1f); // 実測: right=−Z
        private const float PitchSign = -1f;     // T0 ④導出（+pitch=上 にするため反転。実機 OK 確認済 2026-06-06）
        // EyeUvPerDeg / EyeUvMax は Configs（F10 live チューニング可能）

        private sealed class CharEntry
        {
            public GameObject Chara;   // bone/SMR キャッシュの世代キー（衣装 reload で差し替わる）
            public Transform Head;
            public readonly EyeUvDriver Eye = new EyeUvDriver();
            public readonly LookAtState State = new LookAtState();
        }

        // key = CharacterHandle（シーンの寿命中安定。Chara は reload で変わるため entry 内で世代管理）
        private readonly Dictionary<CharacterHandle, CharEntry> m_entries =
            new Dictionary<CharacterHandle, CharEntry>();
        private readonly List<CharacterHandle> m_removeBuf = new List<CharacterHandle>();
        private bool m_activeLogged;

        private void LateUpdate()
        {
            bool wantHead = Configs.EnableHeadLook.Value;
            bool wantEye = Configs.EnableEyeLook.Value;
            bool active = (wantHead || wantEye) && VRModCore.IsVrActive;
            Camera eyeCam = active ? VRModCore.GetVrEyeCamera() : null;
            if (!active || eyeCam == null)
            {
                // 遷移 teardown 中/OFF: 目の uv を復元し状態を捨てる（首は Animator 上書きで自然復帰）
                if (m_entries.Count > 0) { RestoreAll(); m_entries.Clear(); }
                if (m_activeLogged) { Plugin.Log.LogInfo("[HeadLook] 停止"); m_activeLogged = false; }
                return;
            }
            var env = GBSystem.Instance != null ? GBSystem.Instance.GetActiveEnvScene() : null;
            if (env == null || env.m_characters == null) return;
            if (!m_activeLogged) { Plugin.Log.LogInfo("[HeadLook] 追従開始"); m_activeLogged = true; }

            Vector3 targetPos = eyeCam.transform.position;
            float dt = Time.deltaTime;
            // F10 live チューニング可能（solver は純関数のため解決済み値を Tuning で渡す）
            var tuning = new HeadLookSolver.Tuning
            {
                EngageYawDeg = Configs.EngageYawDeg.Value,
                EngagePitchDeg = Configs.EngagePitchDeg.Value,
                ReleaseYawMarginDeg = Configs.ReleaseYawMarginDeg.Value,
                ReleasePitchMarginDeg = Configs.ReleasePitchMarginDeg.Value,
                DeadZoneStartDeg = Configs.DeadZoneStartDeg.Value,
                DeadZoneStopDeg = Configs.DeadZoneStopDeg.Value,
                HeadTau = Configs.HeadTau.Value,
                EyeTau = Configs.EyeTau.Value,
                HeadRatio = Configs.HeadRatio.Value,
                EyeYawRatio = Configs.EyeYawRatio.Value,
                EyePitchRatio = Configs.EyePitchRatio.Value,
            };

            foreach (var handle in env.m_characters)
            {
                if (handle == null) continue;
                GameObject chara = handle.Chara;
                if (chara == null || !chara.activeInHierarchy) continue;
                CharEntry e = ResolveEntry(handle, chara);
                if (e == null || e.Head == null) continue; // bone 不在: ベストエフォート skip

                // Animator が書いた素ポーズの軸（world）
                Quaternion animLocal = e.Head.localRotation;
                Quaternion animWorld = e.Head.rotation;
                Vector3 fwd = animWorld * HeadFwdLocal;
                Vector3 up = animWorld * HeadUpLocal;
                Vector3 right = animWorld * HeadRightLocal;

                var ang = HeadLookSolver.ComputeOffsetAngles(e.Head.position, fwd, up, right, targetPos);
                var r = HeadLookSolver.Step(e.State, ang, dt, in tuning);

                if (wantHead)
                {
                    e.Head.localRotation = animLocal
                        * Quaternion.AngleAxis(r.HeadYawApplied, HeadUpLocal)
                        * Quaternion.AngleAxis(r.HeadPitchApplied * PitchSign, HeadRightLocal);
                }
                if (wantEye && e.Eye.IsValid)
                {
                    // +U=キャラの右（+yaw と一致）/ +V=下（+pitch=上 と逆）→ V は符号反転
                    float uvPerDeg = Configs.EyeUvPerDeg.Value;
                    float uvMax = Configs.EyeUvMax.Value;
                    var uv = new Vector2(
                        Mathf.Clamp(r.EyeYawApplied * uvPerDeg, -uvMax, uvMax),
                        Mathf.Clamp(-r.EyePitchApplied * uvPerDeg, -uvMax, uvMax));
                    e.Eye.Apply(chara, uv);
                }
                else
                {
                    e.Eye.Restore(); // EnableEyeLook OFF への live 切替で虹彩を素に戻す
                }
            }

            // 破棄済み entry の掃除（scene unload / reload。fake-null は Chara == null で検出）
            m_removeBuf.Clear();
            foreach (var kv in m_entries)
                if (kv.Value.Chara == null) m_removeBuf.Add(kv.Key);
            foreach (var k in m_removeBuf)
            {
                m_entries[k].Eye.Restore(); // smr 側も破棄済みなら内部で skip される
                m_entries.Remove(k);
            }
        }

        /// <summary>
        /// entry の取得 + Chara 世代チェック（衣装 reload で GameObject ごと差し替わるため、
        /// 参照が変わったら bone/SMR を再解決する。旧 clone は Resolve 内で復元・破棄）。
        /// </summary>
        private CharEntry ResolveEntry(CharacterHandle handle, GameObject chara)
        {
            if (!m_entries.TryGetValue(handle, out var e))
            {
                e = new CharEntry();
                m_entries.Add(handle, e);
            }
            if (!ReferenceEquals(e.Chara, chara))
            {
                e.Chara = chara;
                e.Head = FindDeep(chara.transform, HeadBoneName);
                e.Eye.Resolve(chara);
                if (e.Head == null)
                    Plugin.Log.LogWarning($"[HeadLook] {HeadBoneName} が見つかりません: {chara.name}（skip）");
            }
            return e;
        }

        /// <summary>名前一致の子孫 Transform を DFS 探索（per-instance 1 回だけ呼ばれる）。</summary>
        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = FindDeep(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        private void RestoreAll()
        {
            foreach (var kv in m_entries) kv.Value.Eye.Restore();
        }

        private void OnDestroy() => RestoreAll();
    }
}
