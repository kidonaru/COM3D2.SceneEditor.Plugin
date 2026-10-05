using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 補間はクォータニオンの成分ごとに行うため、隣のキーと内積が負だと遠回りして棒が回る。
    /// FixRotation が前のキーに合わせて符号をそろえることを固定する
    /// </summary>
    public class FixRotationTests
    {
        private static readonly Quaternion Prev = new Quaternion(0.5f, 0f, 0f, 0.8660254f);
        // Prev と内積が負になる別姿勢（実データで右手だけ符号が反転していたキー）
        private static readonly Quaternion Flipped =
            new Quaternion(-0.4884f, -0.1083f, 0.1867f, -0.8452f);

        private static TransformDataPsylliumTransform Create(
            Quaternion rotation, Quaternion subRotation)
        {
            var trans = new TransformDataPsylliumTransform();
            trans.Initialize("PsylliumTransform (0, 0)");
            trans.rotation = rotation;
            trans.subRotation = subRotation;
            return trans;
        }

        [Fact]
        public void 左手の符号を前のキーへそろえる()
        {
            var prev = Create(Prev, Prev);
            var trans = Create(Flipped, Prev);

            trans.FixRotation(prev);

            Assert.True(Quaternion.Dot(prev.rotation, trans.rotation) > 0f);
        }

        [Fact]
        public void 右手の符号も前のキーへそろえる()
        {
            var prev = Create(Prev, Prev);
            var trans = Create(Prev, Flipped);

            trans.FixRotation(prev);

            Assert.True(Quaternion.Dot(prev.subRotation, trans.subRotation) > 0f);
            // 符号だけを反転する（同じ姿勢の別表現）
            Assert.Equal(-Flipped.x, trans.subRotation.x, 5);
            Assert.Equal(-Flipped.y, trans.subRotation.y, 5);
            Assert.Equal(-Flipped.z, trans.subRotation.z, 5);
            Assert.Equal(-Flipped.w, trans.subRotation.w, 5);
        }

        [Fact]
        public void 内積が正なら右手を変えない()
        {
            var original = new Quaternion(0.342f, 0f, 0f, 0.940f);
            var prev = Create(Prev, Prev);
            var trans = Create(Prev, original);

            trans.FixRotation(prev);

            Assert.Equal(original.w, trans.subRotation.w, 5);
            Assert.Equal(original.x, trans.subRotation.x, 5);
        }
    }
}
