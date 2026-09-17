using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>タイムライン設定の既定値。自動登録は既定 OFF のオプトインとして提供する</summary>
    public class TimelineConfigDefaultsTests
    {
        [Fact]
        public void 自動登録は既定でOFFになる()
        {
            Assert.False(new MTEP.Config().isAutoKeyFrame);
        }
    }
}
