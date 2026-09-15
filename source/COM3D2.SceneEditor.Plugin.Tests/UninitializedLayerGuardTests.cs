using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// Init() を通っていないレイヤーへ破壊的な処理が流れないことを確かめる。
    /// OnLoad が途中で失敗して LayerInit まで到達しなかった場合に踏む。
    /// (同じ状況で踏む BGModelManager.DeleteModel の info null は、
    ///  BGModelStat の生成が Unity ランタイムを要するためここでは検証できない)
    /// </summary>
    public class UninitializedLayerGuardTests
    {
        [Fact]
        public void 未初期化レイヤーのGetAnmBinaryはnullを返す()
        {
            var layer = MotionTimelineLayer.Create(0);

            Assert.False(layer.isInitialized);
            // Init 前は _dummyLastFrame が無いので、anm を組もうとすると NRE になっていた
            Assert.Null(layer.GetAnmBinary(false));
        }
    }
}
