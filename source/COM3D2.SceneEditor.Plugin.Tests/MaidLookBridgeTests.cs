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
        [InlineData(MTEP.LookAtTargetType.None, false, MaidLookMode.無し)]
        public void ResolveLookMode_固定化中は注視先種別を向け先モードへ写す(
            MTEP.LookAtTargetType targetType, bool hasTarget, MaidLookMode expected)
        {
            var mode = MaidLookBridge.ResolveLookMode(true, targetType, hasTarget);
            Assert.Equal(expected, mode);
        }

        [Theory]
        [InlineData(MTEP.LookAtTargetType.Maid)]
        [InlineData(MTEP.LookAtTargetType.Model)]
        public void ResolveLookMode_注視対象が未解決なら向け先無しへ倒す(
            MTEP.LookAtTargetType targetType)
        {
            var mode = MaidLookBridge.ResolveLookMode(true, targetType, false);
            Assert.Equal(MaidLookMode.無し, mode);
        }

        [Fact]
        public void ResolveLookMode_固定化が無効ならSEの向け先を変更しない()
        {
            var mode = MaidLookBridge.ResolveLookMode(
                false, MTEP.LookAtTargetType.Camera, true);
            Assert.Null(mode);
        }

        [Theory]
        [InlineData(Maid.EyeMoveType.顔をそらす)]
        [InlineData(Maid.EyeMoveType.目だけそらす)]
        public void ResolveLookMode_視線そらしでもSEの向け先は奪わない(
            Maid.EyeMoveType eyeMoveType)
        {
            // そらしは向け先を要求するが、所有者は SE 側。
            // 固定化が無効なら SE の向け先を書き換えず、そらしは向け先「無し」のときだけ効く
            Assert.Null(MaidLookBridge.ResolveLookMode(
                false, MTEP.LookAtTargetType.Camera, true));
            Assert.Equal(MaidLookMode.カメラ, MaidLookBridge.ResolveLookMode(
                true, MTEP.LookAtTargetType.Camera, true));
            Assert.True(MaidLookBridge.IsEyeSorashi(eyeMoveType));
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
