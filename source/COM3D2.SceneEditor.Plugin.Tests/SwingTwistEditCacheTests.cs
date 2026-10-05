using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class SwingTwistEditCacheTests
    {
        [Fact]
        public void 書き込んだオイラー角のままなら入力した値をそのまま返す()
        {
            var cache = new SwingTwistEditCache();
            // 変換を往復させると float 誤差で入力値と一致しない値
            var angles = new Vector3(-90f, 0f, 30.1f);
            var eulerAngles = new Vector3(-70.9f, -85.8f, 105.1f);

            cache.Store(angles, eulerAngles);

            Assert.Equal(angles, cache.GetAngles(eulerAngles));
        }

        [Fact]
        public void オイラー角が外から変わったら変換し直す()
        {
            var cache = new SwingTwistEditCache();
            cache.Store(new Vector3(-90f, 0f, 30f), new Vector3(-70.9f, -85.8f, 105.1f));

            var angles = cache.GetAngles(new Vector3(-10f, 0f, 0f));

            Assert.Equal(-10f, angles.x, 3);
            Assert.Equal(0f, angles.y, 3);
            Assert.Equal(0f, angles.z, 3);
        }

        [Fact]
        public void 未保存なら変換した値を返す()
        {
            var angles = new SwingTwistEditCache().GetAngles(new Vector3(0f, 0f, 20f));

            Assert.Equal(0f, angles.x, 3);
            Assert.Equal(0f, angles.y, 3);
            Assert.Equal(20f, angles.z, 3);
        }
    }
}
