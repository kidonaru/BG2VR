using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityVRMod.Core;
using UnityVRMod.Features.Util;
using BG2VR.TransitionGuard;

namespace BG2VR.CameraBridge
{
    /// <summary>
    /// per-scene の正しい 3D カメラを自動解決し、fork の CameraFinder へ in-memory 参照で直接供給する
    /// （plan §3① / spec §5）。手動 config 無しで env ごとに違うカメラへ VR rig を乗せる。
    ///
    /// env のカメラは additive ロードや遷移完了の数フレーム後に現れるため、シーン変化/遷移完了を
    /// トリガに一定時間（<see cref="ResolveWindowSecs"/>）リトライしながら解決する。供給は文字列 config を
    /// 経由せず CameraFinder.SetAssertedCamera で直接渡す＝disk 書込・GameObject.Find 再解決・誤一致が無い。
    /// </summary>
    public sealed class CameraBridgeRunner : MonoBehaviour
    {
        // 既知の VR rig カメラ名（UnityVRMod-fork が生成）。これらは選定対象から除外する。
        private const string VrCameraNamePrefix = "XrVrCamera";
        private const string VrRigNameFragment = "UnityVRMod";

        // シーン変化後、カメラ出現を待ってリトライする時間。
        public float ResolveWindowSecs = 5.0f;

        private float _resolveDeadline = -1f;
        private Camera _lastSuppliedCamera = null; // 直近に fork へ供給した Camera（冪等比較用）
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
            // Watchdog: 供給済みカメラが解決不能になったら（旧 env unload で破棄/無効化）再解決を要求する。
            // 直接参照は Unity fake-null で破棄を検出でき、文字列 stale のような誤一致経路が無い。
            // FindGameCamera()==null は manager が rig を teardown する条件と一致＝必要十分なトリガ
            //（供給カメラ破棄かつ Camera.main fallback も不在のときに発火・検死 2026-06-09 経路を守る）。
            if (_lastSuppliedCamera != null && CameraFinder.FindGameCamera() == null)
            {
                Plugin.Log.LogInfo("[CameraBridge] 供給済みカメラが解決不能（env unload 等）。再解決を要求。");
                _lastSuppliedCamera = null;
                RequestResolve();
            }

            if (Time.time > _resolveDeadline) return;

            Camera best = ResolveBestCamera();
            if (best == null) return; // まだ出ていない → 窓内で次フレーム再試行

            if (best == _lastSuppliedCamera) { _resolveDeadline = -1f; return; } // 既供給＝冪等 no-op

            CameraFinder.SetAssertedCamera(best); // in-memory 直接ハンドオフ（disk 書込なし・Find 再解決なし。内部で InvalidateCache）
            _lastSuppliedCamera = best;
            _resolveDeadline = -1f; // 解決完了。窓を閉じる
            Plugin.Log.LogInfo($"[CameraBridge] VR カメラを供給: '{best.name}'（depth={best.depth}）");
        }

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
    }
}
