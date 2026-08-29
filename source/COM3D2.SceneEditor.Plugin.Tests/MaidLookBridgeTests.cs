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

        [Theory]
        [InlineData(MTEP.LookAtTargetType.None, MaidLookMode.方向指定)]
        [InlineData(MTEP.LookAtTargetType.Camera, MaidLookMode.カメラ)]
        [InlineData(MTEP.LookAtTargetType.Maid, MaidLookMode.メイド)]
        // モデル注視は選択肢に出さないため、表示上は方向指定へ丸める
        [InlineData(MTEP.LookAtTargetType.Model, MaidLookMode.方向指定)]
        public void ToLookMode_キーの注視先種別を統合列挙へ写す(
            MTEP.LookAtTargetType targetType, MaidLookMode expected)
        {
            Assert.Equal(expected, MaidLookBridge.ToLookMode(targetType));
        }

        [Theory]
        [InlineData(MaidLookMode.カメラ, MTEP.LookAtTargetType.Camera)]
        [InlineData(MaidLookMode.メイド, MTEP.LookAtTargetType.Maid)]
        [InlineData(MaidLookMode.方向指定, MTEP.LookAtTargetType.None)]
        // キー化できない値は顔向きキーで駆動する None へ丸める
        [InlineData(MaidLookMode.無し, MTEP.LookAtTargetType.None)]
        [InlineData(MaidLookMode.マウス, MTEP.LookAtTargetType.None)]
        [InlineData(MaidLookMode.オブジェクト, MTEP.LookAtTargetType.None)]
        public void ToTargetType_統合列挙をキーの注視先種別へ写す(
            MaidLookMode mode, MTEP.LookAtTargetType expected)
        {
            Assert.Equal(expected, MaidLookBridge.ToTargetType(mode));
        }

        [Fact]
        public void GetSelectableModes_キー化中はキー化できる値だけを出す()
        {
            Assert.Equal(
                new[] { MaidLookMode.カメラ, MaidLookMode.メイド, MaidLookMode.方向指定 },
                MaidLookBridge.GetSelectableModes(true));
        }

        [Fact]
        public void GetSelectableModes_キー化していなければ全ての向け先を出す()
        {
            Assert.Equal(
                new[]
                {
                    MaidLookMode.カメラ, MaidLookMode.マウス, MaidLookMode.方向指定,
                    MaidLookMode.メイド, MaidLookMode.オブジェクト, MaidLookMode.無し,
                },
                MaidLookBridge.GetSelectableModes(false));
        }

        [Fact]
        public void GetSelectableModes_返したリストを書き換えても次の呼び出しに影響しない()
        {
            var modes = MaidLookBridge.GetSelectableModes(true);
            modes.Clear();
            Assert.Equal(3, MaidLookBridge.GetSelectableModes(true).Count);
        }
    }
}
