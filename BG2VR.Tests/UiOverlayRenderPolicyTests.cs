using BG2VR.WorldUi;
using Xunit;

namespace BG2VR.Tests
{
    public class UiOverlayRenderPolicyTests
    {
        // ---- ZTestMode（unity_GUIZTestMode への CompareFunction 値） ----

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ZTestMode_DepthTestOff_はreversedZに依らずAlways(bool reversedZ)
        {
            Assert.Equal(8, UiOverlayRenderPolicy.ZTestMode(false, reversedZ)); // CompareFunction.Always
        }

        [Fact]
        public void ZTestMode_DepthTestOn_は非reversedでLEqual_reversedでGEqual()
        {
            Assert.Equal(4, UiOverlayRenderPolicy.ZTestMode(true, reversedZ: false)); // CompareFunction.LEqual
            Assert.Equal(7, UiOverlayRenderPolicy.ZTestMode(true, reversedZ: true));  // CompareFunction.GreaterEqual
        }

        // ---- IsAlphaEatingShader ----

        [Theory]
        [InlineData("UI/Mulatiply")]          // 実機の Footer/LocationUI 帯（ゲーム側 typo そのまま）
        [InlineData("Custom/MulatiplyBlend")] // 名前包含で将来の同型 shader も拾う
        public void IsAlphaEatingShader_乗算系を検出する(string name)
        {
            Assert.True(UiOverlayRenderPolicy.IsAlphaEatingShader(name));
        }

        [Theory]
        [InlineData("UI/Default")]
        [InlineData("Sprites/Default")]
        [InlineData("TextMeshPro/Distance Field")]
        [InlineData("UI/Multiply")] // 正しい綴りは実機に存在しない＝誤検出しないこと（typo 名のみ対象）
        [InlineData("")]
        [InlineData(null)]
        public void IsAlphaEatingShader_非対象は検出しない(string name)
        {
            Assert.False(UiOverlayRenderPolicy.IsAlphaEatingShader(name));
        }

        // ---- IsAdditiveShader ----

        [Theory]
        [InlineData("UI/AddBlend")]              // 実機の加算 UI エフェクト（グロー等）
        [InlineData("UI/AddBlendCustomVariant")] // 名前包含で将来の同型 shader も拾う
        public void IsAdditiveShader_加算系を検出する(string name)
        {
            Assert.True(UiOverlayRenderPolicy.IsAdditiveShader(name));
        }

        [Theory]
        [InlineData("UI/Default")]
        [InlineData("UI/Mulatiply")]
        [InlineData("BG2VR/UiAdditiveKeyed")] // 差し替え後の自前 shader を二重検出しない
        [InlineData("")]
        [InlineData(null)]
        public void IsAdditiveShader_非対象は検出しない(string name)
        {
            Assert.False(UiOverlayRenderPolicy.IsAdditiveShader(name));
        }

        // ---- queue 不変条件 ----

        [Fact]
        public void Queue_パネルよりボタン_ボタンよりレーザーが後()
        {
            Assert.True(UiOverlayRenderPolicy.PanelQueue < UiOverlayRenderPolicy.ButtonQueue);
            Assert.True(UiOverlayRenderPolicy.ButtonQueue < UiOverlayRenderPolicy.LaserQueue);
        }

        [Fact]
        public void Queue_パネルは髪OverPassより後()
        {
            // 髪 Toon shader の前髪 over-pass は q=3010（実測 2026-06-07・BG2DevBridge）。
            // これ以下に下げると前髪が UI を上書きする退行（本 fix の発端）。
            Assert.True(UiOverlayRenderPolicy.PanelQueue > 3010);
        }

        // ---- EffectiveDepthTest（種別 × 遮蔽 × WorldUiDepthTest の実効 depth test） ----
        // UiOverlayKind は internal のため public テストメソッドの引数にできない（CS0051）＝メソッド本体内で参照する。

        // 遮蔽 ON（本機能の核）: パネル/ボタン/レーザー=depth test(true・コントローラに隠れる)、
        // レティクル/設定=Always(false・最前面)。worldDepthTest には依らない。
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EffectiveDepthTest_遮蔽ON_パネルとレーザーは隠れレティクルと設定は最前面(bool worldDepthTest)
        {
            Assert.True(UiOverlayRenderPolicy.EffectiveDepthTest(UiOverlayKind.Panel, true, worldDepthTest));
            Assert.True(UiOverlayRenderPolicy.EffectiveDepthTest(UiOverlayKind.Button, true, worldDepthTest));
            Assert.True(UiOverlayRenderPolicy.EffectiveDepthTest(UiOverlayKind.Laser, true, worldDepthTest));   // レーザーもコントローラに隠れる
            Assert.False(UiOverlayRenderPolicy.EffectiveDepthTest(UiOverlayKind.Reticle, true, worldDepthTest)); // レティクルは最前面
            Assert.False(UiOverlayRenderPolicy.EffectiveDepthTest(UiOverlayKind.Settings, true, worldDepthTest));
        }

        // 遮蔽 OFF（従来挙動）: パネル/ボタン/レーザー/レティクルは worldDepthTest 追従、設定のみ常に Always(false)。
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EffectiveDepthTest_遮蔽OFF_ポインタとパネルはworldDepthTest追従(bool worldDepthTest)
        {
            Assert.Equal(worldDepthTest, UiOverlayRenderPolicy.EffectiveDepthTest(UiOverlayKind.Panel, false, worldDepthTest));
            Assert.Equal(worldDepthTest, UiOverlayRenderPolicy.EffectiveDepthTest(UiOverlayKind.Button, false, worldDepthTest));
            Assert.Equal(worldDepthTest, UiOverlayRenderPolicy.EffectiveDepthTest(UiOverlayKind.Laser, false, worldDepthTest));
            Assert.Equal(worldDepthTest, UiOverlayRenderPolicy.EffectiveDepthTest(UiOverlayKind.Reticle, false, worldDepthTest));
            Assert.False(UiOverlayRenderPolicy.EffectiveDepthTest(UiOverlayKind.Settings, false, worldDepthTest));
        }

        // ---- 代替色 ----

        [Fact]
        public void 代替色_アルファを持つ半透明黒()
        {
            var c = UiOverlayRenderPolicy.AlphaEatingReplacementColor;
            Assert.Equal(0f, c.r);
            Assert.Equal(0f, c.g);
            Assert.Equal(0f, c.b);
            Assert.InRange(c.a, 0.01f, 0.99f); // 0=効果なし / 1=完全不透明はどちらも意図と異なる
        }
    }
}
