using UnityEngine;
using Xunit;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ギズモサイズの fov 補正を検証する。
    /// Camera の実体は Unity ランタイムが要るため、投影から求まる
    /// 「対象位置での画面半分の高さ」を直接渡す純関数側で確かめる
    /// </summary>
    public class GizmoSizeFovTests
    {
        private const float ReferenceFov = 45f;

        private static float HalfHeight(float fov, float distance)
        {
            return Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * distance;
        }

        [Fact]
        public void 基準fovでは補正前と同じ大きさになる()
        {
            // 補正前は「距離 × 0.15」。基準 fov ではその値と一致させ、既存の見た目を保つ
            var size = TransformGizmo.CalcGizmoSizeFromHalfHeight(HalfHeight(ReferenceFov, 10f));
            Assert.Equal(10f * 0.15f, size, 4);
        }

        [Theory]
        [InlineData(20f)]
        [InlineData(35f)]
        [InlineData(60f)]
        public void 画面に占める割合はfovによらず一定になる(float fov)
        {
            // 画面高さに対する比 = サイズ / (2 × 画面半分の高さ)
            var half = HalfHeight(fov, 10f);
            var ratio = TransformGizmo.CalcGizmoSizeFromHalfHeight(half) / (2f * half);

            var referenceHalf = HalfHeight(ReferenceFov, 10f);
            var referenceRatio = TransformGizmo.CalcGizmoSizeFromHalfHeight(referenceHalf) / (2f * referenceHalf);

            Assert.Equal(referenceRatio, ratio, 5);
        }

        [Fact]
        public void 望遠ほど世界サイズは小さくなる()
        {
            // fov が小さいほど同じ距離でも画面に大きく写るため、世界サイズは縮める
            var narrow = TransformGizmo.CalcGizmoSizeFromHalfHeight(HalfHeight(20f, 10f));
            var wide = TransformGizmo.CalcGizmoSizeFromHalfHeight(HalfHeight(60f, 10f));
            Assert.True(narrow < wide);
        }
    }
}
