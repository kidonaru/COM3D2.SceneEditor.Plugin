using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>タイムライン視線 → SE 向け先モードの写像</summary>
    public class MaidLookBridgeTests
    {
        [Theory]
        [InlineData(MTEP.LookAtTargetType.Camera, true, MaidLookMode.カメラ)]
        [InlineData(MTEP.LookAtTargetType.Camera, false, MaidLookMode.カメラ)]
        [InlineData(MTEP.LookAtTargetType.Maid, true, MaidLookMode.オブジェクト)]
        [InlineData(MTEP.LookAtTargetType.Model, true, MaidLookMode.オブジェクト)]
        [InlineData(MTEP.LookAtTargetType.None, false, MaidLookMode.方向指定)]
        public void ResolveLookMode_キー化中は注視先種別を向け先モードへ写す(
            MTEP.LookAtTargetType targetType, bool hasTarget, MaidLookMode expected)
        {
            var mode = MaidLookBridge.ResolveLookMode(
                true, targetType, hasTarget, isEyeSorashi: false);
            Assert.Equal(expected, mode);
        }

        [Theory]
        [InlineData(MTEP.LookAtTargetType.Maid)]
        [InlineData(MTEP.LookAtTargetType.Model)]
        public void ResolveLookMode_注視対象が未解決なら顔向きの方向指定へ倒す(
            MTEP.LookAtTargetType targetType)
        {
            var mode = MaidLookBridge.ResolveLookMode(
                true, targetType, false, isEyeSorashi: false);
            Assert.Equal(MaidLookMode.方向指定, mode);
        }

        [Theory]
        [InlineData(MTEP.LookAtTargetType.None)]
        [InlineData(MTEP.LookAtTargetType.Maid)]
        public void ResolveLookMode_注視先なしでそらし中は向け先無しにする(
            MTEP.LookAtTargetType targetType)
        {
            // そらし演出は trsLookTarget == null のときだけ動くため、
            // 方向指定の注視点を作らず向け先を空ける
            var mode = MaidLookBridge.ResolveLookMode(
                true, targetType, false, isEyeSorashi: true);
            Assert.Equal(MaidLookMode.無し, mode);
        }

        [Fact]
        public void ResolveLookMode_注視先ありならそらし中でも注視先を優先する()
        {
            var mode = MaidLookBridge.ResolveLookMode(
                true, MTEP.LookAtTargetType.Camera, true, isEyeSorashi: true);
            Assert.Equal(MaidLookMode.カメラ, mode);
        }

        [Fact]
        public void ResolveLookMode_キー化が無効ならSEの向け先を変更しない()
        {
            var mode = MaidLookBridge.ResolveLookMode(
                false, MTEP.LookAtTargetType.Camera, true, isEyeSorashi: false);
            Assert.Null(mode);
        }

        [Theory]
        [InlineData(Maid.EyeMoveType.無し, false, false, false)]
        [InlineData(Maid.EyeMoveType.無視する, false, false, false)]
        [InlineData(Maid.EyeMoveType.顔を向ける, true, true, false)]
        [InlineData(Maid.EyeMoveType.顔だけ動かす, true, false, false)]
        [InlineData(Maid.EyeMoveType.顔をそらす, true, true, true)]
        [InlineData(Maid.EyeMoveType.目と顔を向ける, true, true, false)]
        [InlineData(Maid.EyeMoveType.目だけ向ける, false, true, false)]
        [InlineData(Maid.EyeMoveType.目だけそらす, false, true, true)]
        public void 目線種別のフラグはEyeToCameraと同じ組み合わせになる(
            Maid.EyeMoveType eyeMoveType, bool headToCam, bool eyeToCam, bool eyeSorashi)
        {
            Assert.Equal(headToCam, MaidLookBridge.IsHeadToCam(eyeMoveType));
            Assert.Equal(eyeToCam, MaidLookBridge.IsEyeToCam(eyeMoveType));
            Assert.Equal(eyeSorashi, MaidLookBridge.IsEyeSorashi(eyeMoveType));
        }
    }
}
