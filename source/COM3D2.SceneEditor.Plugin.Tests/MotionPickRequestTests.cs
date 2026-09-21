using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ピッカー要求の有効判定を固定する。
    /// 対象メイドが変わったらピッカーを畳む (別のメイドの層へ載せない)
    /// </summary>
    public class MotionPickRequestTests
    {
        [Fact]
        public void メイドがnullの要求は無効()
        {
            var request = new MotionPickRequest { maid = null, layer = 2 };

            Assert.False(request.IsValidFor(null));
        }

        [Fact]
        public void レイヤーが範囲外の要求は無効()
        {
            var request = new MotionPickRequest { maid = null, layer = 0 };

            Assert.False(request.IsValidFor(null));
        }
    }
}
