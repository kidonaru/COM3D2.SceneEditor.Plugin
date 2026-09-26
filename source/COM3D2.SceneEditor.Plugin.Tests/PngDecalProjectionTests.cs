using System;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// デカールの投影箱の計算を固定する。
    /// 投影箱は root ローカルで X ±aspect.x/2・Y ±aspect.y/2・Z ±0.5 の箱
    /// </summary>
    public class PngDecalProjectionTests
    {
        private const float Tolerance = 1e-4f;

        private static void AssertNear(float expected, float actual)
        {
            Assert.True(Math.Abs(expected - actual) < Tolerance,
                string.Format("expected {0} but was {1}", expected, actual));
        }

        private static void AssertNear(Vector3 expected, Vector3 actual)
        {
            AssertNear(expected.x, actual.x);
            AssertNear(expected.y, actual.y);
            AssertNear(expected.z, actual.z);
        }

        private static void AssertFinite(float value)
        {
            Assert.False(float.IsNaN(value) || float.IsInfinity(value), "value = " + value);
        }

        /// <summary>root が position に平行移動し、各軸 scale で拡縮した worldToLocal (回転なし)</summary>
        private static Matrix4x4 WorldToLocal(Vector3 position, float scale)
        {
            var m = Matrix4x4.identity;
            m.m00 = 1f / scale;
            m.m11 = 1f / scale;
            m.m22 = 1f / scale;
            m.m03 = -position.x / scale;
            m.m13 = -position.y / scale;
            m.m23 = -position.z / scale;
            return m;
        }

        [Fact]
        public void 横長の画像は長辺を1にして短辺を比率にする()
        {
            var aspect = PngDecalProjection.GetAspect(200, 100);
            AssertNear(1f, aspect.x);
            AssertNear(0.5f, aspect.y);
        }

        [Fact]
        public void 縦長の画像は高さを1にする()
        {
            var aspect = PngDecalProjection.GetAspect(100, 400);
            AssertNear(0.25f, aspect.x);
            AssertNear(1f, aspect.y);
        }

        [Fact]
        public void 大きさが0の画像は正方形として扱う()
        {
            var aspect = PngDecalProjection.GetAspect(0, 100);
            AssertNear(1f, aspect.x);
            AssertNear(1f, aspect.y);
        }

        [Fact]
        public void 等倍の箱は余白込みで覆いProjectorを表の面の手前に置く()
        {
            var frame = PngDecalProjection.ComputeFrame(new Vector2(1f, 0.5f), Vector3.one);

            var m = PngDecalProjection.CullMargin;
            AssertNear(0.25f + m, frame.orthographicSize);
            AssertNear((0.5f + m) / (0.25f + m), frame.aspectRatio);
            AssertNear(m * 0.5f, frame.nearClipPlane);
            AssertNear(1f + m * 2f, frame.farClipPlane);
            AssertNear(new Vector3(0f, 0f, 0.5f + m), frame.localPosition);
            AssertNear(Vector3.one, frame.localScale);
        }

        [Fact]
        public void 拡縮した箱はワールド寸法で覆い子の拡縮で打ち消す()
        {
            var frame = PngDecalProjection.ComputeFrame(Vector2.one, new Vector3(2f, 4f, 3f));

            var m = PngDecalProjection.CullMargin;
            AssertNear(2f + m, frame.orthographicSize);
            AssertNear((1f + m) / (2f + m), frame.aspectRatio);
            AssertNear(3f + m * 2f, frame.farClipPlane);
            // root ローカルの位置は root の拡縮で伸びるため、ワールドで 1.5 + m になるよう割り戻す
            AssertNear((1.5f + m) / 3f, frame.localPosition.z);
            AssertNear(new Vector3(0.5f, 0.25f, 1f / 3f), frame.localScale);
        }

        [Fact]
        public void 負の奥行きでもProjectorは表側の外に立つ()
        {
            var frame = PngDecalProjection.ComputeFrame(Vector2.one, new Vector3(1f, 1f, -2f));

            var m = PngDecalProjection.CullMargin;
            // ワールドでの位置 = root の拡縮 × ローカル位置。符号が打ち消し合って表側 (+1 + m) になる
            AssertNear(1f + m, -2f * frame.localPosition.z);
            AssertNear(2f + m * 2f, frame.farClipPlane);
            AssertNear(-0.5f, frame.localScale.z);
        }

        [Fact]
        public void 拡縮0でも有限値を返す()
        {
            var frame = PngDecalProjection.ComputeFrame(Vector2.one, new Vector3(0f, 0f, 0f));

            AssertFinite(frame.orthographicSize);
            AssertFinite(frame.aspectRatio);
            AssertFinite(frame.farClipPlane);
            AssertFinite(frame.localPosition.z);
            AssertFinite(frame.localScale.x);
            AssertFinite(frame.localScale.y);
            AssertFinite(frame.localScale.z);
            Assert.True(frame.orthographicSize > 0f);
            Assert.True(frame.aspectRatio > 0f);
        }

        [Fact]
        public void 行列は箱の端を0_5へ写す()
        {
            var matrix = PngDecalProjection.ComputeDecalMatrix(Matrix4x4.identity, new Vector2(1f, 0.5f));

            AssertNear(new Vector3(0.5f, 0.5f, 0.5f), matrix.MultiplyPoint3x4(new Vector3(0.5f, 0.25f, 0.5f)));
            AssertNear(Vector3.zero, matrix.MultiplyPoint3x4(Vector3.zero));
        }

        [Fact]
        public void 行列はrootの移動と拡縮を反映する()
        {
            var worldToLocal = WorldToLocal(new Vector3(10f, 0f, 0f), 2f);
            var matrix = PngDecalProjection.ComputeDecalMatrix(worldToLocal, Vector2.one);

            AssertNear(new Vector3(0.5f, 0.25f, 0f), matrix.MultiplyPoint3x4(new Vector3(11f, 0.5f, 0f)));
        }

        [Fact]
        public void 箱の頂点はバウンズと同じ並びで返す()
        {
            var corners = new Vector3[8];
            PngDecalProjection.GetBoxCorners(Matrix4x4.identity, new Vector2(1f, 0.5f), corners);

            AssertNear(new Vector3(-0.5f, -0.25f, -0.5f), corners[0]);
            AssertNear(new Vector3(0.5f, -0.25f, -0.5f), corners[1]);
            AssertNear(new Vector3(-0.5f, 0.25f, -0.5f), corners[2]);
            AssertNear(new Vector3(-0.5f, -0.25f, 0.5f), corners[4]);
            AssertNear(new Vector3(0.5f, 0.25f, 0.5f), corners[7]);
        }

        [Fact]
        public void フェード角90度は真横で0になり60度から薄くなる()
        {
            var range = PngDecalProjection.GetFadeCosRange(90f);
            AssertNear(0f, range.x);
            AssertNear(0.5f, range.y);
        }

        [Fact]
        public void フェード角は1から90度に丸める()
        {
            AssertNear(1f, PngDecalProjection.ClampFadeAngle(0f));
            AssertNear(90f, PngDecalProjection.ClampFadeAngle(120f));

            // 0 度指定でも smoothstep の両端が一致しない
            var range = PngDecalProjection.GetFadeCosRange(0f);
            Assert.True(range.x < range.y);
        }
    }
}
