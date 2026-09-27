using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シェーダー差し替え時の renderQueue の決め方を固定する</summary>
    public class MaterialRenderQueueTests
    {
        [Fact]
        public void 明示指定のキューは引き継ぐ()
        {
            Assert.Equal(3010, MaterialRenderQueue.Resolve(3010, 3000, 2000));
        }

        [Fact]
        public void シェーダー既定のままなら新シェーダーの既定に従う()
        {
            Assert.Equal(2000, MaterialRenderQueue.Resolve(3000, 3000, 2000));
            Assert.Equal(3000, MaterialRenderQueue.Resolve(2000, 2000, 3000));
        }
    }
}
