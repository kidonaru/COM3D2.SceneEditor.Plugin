using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineHistoryBaselineTests
    {
        [Fact]
        public void SetBaselineは現在のタイムラインを基準として据える()
        {
            var manager = TimelineHistoryManager.instance;
            manager.ClearHistory();
            Assert.Null(manager.lastCommittedXml);

            var timeline = new TimelineData { anmName = "テスト" };
            manager.SetBaseline(timeline);

            Assert.NotNull(manager.lastCommittedXml);
            Assert.Equal("テスト", manager.lastCommittedXml.anmName);
        }

        [Fact]
        public void SetBaselineにnullを渡すと基準を消す()
        {
            var manager = TimelineHistoryManager.instance;
            manager.SetBaseline(new TimelineData { anmName = "テスト" });

            manager.SetBaseline(null);

            Assert.Null(manager.lastCommittedXml);
        }
    }
}
