using GB.Bar.MiniGame;

namespace BG2VR.UiSceneVoid
{
    /// <summary>
    /// 3D を上演するミニゲーム（カラオケ / Cheki 等の MiniGameBase 派生）が進行中かの probe
    /// （ゲーム型依存のためテスト対象外。EnvKindClassifier と同じ層）。
    /// s_instance は MiniGameBase.Setup で set / Release で null（publicize 参照で直アクセス）。
    /// ASMR は除外＝void 維持（2D 演出のため背後の 3D を見せず黒背景が正・ユーザー実機判断 2026-06-07）。
    /// 注: locomotion 側 CameraPositionFollowRunner.IsKaraokeActive は具体型 Karaoke のみ対象
    /// （演出カメラ追従抑止）・本 probe は ASMR 以外の全派生対象（void 抑止）＝述語が異なるのは意図的。
    /// </summary>
    internal static class MiniGameProbe
    {
        public static bool Stages3D()
        {
            MiniGameBase mg = MiniGameBase.s_instance;
            return mg != null && !(mg is ASMR);
        }

        /// <summary>Cheki ミニゲーム進行中か（手元カメラ override 判定用）。Release で s_instance=null＝自動で false 化。</summary>
        public static bool IsCheki() => MiniGameBase.s_instance is Cheki;

        /// <summary>カラオケ ミニゲーム進行中か（手元プロップ override 判定用＝左タンバリン/右サイリウム）。
        /// CameraPositionFollowRunner.IsKaraokeActive と同一述語（具体型 Karaoke）。Release で s_instance=null＝自動 false 化。</summary>
        public static bool IsKaraoke() => MiniGameBase.s_instance is Karaoke;

        /// <summary>カラオケが IN_GAME 状態か（判定処理が走る区間）。publicize で private nested enum
        /// State / field m_state を直読み（IsChekiPhotographing と同方式）。SUSPEND/RESULT/PRE_GAME は false＝
        /// サスペンドダイアログ中のナビ抑止を避け、結果画面での無駄な振り発火も防ぐ。</summary>
        public static bool IsKaraokeInGame()
            => MiniGameBase.s_instance is Karaoke k && k.m_state == Karaoke.State.IN_GAME;

        /// <summary>Cheki が「撮影中(PHOTOGRAPHING)」かを判定（照準/VF/シャッター/視点固定/ズームを gate）。
        /// publicize 参照で private nested enum State / field m_state を直読み（s_instance と同方式）。</summary>
        public static bool IsChekiPhotographing()
        {
            return MiniGameBase.s_instance is Cheki cheki && cheki.m_state == Cheki.State.PHOTOGRAPHING;
        }

        /// <summary>手押し相撲ミニゲーム進行中か（両手ハンドモデル固定の判定用）。Release で s_instance=null＝自動 false 化。</summary>
        public static bool IsHandSumo() => MiniGameBase.s_instance is HandSumo;

        /// <summary>手押し相撲が PLAYING 状態か（押し出し検知 gate 用＝ノート判定が走る区間）。publicize で
        /// private nested enum State / field m_state を直読み（IsKaraokeInGame と同方式）。CONFIRM/WAIT/END は
        /// false＝確認ダイアログ・勝利待機画面での誤発火を避ける（そこはボタン操作）。</summary>
        public static bool IsHandSumoPlaying()
            => MiniGameBase.s_instance is HandSumo hs && hs.m_state == HandSumo.State.PLAYING;

        /// <summary>あ〜ん「食べさせる側(ForCast)」進行中か（右手ハンドモデル握り override 判定用）。
        /// AhhnGame.m_ahhnMode（publicize 参照）が ForCastMode のときのみ true。ForPlayer は false。
        /// Release で s_instance=null＝自動 false 化。モデル表示用なので state(CountDown 含む)は問わない
        /// （IsKaraoke/IsHandSumo の広い型 probe と同方針＝入力 gate 用の狭い probe は別）。</summary>
        public static bool IsAhhnForCast()
            => MiniGameBase.s_instance is AhhnGame ag && ag.m_ahhnMode is ForCastMode;
    }
}
