using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>保存された名前 + 位置からのマテリアル同定を固定する</summary>
    public class MaterialLookupTests
    {
        private static readonly string[] Names = { "skin", "cloth", "skin" };

        [Fact]
        public void 位置と名前が一致すればその位置を返す()
        {
            Assert.Equal(2, MaterialLookup.FindIndex(Names, "skin", 2));
        }

        [Fact]
        public void 位置の名前が違えば先頭の名前一致を返す()
        {
            Assert.Equal(0, MaterialLookup.FindIndex(Names, "skin", 1));
        }

        [Fact]
        public void 位置が範囲外なら先頭の名前一致を返す()
        {
            Assert.Equal(1, MaterialLookup.FindIndex(Names, "cloth", 9));
            Assert.Equal(0, MaterialLookup.FindIndex(Names, "skin", -1));
        }

        [Fact]
        public void 名前が無ければマイナス1()
        {
            Assert.Equal(-1, MaterialLookup.FindIndex(Names, "hair", 0));
        }
    }
}
