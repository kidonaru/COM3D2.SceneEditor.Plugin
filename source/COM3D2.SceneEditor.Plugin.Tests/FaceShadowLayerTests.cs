using System.Collections.Generic;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>顔の影レイヤーのキー形式を固定する。値の並びはタイムライン XML の保存形式</summary>
    public class FaceShadowLayerTests
    {
        private static MTEP.TransformDataFaceShadow CreateTransform(string name)
        {
            var trans = new MTEP.TransformDataFaceShadow();
            trans.Initialize(name);
            return trans;
        }

        [Fact]
        public void キーは有効1値でBool型()
        {
            var trans = CreateTransform(FaceShadowItems.Receive);
            Assert.Equal(MTEP.TransformType.FaceShadow, trans.type);
            Assert.Equal(1, trans.valueCount);
            Assert.Equal(0, (int)MTEP.TransformDataFaceShadow.Index.IsEnabled);
            Assert.Equal(MTEP.CustomValueType.BoolValue,
                trans.GetCustomValueInfoMap()["isEnabled"].type);
        }

        [Fact]
        public void 新しいキーの既定はゲームの既定値()
        {
            Assert.True(CreateTransform(FaceShadowItems.Receive).isEnabled);
            Assert.False(CreateTransform(FaceShadowItems.Cast).isEnabled);
        }

        [Fact]
        public void 上書きとキーのある項目だけを定義順でキーにする()
        {
            var overrides = new Dictionary<string, bool> { { FaceShadowItems.Cast, true } };
            var keyed = new List<string> { FaceShadowItems.Receive };

            var keys = MTEP.FaceShadowTimelineLayer.BuildKeys(overrides, keyed, item => false);

            Assert.Equal(2, keys.Count);
            Assert.Equal(FaceShadowItems.Receive, keys[0].Key);
            Assert.False(keys[0].Value);   // 上書きは無いが、キーがあるので今の値
            Assert.Equal(FaceShadowItems.Cast, keys[1].Key);
            Assert.True(keys[1].Value);
        }

        [Fact]
        public void 上書きもキーも無ければキーにしない()
        {
            var keys = MTEP.FaceShadowTimelineLayer.BuildKeys(
                new Dictionary<string, bool>(), new List<string>(), item => true);
            Assert.Empty(keys);
        }
    }
}
