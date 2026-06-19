namespace BG2VR.TransitionGuard
{
    /// <summary>
    /// 「遷移開始（ChangeScene*）で arm → 遷移内の最初のシーン操作（LoadSceneAsync）で 1 回だけ consume」
    /// の純状態（UnityEngine / BepInEx 非依存・xUnit テスト可能）。
    ///
    /// 目的: VR rig の teardown を ChangeScene* 入場（フェードアウトより前）から
    /// 「フェードアウト await 完了後・シーンのロード/アンロード直前」へ遅延する。
    /// これでフェードアウトが実シーン上で live 描画される（凍結フレームのフェードを解消）。
    ///
    /// arm の scope が要る理由: 同じ <c>GBSystem.LoadSceneAsync</c> を env 切替（showEnvScene）や
    /// SetupEnvScenes も呼ぶ。これらは teardown 対象外（カメラ rebind で VR 継続）なので、
    /// arm 済み（=本物の ChangeScene* 遷移内）のときだけ consume する。env 切替の LoadSceneAsync は
    /// armed=false ＝無視される。consume は arm を必ず消す（同一遷移内の後続 load は二重 teardown しない）。
    ///
    /// 既知の限界（実害小・未観測）: arm 後その遷移が LoadSceneAsync 到達前に中断（cancel/例外）すると
    /// _armed が残り、次に来た無関係な LoadSceneAsync（env 切替等）が 1 回だけ誤 consume＝余分な teardown を
    /// 1 回起こしうる（teardown は冪等・状態機械が多重を集約・rig は次フレ再 attach）。現状はリセット経路を
    /// 持たない（「観測されない症状への予防コードは入れない」規約。実機で観測されたら TransitionGuardRunner の
    /// 完了ポーリングから Reset を足す）。
    /// </summary>
    public sealed class TransitionTeardownArming
    {
        private bool _armed;

        public bool IsArmed => _armed;

        /// <summary>ChangeScene* 入場で arm（再 arm は上書き＝直近遷移が支配）。teardown はまだしない。</summary>
        public void Arm() => _armed = true;

        /// <summary>
        /// 最初のシーン操作 Prefix から呼ぶ。armed なら true を返して arm を消す（＝この呼出で teardown する）。
        /// 未 arm（env 切替等）なら false（no-op）。
        /// </summary>
        public bool TryConsume()
        {
            if (!_armed) return false;
            _armed = false;
            return true;
        }
    }
}
