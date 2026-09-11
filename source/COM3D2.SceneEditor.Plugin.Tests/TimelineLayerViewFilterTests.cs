using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineLayerViewFilterTests
    {
        // レイヤーは文字列で代用し、カテゴリと priority は表で引く
        private static readonly Dictionary<string, TimelineLayerCategory> CATEGORY =
            new Dictionary<string, TimelineLayerCategory>
        {
            { "motion", TimelineLayerCategory.Maid },
            { "eyes", TimelineLayerCategory.Maid },
            { "camera", TimelineLayerCategory.Camera },
            { "light", TimelineLayerCategory.Effect },
            { "psyllium", TimelineLayerCategory.Effect },
        };

        private static readonly Dictionary<string, int> PRIORITY = new Dictionary<string, int>
        {
            { "motion", 0 },
            { "eyes", 12 },
            { "camera", 20 },
            { "light", 41 },
            { "psyllium", 44 },
        };

        private static TimelineLayerCategory GetCategory(string layer) => CATEGORY[layer];
        private static int GetPriority(string layer) => PRIORITY[layer];

        private static readonly List<string> ALL = new List<string> { "motion", "eyes", "camera", "light", "psyllium" };

        [Fact]
        public void レイヤーモードはアクティブレイヤーだけを返す()
        {
            var result = new List<string>();
            TimelineLayerViewFilter.Filter(ALL, "camera", TimelineLayerViewMode.Layer, GetCategory, result);
            Assert.Equal(new[] { "camera" }, result);
        }

        [Fact]
        public void カテゴリモードは同カテゴリのレイヤーを入力順で返す()
        {
            var result = new List<string>();
            TimelineLayerViewFilter.Filter(ALL, "eyes", TimelineLayerViewMode.Category, GetCategory, result);
            Assert.Equal(new[] { "motion", "eyes" }, result);
        }

        [Fact]
        public void カテゴリモードでアクティブレイヤーが対象外でも含める()
        {
            // 他メイドのレイヤーがアクティブなど、操作対象一覧に無いケースの防御
            var targets = new List<string> { "camera", "light" };
            var result = new List<string>();
            TimelineLayerViewFilter.Filter(targets, "motion", TimelineLayerViewMode.Category, GetCategory, result);
            Assert.Equal(new[] { "motion" }, result);
        }

        [Fact]
        public void アクティブレイヤーがnullなら空()
        {
            var result = new List<string> { "stale" };
            TimelineLayerViewFilter.Filter(ALL, null, TimelineLayerViewMode.Category, GetCategory, result);
            Assert.Empty(result);
            TimelineLayerViewFilter.Filter(ALL, null, TimelineLayerViewMode.Layer, GetCategory, result);
            Assert.Empty(result);
        }

        [Fact]
        public void Filterは呼ぶたびに結果をクリアする()
        {
            var result = new List<string>();
            TimelineLayerViewFilter.Filter(ALL, "light", TimelineLayerViewMode.Category, GetCategory, result);
            TimelineLayerViewFilter.Filter(ALL, "light", TimelineLayerViewMode.Category, GetCategory, result);
            Assert.Equal(new[] { "light", "psyllium" }, result);
        }

        [Fact]
        public void FindFirstLayerはpriority昇順の先頭を返す()
        {
            var targets = new List<string> { "psyllium", "light" };
            var first = TimelineLayerViewFilter.FindFirstLayer(
                targets, TimelineLayerCategory.Effect, GetCategory, GetPriority);
            Assert.Equal("light", first);
        }

        [Fact]
        public void FindFirstLayerは同priorityなら入力順を保つ()
        {
            var targets = new List<string> { "b", "a" };
            var first = TimelineLayerViewFilter.FindFirstLayer(
                targets, TimelineLayerCategory.Maid, _ => TimelineLayerCategory.Maid, _ => 0);
            Assert.Equal("b", first);
        }

        [Fact]
        public void FindFirstLayerは該当なしでnull()
        {
            var first = TimelineLayerViewFilter.FindFirstLayer(
                ALL, TimelineLayerCategory.Background, GetCategory, GetPriority);
            Assert.Null(first);
        }
    }
}
