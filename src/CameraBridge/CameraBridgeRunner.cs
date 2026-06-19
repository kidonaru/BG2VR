using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityVRMod.Config;
using UnityVRMod.Core;
using UnityVRMod.Features.Util;
using BG2VR.TransitionGuard;

namespace BG2VR.CameraBridge
{
    /// <summary>
    /// per-scene の正しい 3D カメラを自動解決し、UnityVRMod の AssertedCameraOverrides に供給する
    /// （plan §3① / spec §5）。手動 config 無しで env ごとに違うカメラ名へ VR rig を乗せる。
    ///
    /// env のカメラは additive ロードや遷移完了の数フレーム後に現れるため、シーン変化/遷移完了を
    /// トリガに一定時間（<see cref="ResolveWindowSecs"/>）リトライしながら解決する。
    /// </summary>
    public sealed class CameraBridgeRunner : MonoBehaviour
    {
        // 既知の VR rig カメラ名（UnityVRMod-fork が生成）。これらは選定対象から除外する。
        private const string VrCameraNamePrefix = "XrVrCamera";
        private const string VrRigNameFragment = "UnityVRMod";

        // シーン変化後、カメラ出現を待ってリトライする時間。
        public float ResolveWindowSecs = 5.0f;
        // override 書込（= .cfg の同期 disk 書込）のバースト抑制クールダウン。
        public float WriteCooldownSecs = 0.5f;

        private float _resolveDeadline = -1f;
        private float _lastWriteTime = -1000f; // 起動直後の初回書込をブロックしない初期値
        private string _lastWrittenOverride = null;
        private readonly List<CameraCandidate> _candidateBuffer = new List<CameraCandidate>();

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            // scene イベントが発火しない no-op 遷移用の保険。RequestResolve は idempotent
            // （解決窓を開け直すだけ）なので scene イベントと二重に来ても無害。
            TransitionGuardRunner.TransitionEnded += RequestResolve;
            RequestResolve(); // 起動直後に一度解決を試みる
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            TransitionGuardRunner.TransitionEnded -= RequestResolve;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => RequestResolve();
        private void OnActiveSceneChanged(Scene from, Scene to) => RequestResolve();

        /// <summary>解決リトライ窓を開く（再解決を要求）。</summary>
        public void RequestResolve() => _resolveDeadline = Time.time + ResolveWindowSecs;

        private void Update()
        {
            // Watchdog: 供給済み override が解決不能になったら（旧 env unload で対象カメラ消失等）再解決を要求する。
            // date/VIP→Bar 復帰では active scene が先に切り替わり、旧 env のカメラがまだ生きている隙にそれを掴んで
            // 窓を閉じる→旧 env unload で override が stale 化→fork が null→manager が rig teardown→固着、という
            // 経路があった（検死 2026-06-09: 例 'HoleFix|/EveningProxy/Evening/GameCamera'＝Bar シーンにデートの
            // カメラパス）。FindGameCamera()==null は manager が rig を teardown する条件と一致＝必要十分なトリガ。
            // 発火後 _lastWrittenOverride=null にするので、再書込まで本枝は再点火しない（ログ/RequestResolve は 1 回）。
            if (_lastWrittenOverride != null && CameraFinder.FindGameCamera() == null)
            {
                Plugin.Log.LogInfo($"[CameraBridge] 供給済みカメラが解決不能（env unload 等）。再解決を要求。旧 override='{_lastWrittenOverride}'");
                _lastWrittenOverride = null;   // equality 抑制を解除して再書込を許可
                RequestResolve();
            }

            if (Time.time > _resolveDeadline) return;

            Camera best = ResolveBestCamera();
            if (best == null) return; // まだ出ていない → 窓内で次フレーム再試行

            string path = BuildHierarchyPath(best.transform);

            // 書いたパスが GameObject.Find で実際に best 本体へ解決し直せるか自己検証する。
            // additive ロードで同名 root が複数あると Find が別 GO に誤一致しうる（fork の
            // FindGameCameraInternal も GameObject.Find を使う）。誤一致時は誤カメラへ VR を乗せるより、
            // override を書かず Camera.main fallback に委ねる方が安全。
            var resolved = GameObject.Find(path);
            if (resolved == null || resolved.GetComponent<Camera>() != best)
            {
                Plugin.Log.LogWarning($"[CameraBridge] パス '{path}' が対象カメラへ一意解決できない（同名 GO 衝突の可能性）。override 書込をスキップ。");
                return; // 窓は開けたまま。別フレームでカメラ構成が変わる可能性に賭ける
            }

            // FindGameCamera は活性シーン名で gate するので Scene 付きで書く（spec §5）。
            string activeScene = SceneManager.GetActiveScene().name;
            string overrideValue = $"{activeScene}{ScenePathSeparator}{path}";

            if (overrideValue == _lastWrittenOverride) { _resolveDeadline = -1f; return; }

            // ConfigElement.Value のセッターは BepInEx 既定で .cfg を同期 disk 書込する。
            // 解決トリガ（scene イベント等）が連続発火しても書込がバーストしないようクールダウンで coalesce する。
            if (Time.time - _lastWriteTime < WriteCooldownSecs) return; // 窓は開けたまま次フレームへ持ち越し

            ConfigManager.AssertedCameraOverrides.Value = overrideValue;
            CameraFinder.InvalidateCache();
            _lastWrittenOverride = overrideValue;
            _lastWriteTime = Time.time;
            _resolveDeadline = -1f; // 解決完了。窓を閉じる
            Plugin.Log.LogInfo($"[CameraBridge] VR カメラを供給: '{overrideValue}'（depth={best.depth}）");
        }

        private const char ScenePathSeparator = '|';

        private Camera ResolveBestCamera()
        {
            // Camera.allCameras は enabled なカメラのみ返す（Unity 仕様）。VR は現に描画中のカメラへ
            // 乗せるべきなので、これを意図的な列挙ソースにする。一時的に enabled=false の正解カメラは
            // ResolveWindow のリトライで出現を待つ（CameraSelector 側の ActiveAndEnabled フィルタは
            // 列挙ソース非依存のテスト可能性のため残す）。
            Camera[] cams = Camera.allCameras;
            _candidateBuffer.Clear();
            for (int i = 0; i < cams.Length; i++)
            {
                Camera cam = cams[i];
                string n = cam.name;
                bool nameExcluded = n.StartsWith(VrCameraNamePrefix) || n.Contains(VrRigNameFragment);
                _candidateBuffer.Add(new CameraCandidate(
                    index: i,
                    activeAndEnabled: cam.isActiveAndEnabled,
                    hasTargetTexture: cam.targetTexture != null,
                    // fork の desktop 抑制(mask=0)で 3D カメラ識別が盲目化するのを防ぐため、抑制前の
                    // 真値 cullingMask を使う（非抑制カメラは live 値が返る）。
                    cullingMask: VRModCore.GetEffectiveCullingMask(cam),
                    depth: cam.depth,
                    nameExcluded: nameExcluded));
            }

            int best = CameraSelector.SelectBestIndex(_candidateBuffer);
            return best >= 0 ? cams[best] : null;
        }

        /// <summary>GameObject.Find で厳密一致させるためのルート起点パス（/Root/.../Name）を作る。</summary>
        private static string BuildHierarchyPath(Transform t)
        {
            var sb = new StringBuilder();
            BuildHierarchyPathRec(t, sb);
            return sb.ToString();
        }

        private static void BuildHierarchyPathRec(Transform t, StringBuilder sb)
        {
            if (t.parent != null) BuildHierarchyPathRec(t.parent, sb);
            sb.Append('/');
            sb.Append(t.name);
        }
    }
}
