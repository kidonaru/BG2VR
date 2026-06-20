using System;
using GB;
using GB.Game;
using GB.Scene;
using UnityEngine;
using UnityVRMod.Core;
using BG2VR.SpatialVoice.SteamAudio;
using BG2VR.UiSceneVoid;

namespace BG2VR.SpatialVoice
{
    /// <summary>
    /// 空間化ボイス統括（spec 2026-06-20）。VR 中、実再生中の voice（[0]優先）を専用 GO へミラーし、
    /// Steam Audio HRTF で HMD 基準のバイノーラル定位を与える。本体 voice は音量 0 で Play 継続
    /// （lip-sync は timeSamples で生存）。非 VR / config OFF / native 失敗 / cast 未解決（head bone 不在）は
    /// 本体素 2D（回帰なし）。patch-free のポーリング runner（HeadLook / CameraFollow 同型）。
    /// </summary>
    internal sealed class SpatialVoiceRunner : MonoBehaviour
    {
        private const string HeadBoneName = "Head_skinJT";
        private static readonly Vector3 HeadFwdLocal = new Vector3(0f, 1f, 0f); // 頭ボーン前方（HeadLook 実測）

        private BinauralRenderer m_renderer;
        private AudioSource m_mirror;
        private VoiceHrtfFilter m_filter;
        private bool m_started;

        private bool m_engaged;
        private AudioSourceEx m_body;   // ミュート中の本体 source（復元対象）
        private AudioClip m_mirrorClip; // ミラー中の clip（変化検出）

        // head bone の単一エントリキャッシュ（話者は同時 1 人。switch / costume reload 時のみ再 DFS）
        private CharacterHandle m_cachedHandle;
        private GameObject m_cachedChara;
        private Transform m_cachedHead;

        private bool m_frameMismatchLogged;

        private void Start()
        {
            // CharID mirror（SpatialVoiceLogic.Speakers）が実 enum と一致しているか検証（drift 検出）。
            foreach (var sp in SpatialVoiceLogic.Speakers)
            {
                if (!Enum.TryParse<CharID>(sp.Name, out var e) || (int)e != sp.Id)
                    Plugin.Log.LogError($"[SpatialVoice] CharID mirror 不一致: {sp.Name}={sp.Id}（SpatialVoiceLogic.Speakers を実 enum に合わせて更新）。");
            }

            // native init（main スレッドで AudioSettings から sampleRate / frameSize 確定）。失敗してもベストエフォート（本体 2D）。
            var cfg = AudioSettings.GetConfiguration();
            m_renderer = new BinauralRenderer();
            m_renderer.Init(cfg.sampleRate, cfg.dspBufferSize);

            SetupMirror();
            m_started = true;
        }

        private void SetupMirror()
        {
            var go = new GameObject("BG2VR_VoiceHrtf");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(transform, false); // runner GO（DontDestroyOnLoad）の子として生存
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 0f; // Unity 非空間化（HRTF は自前 DSP）
            src.dopplerLevel = 0f;
            src.volume = 1f;       // 距離ゲインは filter 内で適用
            m_mirror = src;
            m_filter = go.AddComponent<VoiceHrtfFilter>();
            m_filter.Bind(m_renderer);
        }

        private void LateUpdate()
        {
            if (!m_started) return;

            bool cfgOn = Configs.VrSpatialVoice.Value;
            bool vrActive = VRModCore.IsVrActive;
            Camera eyeCam = vrActive ? VRModCore.GetVrEyeCamera() : null;
            bool nativeLoaded = m_renderer != null && m_renderer.Ready;

            // DSP ブロック長が init 時 frameSize と食い違うと HRTF が全バイパスされ無言で 2D 化する。一度だけ警告。
            if (nativeLoaded && m_renderer.FrameMismatch && !m_frameMismatchLogged)
            {
                m_frameMismatchLogged = true;
                Plugin.Log.LogWarning($"[SpatialVoice] DSP ブロック長が init 時（frameSize={m_renderer.FrameSize}）と不一致のため HRTF をバイパスしています（本体 2D）。AudioSettings の dspBufferSize を確認してください。");
            }

            AudioSourceEx body = FindPlayingVoice(); // 実再生 voice（[0]優先）
            bool voicePlaying = body != null;
            string clipName = voicePlaying && body.Source.clip != null ? body.Source.clip.name : null;

            // ASMR / 目隠し鬼 では空間化を抑制し本体素 2D に落とす（近接親密ボイス / ゲーム性のため・ユーザー指定 2026-06-20）。
            // ASMR はミニゲーム型（s_instance）+ 会話経由の寝 ASMR（clip 名 {CHAR}_5...）の両方で検出。
            bool contextAllowed = !(MiniGameProbe.IsAsmr() || MiniGameProbe.IsBlindFold()
                || (clipName != null && SpatialVoiceLogic.IsAsmrVoiceClip(clipName)));

            // 話者特定 → head bone 解決（口元 = head + 前方 offset）
            Transform head = null;
            Vector3 mouthWorld = default;
            if (voicePlaying && cfgOn && eyeCam != null && nativeLoaded && contextAllowed)
            {
                if (clipName != null && SpatialVoiceLogic.TryCharIdFromClipName(clipName, out int charIdInt))
                {
                    var env = GBSystem.Instance != null ? GBSystem.Instance.GetActiveEnvScene() : null;
                    var handle = FindHandle(env, charIdInt);
                    if (handle != null)
                    {
                        head = ResolveHead(handle, handle.Chara);
                        if (head != null)
                            mouthWorld = head.position + (head.rotation * HeadFwdLocal) * Configs.VoiceMouthForwardOffset.Value;
                    }
                }
            }
            bool castResolved = head != null;

            bool engage = SpatialVoiceLogic.ShouldEngage(cfgOn, vrActive, eyeCam != null, nativeLoaded, voicePlaying, castResolved, contextAllowed);

            if (engage)
            {
                body.Source.volume = 0f; // 本体ミュート（毎フレ再アサート）。lip-sync は timeSamples で生存。

                AudioClip clip = body.Source.clip;
                if (!m_engaged || !ReferenceEquals(m_body, body) || !ReferenceEquals(m_mirrorClip, clip))
                    StartMirror(body, clip);
                m_body = body;
                m_engaged = true;

                Transform eyeT = eyeCam.transform;
                Vector3 eyePos = eyeT.position;
                Vector3 dir = SpatialVoiceLogic.WorldToSteamAudioDir(mouthWorld, eyePos, eyeT.right, eyeT.up, eyeT.forward);
                float dist = Vector3.Distance(mouthWorld, eyePos);
                float gain = SpatialVoiceLogic.DistanceGain(dist, Configs.VoiceMinDistance.Value, Configs.VoiceMaxDistance.Value);
                m_filter.SetParams(dir, Configs.VoiceHrtfSpatialBlend.Value, gain, true);
            }
            else if (m_engaged)
            {
                Disengage();
            }
        }

        private static AudioSourceEx FindPlayingVoice()
        {
            var sm = GBSystem.Instance != null ? GBSystem.Instance.m_sound : null;
            var voices = sm != null ? sm.m_voice : null;
            if (voices == null) return null;
            for (int i = 0; i < voices.Count; i++) // [0]=main 優先 → [1]=sub
            {
                var ex = voices[i];
                if (ex != null && ex.Source != null && ex.Source.isPlaying && ex.Source.clip != null)
                    return ex;
            }
            return null;
        }

        private static CharacterHandle FindHandle(EnvSceneBase env, int charIdInt)
        {
            if (env == null || env.m_characters == null) return null;
            var target = (CharID)charIdInt;
            foreach (var h in env.m_characters)
            {
                if (h == null) continue;
                if (h.GetCharID() == target)
                {
                    var chara = h.Chara;
                    if (chara != null && chara.activeInHierarchy) return h;
                }
            }
            return null; // 該当キャラ不在（NUM / システム voice 等）= castResolved=false → 2D
        }

        private Transform ResolveHead(CharacterHandle handle, GameObject chara)
        {
            if (chara == null) return null;
            if (!ReferenceEquals(handle, m_cachedHandle) || !ReferenceEquals(chara, m_cachedChara))
            {
                m_cachedHandle = handle;
                m_cachedChara = chara;
                m_cachedHead = FindDeep(chara.transform, HeadBoneName);
            }
            return m_cachedHead;
        }

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

        private void StartMirror(AudioSourceEx body, AudioClip clip)
        {
            if (m_mirror == null || clip == null) return;
            m_mirror.clip = clip;
            m_mirror.pitch = body.Source.pitch;
            int ts = body.Source.timeSamples;
            m_mirror.timeSamples = clip.samples > 0 ? Mathf.Clamp(ts, 0, clip.samples - 1) : 0;
            m_mirror.Play();
            m_mirrorClip = clip;
        }

        private void Disengage()
        {
            if (m_mirror != null)
            {
                if (m_mirror.isPlaying) m_mirror.Stop();
                m_mirror.clip = null;
            }
            m_mirrorClip = null;
            m_filter?.SetParams(new Vector3(0f, 0f, -1f), 1f, 0f, false);
            RestoreBodyVolume();
            m_engaged = false;
            m_body = null;
        }

        private void RestoreBodyVolume()
        {
            if (m_body == null || m_body.Source == null) return;
            try
            {
                // ゲームと同一の dB カーブ + VolumeTweak で厳密復元（最新スライダー値）。
                SoundManager.ChangeVolume(m_body, GBSystem.Instance.RefSaveData().GetVoiceVolumeF());
            }
            catch
            {
                m_body.Source.volume = 1f; // SaveData 不在等のフォールバック
            }
        }

        private void OnDestroy()
        {
            if (m_engaged) Disengage();
            m_renderer?.Dispose();
        }
    }
}
