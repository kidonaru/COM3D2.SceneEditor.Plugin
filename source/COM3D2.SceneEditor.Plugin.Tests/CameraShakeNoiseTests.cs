using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class CameraShakeNoiseTests
    {
        [Fact]
        public void 同じ引数なら常に同じ値を返す()
        {
            var a = CameraShakeNoise.Sample(0, 1.25f, 42, 1f);
            var b = CameraShakeNoise.Sample(0, 1.25f, 42, 1f);
            Assert.Equal(a, b);
        }

        [Fact]
        public void シードが違えば波形が変わる()
        {
            var a = CameraShakeNoise.Sample(0, 1.25f, 42, 1f);
            var b = CameraShakeNoise.Sample(0, 1.25f, 43, 1f);
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void 軸が違えば波形が変わる()
        {
            var a = CameraShakeNoise.Sample(0, 1.25f, 42, 1f);
            var b = CameraShakeNoise.Sample(1, 1.25f, 42, 1f);
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void 振幅が1を超えない()
        {
            for (var i = 0; i < 200; i++)
            {
                var value = CameraShakeNoise.Sample(i % 6, i * 0.05f, 7, 1f);
                Assert.InRange(value, -1f, 1f);
            }
        }

        [Fact]
        public void 周波数倍率2倍は半分の時間で同じ位相になる()
        {
            var a = CameraShakeNoise.Sample(2, 3.0f, 11, 1f);
            var b = CameraShakeNoise.Sample(2, 1.5f, 11, 2f);
            Assert.Equal(a, b, 5);
        }

        [Fact]
        public void 振幅0ならオフセットも0()
        {
            var p = CameraShakeParams.Default;
            Vector3 pos, euler;
            CameraShakeNoise.Evaluate(p, 1.7f, out pos, out euler);
            Assert.Equal(Vector3.zero, pos);
            Assert.Equal(Vector3.zero, euler);
        }

        [Fact]
        public void 周波数倍率0ならオフセットも0()
        {
            // 位相が進まないだけの Sample はシード由来の非ゼロ定数を返すため、
            // 素通しするとカメラが固定量ずれたまま止まる
            var p = CameraShakeParams.Default;
            p.positionAmplitude = Vector3.one;
            p.rotationAmplitude = Vector3.one;
            p.frequencyScale = 0f;
            p.seed = 7;

            Vector3 pos, euler;
            CameraShakeNoise.Evaluate(p, 1.7f, out pos, out euler);

            Assert.Equal(Vector3.zero, pos);
            Assert.Equal(Vector3.zero, euler);
            // 早期リターンが無ければ非ゼロになることを押さえておく
            Assert.NotEqual(0f, CameraShakeNoise.Sample(0, 1.7f, 7, 0f));
        }

        [Fact]
        public void オフセットは振幅に比例する()
        {
            var p1 = CameraShakeParams.Default;
            p1.positionAmplitude = new Vector3(0.1f, 0f, 0f);
            var p2 = p1;
            p2.positionAmplitude = new Vector3(0.2f, 0f, 0f);

            Vector3 pos1, pos2, euler;
            CameraShakeNoise.Evaluate(p1, 1.7f, out pos1, out euler);
            CameraShakeNoise.Evaluate(p2, 1.7f, out pos2, out euler);

            Assert.Equal(pos1.x * 2f, pos2.x, 5);
        }

        [Fact]
        public void 位置と回転は別の軸番号を使うので独立に揺れる()
        {
            var p = CameraShakeParams.Default;
            p.positionAmplitude = Vector3.one;
            p.rotationAmplitude = Vector3.one;

            Vector3 pos, euler;
            CameraShakeNoise.Evaluate(p, 2.3f, out pos, out euler);

            Assert.NotEqual(pos.x, euler.x);
        }
    }
}
