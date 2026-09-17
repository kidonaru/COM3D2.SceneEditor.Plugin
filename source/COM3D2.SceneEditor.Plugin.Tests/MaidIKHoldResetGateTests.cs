using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 固定 IK の目標取り直しは、要求されたフレームでは行わず翌フレーム以降に行う。
    /// ゲーム側の LateUpdate (Maid.OffsetUpdate / TBody.AutoTwist) がプラグインより後に走り、
    /// ポーズ変更のフレームではまだ体の高さや前腕のスケールが確定していない。
    /// 確定前の位置を目標にすると翌フレームに腕脚が引き戻される
    /// </summary>
    public class MaidIKHoldResetGateTests
    {
        [Fact]
        public void 要求と同じフレームでは取り直さない()
        {
            Assert.False(MaidIKHoldResetGate.ShouldCapture(requestedFrame: 100, currentFrame: 100));
        }

        [Fact]
        public void 要求の翌フレームで取り直す()
        {
            Assert.True(MaidIKHoldResetGate.ShouldCapture(requestedFrame: 100, currentFrame: 101));
        }

        [Fact]
        public void 要求が無ければ取り直さない()
        {
            Assert.False(MaidIKHoldResetGate.ShouldCapture(MaidIKHoldResetGate.NoRequest, currentFrame: 100));
        }

        [Fact]
        public void 固定が動かすボーンは四肢チェーンの全ボーン()
        {
            var names = MaidIKHoldController.SolvedBoneNames;

            Assert.Equal(12, names.Length);
            Assert.Contains("Bip01 R UpperArm", names);
            Assert.Contains("Bip01 R Forearm", names);
            Assert.Contains("Bip01 R Hand", names);
            Assert.Contains("Bip01 L Thigh", names);
            Assert.Contains("Bip01 L Calf", names);
            Assert.Contains("Bip01 L Foot", names);
        }
    }
}
