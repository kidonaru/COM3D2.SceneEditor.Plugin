using COM3D2.MotionTimelineEditor;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>設定ウィンドウの UI 倍率スライダーの値の丸めを固定する</summary>
    public class UIScaleSettingTests
    {
        [Theory]
        [InlineData(1.5f, 1.5f)]
        [InlineData(1.23f, 1.25f)]
        [InlineData(1.12f, 1.1f)]
        [InlineData(0.5f, 0.8f)]
        [InlineData(3f, 2f)]
        public void 倍率は5パーセント刻みに丸めて範囲へ収める(float input, float expected)
        {
            Assert.Equal(expected, UIScaleSetting.Snap(input), 4);
        }

        [Fact]
        public void 表示は5パーセント刻み()
        {
            Assert.Equal(100f, UIScaleSetting.SnapPercent(102f), 4);
            Assert.Equal(105f, UIScaleSetting.SnapPercent(103f), 4);
            Assert.Equal(80f, UIScaleSetting.SnapPercent(50f), 4);
        }

        [Fact]
        public void スライダーを一方向へ動かすと表示は戻らずに進む()
        {
            // スライダーは絶対位置を返す。丸めた表示がカーソルの前後で揺れないこと
            var prev = 0f;
            for (var v = 80f; v <= 200f; v += 0.7f)
            {
                var shown = UIScaleSetting.SnapPercent(v);
                Assert.True(shown >= prev);
                prev = shown;
            }
        }

        [Fact]
        public void 設定で選べる範囲は描画側の許容範囲に収まる()
        {
            // 描画側 (GUIScale.ClampScale) の範囲を外れると、選んだ値と実際の倍率が食い違う
            Assert.True(UIScaleSetting.Min >= GUIScale.MinScale);
            Assert.True(UIScaleSetting.Max <= GUIScale.MaxScale);
        }
    }
}
