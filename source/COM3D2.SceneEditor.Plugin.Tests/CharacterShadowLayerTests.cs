using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>影用レイヤーの選択を固定する。名前の無い、カメラに映る、キャラ用でないレイヤーの最小番号を選ぶ</summary>
    public class CharacterShadowLayerTests
    {
        // 実機のレイヤー構成 (Charactor=10, Face=11, Man=12) に合わせたテスト用マスク
        private const int CharacterMask = (1 << 10) | (1 << 11) | (1 << 12);

        private static string[] CreateNames()
        {
            var names = new string[32];
            for (var i = 0; i < names.Length; i++)
            {
                names[i] = "Layer" + i;
            }
            return names;
        }

        private static int Select(string[] names, int cameraMask, int characterMask)
        {
            return CharacterShadowLayer.Select(layer => names[layer], cameraMask, characterMask);
        }

        [Fact]
        public void 名前の無いレイヤーのうち最小の番号を選ぶ()
        {
            var names = CreateNames();
            names[3] = "";
            names[6] = "";
            names[7] = null;

            Assert.Equal(3, Select(names, -1, CharacterMask));
        }

        [Fact]
        public void カメラに映らないレイヤーは選ばない()
        {
            var names = CreateNames();
            names[3] = "";
            names[6] = "";

            Assert.Equal(6, Select(names, ~(1 << 3), CharacterMask));
        }

        [Fact]
        public void キャラ用のレイヤーは選ばない()
        {
            var names = CreateNames();
            names[10] = "";
            names[20] = "";

            Assert.Equal(20, Select(names, -1, CharacterMask));
        }

        [Fact]
        public void 該当が無ければNone()
        {
            var names = CreateNames();
            names[5] = "";

            Assert.Equal(CharacterShadowLayer.None, Select(names, ~(1 << 5), CharacterMask));
            Assert.Equal(CharacterShadowLayer.None, Select(CreateNames(), -1, CharacterMask));
        }
    }
}
