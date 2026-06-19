namespace BG2VR.ScenePinned
{
    /// <summary>
    /// 現在 env が pinned（保存済み固定 pose を所有し適用中）かの共有フラグ（spec §4.5）。
    /// ScenePinnedPoseRunner が毎フレ確定し、CameraPositionFollowRunner が参照して
    /// pinned env ではカメラ追従を抑止する。
    /// </summary>
    public static class ScenePinnedPoseState
    {
        public static bool IsCurrentEnvPinned;
    }
}
