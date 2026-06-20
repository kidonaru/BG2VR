using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GB;
using UnityEngine;

namespace BG2VR.Utils
{
    /// <summary>
    /// ゲーム本体の ConfirmDialog (GBSystem.GetConfirmDialog) を使った自動クローズ式トースト通知
    /// （spec §3.1）。OK ボタン1個で表示し、autoCloseSec 経過 or OK 押下のどちらか早い方で閉じる。
    /// ゲーム UI なので WorldUiProjector 経由で VR に表示され、OK は GbInputBridge 経由の VR ボタンで届く。
    /// 既に他ダイアログ表示中（IsActive）はスキップしてゲーム進行を壊さない＝連打も自然に抑止する
    /// （Enter 直後に gameObject.activeSelf=true になるため 2 回目の呼び出しは即 return）。
    /// 型解決/表示の失敗はログのみ（abort しないベストエフォート・FixMod 版 ConfirmDialogHelper と同方針）。
    /// 既知の制約: ConfirmDialog は GBSystem の共有シングルトン。トースト表示中（autoCloseSec 待機中）に
    /// ゲーム側が同シングルトンへ別ダイアログを Enter した場合、deadline 満了時の Exit がそれを巻き込んで
    /// 閉じ得る。pinned 操作はユーザ任意 hotkey で 2 秒窓に確認ダイアログが重なる確率は低く未観測のため、
    /// 対症コードは入れない（予防コード回避方針）。実機で再現したら toast 専有フラグで Exit 対象を限定する。
    /// </summary>
    public static class ConfirmDialogHelper
    {
        public static async UniTask ShowInfoAsync(string text, float autoCloseSec, CancellationToken ct = default)
        {
            ConfirmDialog dialog;
            try
            {
                dialog = GBSystem.Instance?.GetConfirmDialog();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[PinnedPoseToast] ダイアログ取得失敗: {ex.Message}");
                return;
            }
            // 未初期化 / 既に何かダイアログ表示中はスキップ（ゲーム進行を壊さない・連打抑止）。
            if (dialog == null || dialog.IsActive()) return;

            try
            {
                dialog.SetTextWithoutMSGID(text);
                dialog.SetYesOnly();
                dialog.Enter();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[PinnedPoseToast] ダイアログ表示失敗: {ex.Message}");
                return;
            }

            // 自動クローズ（autoCloseSec 経過）と OK 押下（IsSelected）のどちらか早い方まで待つ。
            // WhenAny ではなく単発 WaitUntil + デッドラインにする（UniTask バージョン差で非ジェネリック
            // 多重 WhenAny の有無が不安定なため・FixMod ConfirmDialogHelper も WaitUntil 単発で実績）。
            // unscaled 時刻で計る＝ダイアログ自体が独立 update（isIndependentUpdate）で時間も unscaled のため。
            float deadline = Time.unscaledTime + autoCloseSec;
            try
            {
                await UniTask.WaitUntil(
                    () => dialog == null || dialog.IsSelected() || Time.unscaledTime >= deadline,
                    cancellationToken: ct);
            }
            catch (OperationCanceledException) { /* 破棄/teardown: 下で Exit を試みる */ }

            try
            {
                // fake-null（破棄済み）でなく、まだ表示中なら閉じる（OK 押下時も明示クローズ＝FixMod 版と同方式・
                // ゲームは選択時に自動 Exit しない＝呼び出し側が Exit を所有する）。
                if (dialog != null && dialog.IsActive()) dialog.Exit();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[PinnedPoseToast] ダイアログ Exit 失敗: {ex.Message}");
            }

            // Exit の 0.1s クローズアニメ（DOScale→SetActive(false)）完了まで待つ（FixMod 同型）。
            // これが無いとアニメ中の連打で次の呼び出しが冒頭 IsActive()=true でスキップされ
            // 「連打すると通知が出ないことがある」挙動になる。
            try
            {
                await UniTask.WaitUntil(() => dialog == null || !dialog.IsActive(), cancellationToken: ct);
            }
            catch (OperationCanceledException) { /* クローズ中キャンセル: 無視 */ }
        }
    }
}
