using UnityEngine;
using UnityVRMod.Core;

namespace BG2VR.VrInput
{
    /// <summary>指ベンドのチューニングパラメータ（全 Config 解決済み値）。Runner が毎フレ Configs から構築して渡す。</summary>
    internal struct FingerCurlParams
    {
        public float InitialDeg;       // 人差し/中/薬/小指の入力0(指を離した状態)の関節角（remap の下端）
        public float ThumbInitialDeg;  // 親指の入力0の関節角（親指は別指定）
        public float MaxDeg;           // 人差し/中/薬/小指の入力1の関節角
        public float ThumbMaxDeg;      // 親指の入力1の関節角
        public float Tau;              // EMA 平滑時定数(秒)
    }

    /// <summary>
    /// ハンドモデル（skin 済み手 FBX）の指ボーンを、トリガー値（人差し指）/ グリップ値（親・中・薬・小指）で
    /// 毎フレ曲げる。手GO は MOD が Instantiate した自前オブジェクトのため復元不要（rest から毎フレ再計算して
    /// localRotation を上書きするだけ・Animator は Runner が無効化済み）。
    /// 曲げは各関節の rest localRotation の上から local 曲げ軸まわりに回す（fan-out を保持したまま flexion）。
    /// 入力は dt 補正 EMA で平滑（FingerCurlMath）。曲げ角の Quaternion 化は AngleAxis（native）のため本クラス（実機）で行う。
    /// リグ同定は実測（2026-06-10・BG2DevBridge）: Bone.001=掌、その子 Bone.002=親指/006=人差し/009=中/012=薬/015=小。
    /// 各ボーンは local +Y に沿うため flexion は local X 軸まわり。
    /// </summary>
    internal sealed class HandFingerPoser
    {
        // 曲げ軸（構造定数・リグ依存）。Player hand「Hand for VR」リグの bone 軸は実機で要再確認。
        // 開始値 -X（前モデル hands v1 の確定値）。NG なら bridge で index02_R を試曲げして軸/符号を確定する。
        private static readonly Vector3 CurlAxis = new Vector3(-1f, 0f, 0f);

        // mirror（右手＝negative-X scale GO）側の curl 角符号。実機確認で両手が同じ向きに曲がる（左右非対称なし）と
        // 確定したため 1＝補正なし（2026-06-10）。negative-X scale は curl 向きを反転させなかった。
        // 注意: curl は純 X 軸回転なので ControllerModelPose.MirrorRotationX は no-op（y,z=0）＝鏡映補正にならない。
        private const float MirrorCurlSign = 1f;

        // 手ルートと各指の可動ボーン名（実測: Player hand「Hand for VR」by FFeller・2026-06-11 strings 採取）。
        // 各指は 00(中手骨)+01/02/03(指骨)。中手骨 00 は曲げず 01/02/03 の3関節を curl。親指は 01/02/03（00 なし）。
        private const string RootBoneName = "hand_R";
        private static readonly string[] ThumbBones  = { "thumb01_R", "thumb02_R", "thumb03_R" };
        private static readonly string[] IndexBones  = { "index01_R", "index02_R", "index03_R" };
        private static readonly string[] MiddleBones = { "middle01_R", "middle02_R", "middle03_R" };
        private static readonly string[] RingBones   = { "ring01_R", "ring02_R", "ring03_R" };
        private static readonly string[] PinkyBones  = { "pinky01_R", "pinky02_R", "pinky03_R" };

        private sealed class Finger
        {
            public Transform[] Bones;   // 可動ボーン（中手骨除く・root→tip 順）
            public Quaternion[] Rest;   // 各ボーンの rest localRotation
        }

        private Finger m_thumb, m_index, m_middle, m_ring, m_pinky;
        private bool m_built;
        private bool m_warned;
        // 平滑状態（手単位）。trigger=人差し指 / grip=他4本。
        private float m_triggerCurl, m_gripCurl;

        /// <summary>手GO の指ボーン+rest をキャッシュ（GO 構築直後・Animator 無効化後に呼ぶ）。
        /// 指ボーンが見つからない（旧 bundle の剛体 / 未skin / 構造変化）場合は no-op 化（クラッシュさせない）。</summary>
        public void Build(GameObject handGo)
        {
            m_built = false;
            m_thumb = m_index = m_middle = m_ring = m_pinky = null;
            m_triggerCurl = m_gripCurl = 0f;

            Transform root = FindByName(handGo.transform, RootBoneName);
            if (root == null)
            {
                if (!m_warned) { m_warned = true; Plugin.Log.LogWarning($"[HandFinger] 手ルート {RootBoneName} 不在（旧bundle/未skin/別リグ?）。指ベンド無効。"); }
                return;
            }
            m_thumb = BuildFinger(root, ThumbBones);
            m_index = BuildFinger(root, IndexBones);
            m_middle = BuildFinger(root, MiddleBones);
            m_ring = BuildFinger(root, RingBones);
            m_pinky = BuildFinger(root, PinkyBones);
            m_built = m_index != null;  // 最低限 人差し指が取れれば有効とみなす
            if (!m_built && !m_warned) { m_warned = true; Plugin.Log.LogWarning("[HandFinger] 指ボーン不在。指ベンド無効。"); }
        }

        /// <summary>毎フレ呼ぶ。人差し指=trigger / 親中薬小=grip のアナログ値で各関節を曲げる。
        /// mirror=true（右手）は curl 角を MirrorCurlSign で符号調整して左右対称に。</summary>
        public void Pose(in VrControllerSnapshot snap, bool mirror, float dt, in FingerCurlParams p)
        {
            if (!m_built) return;
            // 非表示中（未接続/非ready）は bone を書かない。平滑状態を 0 にリセットして
            // 再接続の初フレームに前回の握りポーズが一瞬残らないようにする（GO は SetActive(false) 側で非表示）。
            if (!snap.Valid) { m_triggerCurl = m_gripCurl = 0f; return; }
            m_triggerCurl = FingerCurlMath.Smooth(m_triggerCurl, Mathf.Clamp01(snap.TriggerValue), dt, p.Tau);
            m_gripCurl = FingerCurlMath.Smooth(m_gripCurl, Mathf.Clamp01(snap.GripValue), dt, p.Tau);

            ApplyFinger(m_index, m_triggerCurl, p.InitialDeg, p.MaxDeg, mirror);
            ApplyFinger(m_middle, m_gripCurl, p.InitialDeg, p.MaxDeg, mirror);
            ApplyFinger(m_ring, m_gripCurl, p.InitialDeg, p.MaxDeg, mirror);
            ApplyFinger(m_pinky, m_gripCurl, p.InitialDeg, p.MaxDeg, mirror);
            ApplyFinger(m_thumb, m_gripCurl, p.ThumbInitialDeg, p.ThumbMaxDeg, mirror); // 親指は初期角・最大角とも専用
        }

        /// <summary>指ベンド無効時に rest（伸びた状態）へ戻す（毎フレ呼んで冪等）。</summary>
        public void Relax()
        {
            if (!m_built) return;
            m_triggerCurl = m_gripCurl = 0f;
            RestFinger(m_thumb); RestFinger(m_index); RestFinger(m_middle); RestFinger(m_ring); RestFinger(m_pinky);
        }

        private static void ApplyFinger(Finger f, float curl01, float initialDeg, float maxDeg, bool mirror)
        {
            if (f == null) return;
            // mirror 側は curl 角を符号反転して左右対称に（純 X 軸 curl のため符号反転が正しい補正・MirrorCurlSign 参照）。
            float angle = FingerCurlMath.CurlAngle(curl01, initialDeg, maxDeg) * (mirror ? MirrorCurlSign : 1f);
            Quaternion curl = Quaternion.AngleAxis(angle, CurlAxis); // native ECall（実機のみ）
            for (int i = 0; i < f.Bones.Length; i++)
                f.Bones[i].localRotation = f.Rest[i] * curl;
        }

        private static void RestFinger(Finger f)
        {
            if (f == null) return;
            for (int i = 0; i < f.Bones.Length; i++)
                f.Bones[i].localRotation = f.Rest[i];
        }

        private static Finger BuildFinger(Transform root, string[] boneNames)
        {
            // 指の可動ボーンを名前で個別解決（中手骨 00 は名前リストに含めない＝曲げない）。1本でも欠ければ無効。
            var bones = new Transform[boneNames.Length];
            var rest = new Quaternion[boneNames.Length];
            for (int i = 0; i < boneNames.Length; i++)
            {
                Transform t = FindByName(root, boneNames[i]);
                if (t == null) return null;
                bones[i] = t;
                rest[i] = t.localRotation;
            }
            return new Finger { Bones = bones, Rest = rest };
        }

        private static Transform FindByName(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
