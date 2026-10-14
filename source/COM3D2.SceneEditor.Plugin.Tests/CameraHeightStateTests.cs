using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class CameraHeightStateTests
    {
        [Fact]
        public void 初期状態は補正なし()
        {
            var state = new CameraHeightState();
            Assert.Equal(CameraHeightState.NoSlot, state.liveSlotNo);
            Assert.Equal(CameraHeightState.NoSlot, state.slotA);
            Assert.Equal(CameraHeightState.NoSlot, state.slotB);
            Assert.Equal(0f, state.weight);
        }

        [Fact]
        public void ライブ値を選ぶと補間なしでそのメイドになる()
        {
            var state = new CameraHeightState();
            state.SetLive(2);
            Assert.Equal(2, state.liveSlotNo);
            Assert.Equal(2, state.slotA);
            Assert.Equal(2, state.slotB);
            Assert.Equal(0f, state.weight);
        }

        [Fact]
        public void キーの区間を適用すると始点と終点を補間しライブ値は始点になる()
        {
            var state = new CameraHeightState();
            state.ApplyKeys(0, 1, 0.25f);
            Assert.Equal(0, state.slotA);
            Assert.Equal(1, state.slotB);
            Assert.Equal(0.25f, state.weight);
            Assert.Equal(0, state.liveSlotNo);
        }

        [Fact]
        public void 区間の終端ではライブ値は終点になる()
        {
            var state = new CameraHeightState();
            state.ApplyKeys(0, 1, 1f);
            Assert.Equal(1f, state.weight);
            Assert.Equal(1, state.liveSlotNo);
        }

        [Theory]
        [InlineData(-0.5f, 0f)]
        [InlineData(1.5f, 1f)]
        public void 区間の進み具合は0から1に収める(float t, float expected)
        {
            var state = new CameraHeightState();
            state.ApplyKeys(0, 1, t);
            Assert.Equal(expected, state.weight);
        }

        [Fact]
        public void 適用を始めると前に適用したキーの補間が残らない()
        {
            var state = new CameraHeightState();
            state.SetLive(3);
            state.ApplyKeys(0, 1, 0.5f);
            state.BeginApply();
            Assert.Equal(0, state.slotA);
            Assert.Equal(0, state.slotB);
            Assert.Equal(0f, state.weight);
        }

        [Fact]
        public void タイムラインを切り替えてもライブ値は残り補間は消える()
        {
            var state = new CameraHeightState();
            state.ApplyKeys(0, 1, 0.5f);
            state.ResetBlend();
            Assert.Equal(0, state.liveSlotNo);
            Assert.Equal(0, state.slotA);
            Assert.Equal(0, state.slotB);
            Assert.Equal(0f, state.weight);
        }
    }
}
