using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// タイムラインの PNG 名 (画像名 + 番号) の採番と、Undo で消す余りの判定を固定する。
    /// キーフレームは名前で実体に結び付くため、番号がずれるとキーが孤立する
    /// </summary>
    public class PngObjectGroupTests
    {
        [Fact]
        public void 希望が無ければ空いている最小の番号()
        {
            Assert.Equal(0, PngObjectTimelineManager.AssignGroup(new HashSet<int>(), -1));
            Assert.Equal(2, PngObjectTimelineManager.AssignGroup(new HashSet<int> { 0, 1 }, -1));
        }

        [Fact]
        public void 欠番があれば欠番を使う()
        {
            Assert.Equal(1, PngObjectTimelineManager.AssignGroup(new HashSet<int> { 0, 2 }, -1));
        }

        [Fact]
        public void 空いていれば予約した番号を使う()
        {
            // XML に a と a (2) だけがあるとき、a (2) を 1 へ詰めない
            Assert.Equal(2, PngObjectTimelineManager.AssignGroup(new HashSet<int> { 0 }, 2));
        }

        [Fact]
        public void 予約した番号が使用中なら空いている最小の番号()
        {
            Assert.Equal(0, PngObjectTimelineManager.AssignGroup(new HashSet<int> { 2 }, 2));
        }

        [Fact]
        public void 番号1以上は括弧付きの名前になる()
        {
            Assert.Equal("a", new TimelinePngObjectData { imageName = "a", group = 0 }.name);
            Assert.Equal("a (2)", new TimelinePngObjectData { imageName = "a", group = 2 }.name);
        }

        [Fact]
        public void 余りの実体名はXMLの定義に無い名前()
        {
            var sources = new List<TimelinePngObjectData>
            {
                new TimelinePngObjectData { imageName = "a", group = 0 },
                new TimelinePngObjectData { imageName = "b", group = 0 },
            };

            var surplus = PngObjectTimelineManager.GetSurplusNames(
                new[] { "a", "a (1)", "b" }, sources);

            Assert.Equal(new[] { "a (1)" }, surplus.ToArray());
        }

        [Fact]
        public void XMLが空なら全部余り()
        {
            var surplus = PngObjectTimelineManager.GetSurplusNames(
                new[] { "a", "b" }, new List<TimelinePngObjectData>());

            Assert.Equal(new[] { "a", "b" }, surplus.ToArray());
        }
    }
}
