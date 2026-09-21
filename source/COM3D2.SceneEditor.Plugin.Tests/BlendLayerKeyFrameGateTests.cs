using System;
using COM3D2.MotionTimelineEditor.Plugin;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 適用先がブレンド層の間、ボーン由来のレイヤーへキーを登録しない判定を固定する。
    /// この間はボーンを触れない代わりに層が有効なままで、ボーンにブレンドの寄与が乗っている。
    /// そのまま登録するとブレンド込みの値がメイドアニメへ焼き込まれてしまう
    /// </summary>
    public class BlendLayerKeyFrameGateTests
    {
        [Theory]
        [InlineData(typeof(MotionTimelineLayer), true, true)]
        [InlineData(typeof(MotionTimelineLayer), false, false)]
        public void ブレンド層選択中はメイドアニメを外す(
            Type layerType, bool isLayerSelected, bool expected)
        {
            Assert.Equal(expected,
                MaidAnimationBlendController.ShouldSkipBoneKeyFrame(layerType, isLayerSelected));
        }

        /// <summary>
        /// メイド移動はブレンドの影響を受けないメイドルートの値で、レイヤー選択中も
        /// ルートのギズモで動かせる。外すと動かした分がキーにならず消えてしまう
        /// </summary>
        [Theory]
        [InlineData(typeof(MoveTimelineLayer), true)]
        [InlineData(typeof(MoveTimelineLayer), false)]
        public void メイド移動は外さない(Type layerType, bool isLayerSelected)
        {
            Assert.False(
                MaidAnimationBlendController.ShouldSkipBoneKeyFrame(layerType, isLayerSelected));
        }

        /// <summary>
        /// ブレンド層自身 (メイドアニメブレンド) はボーンではなく層の値をキー化するので、
        /// 適用先がレイヤーの間こそ登録できないと重み等を記録できなくなる
        /// </summary>
        [Theory]
        [InlineData(typeof(AnimationTimelineLayer), true)]
        [InlineData(typeof(AnimationTimelineLayer), false)]
        public void ブレンドレイヤー自身は常に登録できる(Type layerType, bool isLayerSelected)
        {
            Assert.False(
                MaidAnimationBlendController.ShouldSkipBoneKeyFrame(layerType, isLayerSelected));
        }

        /// <summary>ボーンを持たないレイヤー (表情など) はブレンドの影響を受けない</summary>
        [Theory]
        [InlineData(typeof(MorphTimelineLayer), true)]
        [InlineData(typeof(CameraTimelineLayer), true)]
        public void ボーン由来でないレイヤーは外さない(Type layerType, bool isLayerSelected)
        {
            Assert.False(
                MaidAnimationBlendController.ShouldSkipBoneKeyFrame(layerType, isLayerSelected));
        }

        [Fact]
        public void 型が不明なら外さない()
        {
            Assert.False(MaidAnimationBlendController.ShouldSkipBoneKeyFrame(null, true));
        }
    }
}
