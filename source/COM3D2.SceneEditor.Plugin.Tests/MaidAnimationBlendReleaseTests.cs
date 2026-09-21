using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 解除対象の層の洗い出しを固定する。
    /// 実際の解除は Unity の Animation を触るためテストしない
    /// </summary>
    public class MaidAnimationBlendReleaseTests
    {
        // index = layer 番号。0,1 は未使用
        private static readonly string[] Names = { "", "", "a.anm", "", "b.anm", "", "", "", "" };

        [Fact]
        public void 名前のある層だけを昇順で返す()
        {
            var result = MaidAnimationBlendController.GetLoadedLayers(Names, 2, 8);
            Assert.Equal(new List<int> { 2, 4 }, result);
        }

        [Fact]
        public void 適用先と違い空の層は含めない()
        {
            var result = MaidAnimationBlendController.GetLoadedLayers(new string[9], 2, 8);
            Assert.Empty(result);
        }

        [Fact]
        public void 配列が短くても落ちない()
        {
            var result = MaidAnimationBlendController.GetLoadedLayers(new string[3] { "", "", "a.anm" }, 2, 8);
            Assert.Equal(new List<int> { 2 }, result);
        }
    }
}
