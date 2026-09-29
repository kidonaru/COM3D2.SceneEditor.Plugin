using COM3D2.MotionTimelineEditor;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>連携プラグインの UI 倍率が SceneEditor と自前設定のどちらから決まるかを固定する</summary>
    public class UIScaleClientTests
    {
        [Fact]
        public void SceneEditorの倍率があればそれに従う()
        {
            Assert.Equal(1.5f, UIScaleClient.ResolveScale(1.5f, 1.2f), 4);
        }

        [Fact]
        public void SceneEditorが無いか無効なら自前の設定を使う()
        {
            // UIScaleHost は無効時に 0 を返す。旧版・不在も 0 として扱う
            Assert.Equal(1.2f, UIScaleClient.ResolveScale(0f, 1.2f), 4);
        }

        [Fact]
        public void 壊れた値は安全な倍率へ直す()
        {
            Assert.Equal(1.2f, UIScaleClient.ResolveScale(float.NaN, 1.2f), 4);
            Assert.Equal(1f, UIScaleClient.ResolveScale(0f, float.NaN), 4);
            Assert.Equal(GUIScale.MaxScale, UIScaleClient.ResolveScale(9f, 1f), 4);
        }
    }
}
