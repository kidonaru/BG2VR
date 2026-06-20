using UnityEngine;
using Xunit;
using BG2VR.VrInput;

public class ControllerModelPoseTests
{
    private const float Eps = 1e-4f;

    // テスト host は native ECall（Quaternion.Euler/AngleAxis = Internal_From*）を実行できないため、
    // クォータニオンは正規化済みの生値で構築する（Quaternion 乗算・Vector3 回転・Quaternion.Angle は managed）。
    private static readonly Quaternion Yaw90 = new Quaternion(0f, 0.70710678f, 0f, 0.70710678f); // +90° about Y
    private static readonly Quaternion Yaw45 = new Quaternion(0f, 0.38268343f, 0f, 0.92387953f); // +45° about Y
    private static readonly Quaternion Pitch90 = new Quaternion(0.70710678f, 0f, 0f, 0.70710678f); // +90° about X

    [Fact]
    public void ゼロ回転オフセットならsnapshot姿勢をそのまま返す()
    {
        Vector3 pos = new Vector3(0.1f, 0.2f, -0.3f);
        ControllerModelPose.Compute(pos, Yaw45, Vector3.zero, Quaternion.identity,
            out Vector3 lp, out Quaternion lr);
        Assert.True(Vector3.Distance(pos, lp) < Eps);
        Assert.True(Quaternion.Angle(Yaw45, lr) < 0.05f);
    }

    [Fact]
    public void 回転オフセットはsnapshot回転に合成される()
    {
        // snap=identity、+90°ヨーオフセット → 結果は +90°ヨー
        ControllerModelPose.Compute(Vector3.zero, Quaternion.identity,
            Vector3.zero, Yaw90, out _, out Quaternion lr);
        Assert.True(Quaternion.Angle(Yaw90, lr) < 0.05f);
    }

    [Fact]
    public void 回転オフセットはローカル前置で合成される()
    {
        // 非可換な 2 軸（snap=ヨー90° / offset=ピッチ90°）で合成順序を固定する。
        // Compute は localRot = snapRot * rotOffset（ローカル前置）であり、逆順 rotOffset * snapRot とは有意に異なる。
        ControllerModelPose.Compute(Vector3.zero, Yaw90, Vector3.zero, Pitch90, out _, out Quaternion lr);
        Assert.True(Quaternion.Angle(Yaw90 * Pitch90, lr) < 0.05f);   // 期待 = snapRot * rotOffset
        Assert.True(Quaternion.Angle(Pitch90 * Yaw90, lr) > 1f);      // 逆順とは別物
    }

    [Fact]
    public void 位置オフセットは手のローカル軸に乗る()
    {
        // snap がヨー+90°なら、ローカル +Z オフセットはワールド +X 方向へ回る
        Vector3 snapPos = new Vector3(1f, 0f, 0f);
        ControllerModelPose.Compute(snapPos, Yaw90,
            new Vector3(0f, 0f, 1f), Quaternion.identity, out Vector3 lp, out _);
        // 期待 = snapPos + (1,0,0)
        Assert.True(Vector3.Distance(new Vector3(2f, 0f, 0f), lp) < 1e-3f);
    }

    [Fact]
    public void HandModelScaleVector_非mirrorは全軸正の等倍()
    {
        Vector3 s = ControllerModelPose.HandModelScaleVector(2f, 0.5f, false);
        // scale*base = 1.0 を全軸
        Assert.Equal(1f, s.x, 4);
        Assert.Equal(1f, s.y, 4);
        Assert.Equal(1f, s.z, 4);
    }

    [Fact]
    public void HandModelScaleVector_mirrorはXのみ反転()
    {
        Vector3 s = ControllerModelPose.HandModelScaleVector(2f, 0.5f, true);
        // X だけ負、Y/Z は正のまま（negative X scale で対の手を生成）
        Assert.Equal(-1f, s.x, 4);
        Assert.Equal(1f, s.y, 4);
        Assert.Equal(1f, s.z, 4);
    }

    [Fact]
    public void HandModelScaleVector_base1なら倍率がそのまま出る()
    {
        // config 倍率がそのまま等倍スケールになる（baseScale=1.0 を引数で渡した場合）
        Vector3 s = ControllerModelPose.HandModelScaleVector(1.3f, 1.0f, false);
        Assert.Equal(1.3f, s.x, 4);
        Assert.Equal(1.3f, s.y, 4);
        Assert.Equal(1.3f, s.z, 4);
    }

    [Fact]
    public void MirrorRotationX_恒等は恒等のまま()
    {
        Quaternion m = ControllerModelPose.MirrorRotationX(Quaternion.identity);
        Assert.True(Quaternion.Angle(Quaternion.identity, m) < 0.05f);
    }

    [Fact]
    public void MirrorRotationX_二回適用で元に戻る()
    {
        // 鏡映は対合（involution）＝2 回で恒等
        Quaternion m2 = ControllerModelPose.MirrorRotationX(ControllerModelPose.MirrorRotationX(Yaw45));
        Assert.True(Quaternion.Angle(Yaw45, m2) < 0.05f);
    }

    [Fact]
    public void MirrorRotationX_ヨーは逆ヨーになる()
    {
        // +90°ヨーの X 平面鏡映 = −90°ヨー（左回りが右回りに反転＝左右対称の核心）
        Quaternion m = ControllerModelPose.MirrorRotationX(Yaw90);
        Quaternion yawNeg90 = new Quaternion(0f, -0.70710678f, 0f, 0.70710678f);
        Assert.True(Quaternion.Angle(yawNeg90, m) < 0.05f);
    }

    [Fact]
    public void Brightened_倍率1は元の色のまま()
    {
        Color c = ControllerModelPose.Brightened(Color.white, 1f);
        Assert.Equal(1f, c.r, 4);
        Assert.Equal(1f, c.g, 4);
        Assert.Equal(1f, c.b, 4);
        Assert.Equal(1f, c.a, 4); // 白×1=白＝既定 no-op
    }

    [Fact]
    public void Brightened_倍率はRGBに乗りアルファは保つ()
    {
        Color c = ControllerModelPose.Brightened(Color.white, 0.5f);
        Assert.Equal(0.5f, c.r, 4);
        Assert.Equal(0.5f, c.g, 4);
        Assert.Equal(0.5f, c.b, 4);
        Assert.Equal(1f, c.a, 4); // アルファは倍率の影響を受けない（不透明維持）
    }

    [Fact]
    public void Brightened_倍率0はベタ黒だがアルファ保持()
    {
        Color c = ControllerModelPose.Brightened(new Color(1f, 1f, 1f, 0.5f), 0f);
        Assert.Equal(0f, c.r, 4);
        Assert.Equal(0f, c.g, 4);
        Assert.Equal(0f, c.b, 4);
        Assert.Equal(0.5f, c.a, 4); // base のアルファをそのまま保つ
    }

    [Fact]
    public void Brightened_非白baseは各成分が倍率で縮む()
    {
        // 色駆動プロップ（タンバリン/サイリウム）の submesh 元色 × 倍率
        Color c = ControllerModelPose.Brightened(new Color(0.8f, 0.6f, 0.4f, 1f), 0.5f);
        Assert.Equal(0.4f, c.r, 4);
        Assert.Equal(0.3f, c.g, 4);
        Assert.Equal(0.2f, c.b, 4);
        Assert.Equal(1f, c.a, 4);
    }
}
