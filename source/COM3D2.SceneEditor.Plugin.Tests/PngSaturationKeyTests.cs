using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// PNG 配置のキーに追加した彩度 (index 32) を固定する。
    /// 旧データ (32 値) は不足分が 0 で埋まるため、補正しないとグレースケールで読まれる
    /// </summary>
    public class PngSaturationKeyTests
    {
        private static TransformDataPngObject CreateKey()
        {
            var trans = new TransformDataPngObject();
            trans.Initialize("logo");
            return trans;
        }

        [Fact]
        public void リセットしたキーの彩度は1()
        {
            // 新規キーは値 0 で作られ、レイヤーの UpdateFrame が実体の値を書き込む。既定値は Reset で入る
            var trans = CreateKey();
            trans.Reset();

            Assert.Equal(1f, trans.saturation);
        }

        [Fact]
        public void 彩度はindex32で値数は33()
        {
            // 値の並びはタイムライン XML の保存形式。ベタ書きで固定する
            Assert.Equal(32, (int)TransformDataPngObject.Index.Saturation);
            Assert.Equal(33, CreateKey().valueCount);
            Assert.Equal(32, TransformDataPngObject.LegacyValueCount);
        }

        [Fact]
        public void 彩度を持たない旧キーは1で読む()
        {
            var trans = CreateKey();
            trans.FromXml(new TransformXml
            {
                name = "logo",
                type = TransformType.PngObject,
                values = new float[32],
            });

            Assert.Equal(1f, trans.saturation);
        }

        [Fact]
        public void 彩度を持つキーは保存値で読む()
        {
            var values = new float[33];
            values[32] = 0.25f;
            var trans = CreateKey();
            trans.FromXml(new TransformXml
            {
                name = "logo",
                type = TransformType.PngObject,
                values = values,
            });

            Assert.Equal(0.25f, trans.saturation);
        }

        [Fact]
        public void 彩度は往復で保たれる()
        {
            var trans = CreateKey();
            trans.saturation = 1.5f;

            var restored = CreateKey();
            restored.FromXml(trans.ToXml());

            Assert.Equal(1.5f, restored.saturation);
        }
    }
}
