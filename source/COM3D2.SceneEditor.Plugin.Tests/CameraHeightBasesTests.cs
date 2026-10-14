using System.Linq;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class CameraHeightBasesTests
    {
        [Fact]
        public void 未設定のスロットは0()
        {
            Assert.Equal(0, new CameraHeightBases().Get(1));
        }

        [Fact]
        public void 負のスロットは0()
        {
            Assert.Equal(0, new CameraHeightBases().Get(-1));
        }

        [Fact]
        public void 身長が0以下なら設定しない()
        {
            var bases = new CameraHeightBases();
            bases.Set(0, 160);
            bases.Set(0, 0);
            Assert.Equal(160, bases.Get(0));
        }

        [Fact]
        public void 未設定のときだけ取り込む()
        {
            var bases = new CameraHeightBases();
            bases.SetIfUnset(0, 160);
            bases.SetIfUnset(0, 140);
            Assert.Equal(160, bases.Get(0));
        }

        [Fact]
        public void 取り込むと相手の設定済みスロットだけを上書きする()
        {
            var bases = new CameraHeightBases();
            bases.Set(0, 160);
            bases.Set(1, 150);
            var loaded = new CameraHeightBases();
            loaded.Set(1, 140);
            loaded.Set(2, 170);

            bases.MergeFrom(loaded);

            Assert.Equal(160, bases.Get(0));
            Assert.Equal(140, bases.Get(1));
            Assert.Equal(170, bases.Get(2));
            Assert.Equal(new[] { 0, 1, 2 }, bases.slotNos.OrderBy(x => x).ToArray());
        }

        [Fact]
        public void 消すと全スロットが未設定になる()
        {
            var bases = new CameraHeightBases();
            bases.Set(0, 160);
            bases.Clear();
            Assert.Equal(0, bases.Get(0));
            Assert.Empty(bases.slotNos);
        }

        [Fact]
        public void 自分自身や空を取り込んでも変わらない()
        {
            var bases = new CameraHeightBases();
            bases.Set(0, 160);
            bases.MergeFrom(bases);
            bases.MergeFrom(null);
            Assert.Equal(160, bases.Get(0));
        }

        [Fact]
        public void XMLを読んでも差し込まれていたインスタンスは消さない()
        {
            var shared = new CameraHeightBases();
            shared.Set(0, 160);
            var data = new TimelineData { cameraHeightBases = shared };
            data.FromXml(new TimelineXml());
            Assert.Equal(160, shared.Get(0));
        }

        [Fact]
        public void タイムラインは差し込まれたインスタンスへ書く()
        {
            var shared = new CameraHeightBases();
            var data = new TimelineData { cameraHeightBases = shared };
            data.SetCameraHeightBase(0, 160);
            Assert.Equal(160, shared.Get(0));
        }
    }
}
