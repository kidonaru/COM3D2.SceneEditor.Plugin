using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class PostEffectItemInspectorTests
    {
        /// <summary>レイヤー側の命名 (接尾辞に添字が付く形) を模したもの</summary>
        private static string GetName(int index)
        {
            return index > 0 ? string.Format("Paraffin ({0})", index) : "Paraffin";
        }

        [Theory]
        [InlineData("Paraffin", 0)]
        [InlineData("Paraffin (1)", 1)]
        [InlineData("Paraffin (3)", 3)]
        public void ResolveIndex_項目名から添字を引く(string itemName, int expected)
        {
            Assert.Equal(expected, PostEffectItemInspector.ResolveIndex(itemName, 4, GetName));
        }

        [Fact]
        public void ResolveIndex_設定数を超える項目は_1を返す()
        {
            // エフェクト数を減らすと、残ったキーの項目名だけが範囲外になる
            Assert.Equal(-1, PostEffectItemInspector.ResolveIndex("Paraffin (5)", 4, GetName));
        }

        [Fact]
        public void ResolveIndex_別のエフェクトの項目名は_1を返す()
        {
            Assert.Equal(-1, PostEffectItemInspector.ResolveIndex("Rimlight", 4, GetName));
        }

        [Fact]
        public void ResolveIndex_設定数が0なら常に_1を返す()
        {
            Assert.Equal(-1, PostEffectItemInspector.ResolveIndex("Paraffin", 0, GetName));
        }
    }
}
