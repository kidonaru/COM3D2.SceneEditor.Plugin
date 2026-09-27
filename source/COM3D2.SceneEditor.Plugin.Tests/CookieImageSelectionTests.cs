using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 輪郭画像コンボの選択位置と代わりの表示名を固定する。
    /// GUIComboBox は defaultName が null でないと選択中の項目より優先して表示するため、
    /// 一覧に見つかったときは必ず null を返す
    /// </summary>
    public class CookieImageSelectionTests
    {
        private static readonly List<string> Names = new List<string> { "1_star.png", @"gobo\window.png" };

        [Fact]
        public void 一覧にある画像は選択位置を返し代わりの表示名は無い()
        {
            string fallback;
            var index = LightRowDrawer.ResolveCookieImageSelection(Names, "1_star.png", out fallback);

            Assert.Equal(0, index);
            Assert.Null(fallback);
        }

        [Fact]
        public void 大文字小文字と区切り文字の違いは同じ画像として扱う()
        {
            string fallback;
            var index = LightRowDrawer.ResolveCookieImageSelection(Names, "GOBO/Window.png", out fallback);

            Assert.Equal(1, index);
            Assert.Null(fallback);
        }

        [Fact]
        public void 一覧に無い画像は見つからない旨を表示する()
        {
            string fallback;
            var index = LightRowDrawer.ResolveCookieImageSelection(Names, "missing.png", out fallback);

            Assert.Equal(-1, index);
            Assert.Equal("missing.png (見つかりません)", fallback);
        }

        [Fact]
        public void 未選択なら未選択と表示する()
        {
            string fallback;
            var index = LightRowDrawer.ResolveCookieImageSelection(Names, "", out fallback);

            Assert.Equal(-1, index);
            Assert.Equal("未選択", fallback);
        }
    }
}
