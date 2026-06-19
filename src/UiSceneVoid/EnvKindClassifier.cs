using GB.Scene;

namespace BG2VR.UiSceneVoid
{
    /// <summary>
    /// EnvSceneBase → EnvKind の型ベース分類（ゲーム型依存のためテスト対象外・switch のみ）。
    /// GetActiveEnvScene() の戻り値をそのまま受ける。null（Unity fake-null 含む＝== が吸収）は None。
    /// </summary>
    internal static class EnvKindClassifier
    {
        public static EnvKind Classify(EnvSceneBase env)
        {
            if (env == null) return EnvKind.None;
            if (env is HoleScene) return EnvKind.Hole;
            if (env is Talk2DScene) return EnvKind.Talk2D;
            if (env is SteelFrameScene) return EnvKind.SteelFrame;
            return EnvKind.Other; // VipRoomScene / GameRoomScene / 未知型 / debug シーン
        }
    }
}
