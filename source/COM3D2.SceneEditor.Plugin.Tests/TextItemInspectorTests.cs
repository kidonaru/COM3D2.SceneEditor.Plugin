using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TextItemInspectorTests
    {
        [Theory]
        [InlineData("Text0", 0)]
        [InlineData("Text5", 5)]
        [InlineData("Text15", 15)]
        public void ParseTextIndex_項目名の末尾の添字を取り出す(string itemName, int expected)
        {
            Assert.Equal(expected, TextItemInspector.ParseTextIndex(itemName));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        // 別レイヤーの項目名や添字の無い名前は対象外
        [InlineData("Text")]
        [InlineData("Voice0")]
        [InlineData("Textあ")]
        [InlineData("Text-1")]
        public void ParseTextIndex_想定外の名前は_1を返す(string itemName)
        {
            Assert.Equal(-1, TextItemInspector.ParseTextIndex(itemName));
        }
    }
}
