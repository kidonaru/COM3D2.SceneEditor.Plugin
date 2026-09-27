using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class AngleUtilsTests
    {
        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(90f, 90f)]
        [InlineData(180f, 180f)]
        // 旋回で 180 を超えた角度は負側へ折り返す
        [InlineData(181f, -179f)]
        [InlineData(270f, -90f)]
        [InlineData(360f, 0f)]
        // 何周ぶんでも (-180, 180] へ収める
        [InlineData(730f, 10f)]
        [InlineData(-90f, -90f)]
        [InlineData(-190f, 170f)]
        [InlineData(-730f, -10f)]
        public void NormalizeAngle_角度を180度以内へ正規化する(float angle, float expected)
        {
            Assert.Equal(expected, AngleUtils.NormalizeAngle(angle), 3);
        }

        [Theory]
        // 差が 180 度以内ならそのまま
        [InlineData(0f, 0f, 0f)]
        [InlineData(90f, 0f, 90f)]
        [InlineData(180f, 0f, 180f)]
        // 180 度を超えたら 360 度単位で前の角度側へ寄せる
        [InlineData(200f, 0f, -160f)]
        [InlineData(-200f, 0f, 160f)]
        [InlineData(370f, 10f, 10f)]
        [InlineData(730f, 0f, 10f)]
        // 旧実装の (int) 切り捨てでは補正が発火しなかった 180.0〜181.0 度の帯を塞ぐ
        [InlineData(180.5f, 0f, -179.5f)]
        [InlineData(-180.5f, 0f, 179.5f)]
        public void GetFixedAngle_前の角度から180度以内へ寄せる(float angle, float prevAngle, float expected)
        {
            Assert.Equal(expected, AngleUtils.GetFixedAngle(angle, prevAngle), 3);
        }

        [Fact]
        public void GetFixedAngles_成分ごとに前の角度へ寄せる()
        {
            var actual = AngleUtils.GetFixedAngles(
                new Vector3(200f, -200f, 370f),
                new Vector3(0f, 0f, 10f));

            Assert.Equal(-160f, actual.x, 3);
            Assert.Equal(160f, actual.y, 3);
            Assert.Equal(10f, actual.z, 3);
        }

        [Fact]
        public void NormalizeAngles_成分ごとに180度以内へ正規化する()
        {
            var actual = AngleUtils.NormalizeAngles(new Vector3(200f, -200f, 180f));

            Assert.Equal(-160f, actual.x, 3);
            Assert.Equal(160f, actual.y, 3);
            Assert.Equal(180f, actual.z, 3);
        }

        private static void AssertAngles(Vector3 expected, Vector3 actual)
        {
            Assert.Equal(expected.x, actual.x, 3);
            Assert.Equal(expected.y, actual.y, 3);
            Assert.Equal(expected.z, actual.z, 3);
        }

        [Fact]
        public void GetContinuousEulerAngles_縦回転90度以内の前回値ならUnityの分解のまま返す()
        {
            var actual = AngleUtils.GetContinuousEulerAngles(
                new Vector3(30f, 10f, 350f),
                new Vector3(28f, 12f, -8f));

            AssertAngles(new Vector3(30f, 10f, -10f), actual);
        }

        [Fact]
        public void GetContinuousEulerAngles_真上を越えた前回値なら縦回転を90度超で返す()
        {
            // (120, 10, 0) は Unity では (60, 190, 180) に分解される
            var actual = AngleUtils.GetContinuousEulerAngles(
                new Vector3(60f, 190f, 180f),
                new Vector3(115f, 10f, 0f));

            AssertAngles(new Vector3(120f, 10f, 0f), actual);
        }

        [Fact]
        public void GetContinuousEulerAngles_真下を越えた前回値なら縦回転をマイナス90度超で返す()
        {
            // (-120, 10, 0) は Unity では (300, 190, 180) に分解される
            var actual = AngleUtils.GetContinuousEulerAngles(
                new Vector3(300f, 190f, 180f),
                new Vector3(-115f, 10f, 0f));

            AssertAngles(new Vector3(-120f, 10f, 0f), actual);
        }

        [Fact]
        public void GetContinuousEulerAngles_同じ向きでも前回値が通常範囲なら分解のまま返す()
        {
            var actual = AngleUtils.GetContinuousEulerAngles(
                new Vector3(60f, 190f, 180f),
                new Vector3(55f, -170f, 180f));

            AssertAngles(new Vector3(60f, -170f, 180f), actual);
        }
    }
}
