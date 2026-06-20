using Xunit;
using BG2VR.VrInput;

public class ModelSwitchInputTests
{
    [Fact]
    public void GripHeld_TriggerRising_FiresOnce()
    {
        var m = new ModelSwitchInput();
        m.Update(true, false, false, false, out _, out _);      // grip 保持・トリガー解放（prev=false 準備）
        m.Update(true, true, false, false, out bool cl, out _); // grip 中トリガー rising
        Assert.True(cl);
        m.Update(true, true, false, false, out bool cl2, out _); // 押しっぱなし＝多重発火しない
        Assert.False(cl2);
    }

    [Fact]
    public void TriggerWithoutGrip_DoesNotFire()
    {
        var m = new ModelSwitchInput();
        m.Update(false, false, false, false, out _, out _);
        m.Update(false, true, false, false, out bool cl, out _);
        Assert.False(cl);
    }

    [Fact]
    public void IsPerHand()
    {
        var m = new ModelSwitchInput();
        m.Update(false, false, true, false, out _, out _);
        m.Update(false, false, true, true, out bool cl, out bool cr);
        Assert.False(cl);
        Assert.True(cr);
    }

    [Fact]
    public void GripStartsWithTriggerAlreadyHeld_DoesNotFire()
    {
        // トリガーを先に押してから grip した場合は rising でない＝発火しない（combo は grip→trigger 順のみ）。
        var m = new ModelSwitchInput();
        m.Update(false, true, false, false, out _, out _);       // トリガー保持・grip なし（prevTrig=true）
        m.Update(true, true, false, false, out bool cl, out _);  // grip 立ち上げ・トリガー据置（rising でない）
        Assert.False(cl);
    }
}
