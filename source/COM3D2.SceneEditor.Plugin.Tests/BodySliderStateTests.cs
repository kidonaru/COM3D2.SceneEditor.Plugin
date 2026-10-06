using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>体型スライダーの値の保持と、骨ごとの合成を固定する</summary>
    public class BodySliderStateTests
    {
        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.Equal(expected.x, actual.x, 5);
            Assert.Equal(expected.y, actual.y, 5);
            Assert.Equal(expected.z, actual.z, 5);
        }

        private static Dictionary<string, BodySliderBoneOp> BuildOps(BodySliderState state)
        {
            var ops = new Dictionary<string, BodySliderBoneOp>();
            state.BuildBoneOps(ops);
            return ops;
        }

        [Fact]
        public void 未設定の項目は既定値で状態なし()
        {
            var state = new BodySliderState();
            Assert.True(state.isDefault);
            Assert.Equal(Vector3.one, state.Get("THISCL"));
            Assert.Equal(Vector3.zero, state.Get("THIPOS"));
        }

        [Fact]
        public void 設定すると丸めて保持し既定へ戻すと状態なしになる()
        {
            var state = new BodySliderState();
            state.Set("THISCL", new Vector3(9f, 1f, 1f));
            Assert.False(state.isDefault);
            Assert.Equal(2f, state.Get("THISCL").x);

            state.Set("THISCL", Vector3.one);
            Assert.True(state.isDefault);
        }

        [Fact]
        public void 定義に無いキーへの設定は無視する()
        {
            var state = new BodySliderState();
            state.Set("UNKNOWN", new Vector3(2f, 2f, 2f));
            state.Set(null, new Vector3(2f, 2f, 2f));
            Assert.True(state.isDefault);
        }

        [Fact]
        public void 左の上腕は左の骨にだけ掛かる()
        {
            var state = new BodySliderState();
            state.Set("UPARMSCL_L", new Vector3(2f, 2f, 2f));

            var ops = BuildOps(state);
            Assert.True(ops.ContainsKey("Bip01 L UpperArm"));
            Assert.False(ops.ContainsKey("Bip01 R UpperArm"));
            AssertVector(new Vector3(2f, 2f, 2f), ops["Bip01 L UpperArm"].scale);
            Assert.True(ops["Bip01 L UpperArm"].hasScale);
            Assert.False(ops["Bip01 L UpperArm"].hasOffset);
        }

        [Fact]
        public void 尻の骨は骨盤と尻のスケールの積になる()
        {
            var state = new BodySliderState();
            state.Set("PELSCL", new Vector3(2f, 1f, 1f));   // width → z
            state.Set("HIPSCL", new Vector3(1.5f, 1f, 1f));

            var ops = BuildOps(state);
            AssertVector(new Vector3(1f, 1f, 3f), ops["Hip_L"].scale);
            AssertVector(new Vector3(1f, 1f, 2f), ops["Bip01 Pelvis_SCL_"].scale);
        }

        [Fact]
        public void 尻の骨は足の位置と尻の位置の和になる()
        {
            var state = new BodySliderState();
            state.Set("THIPOS", new Vector3(10f, 0f, 0f));   // L: z = -X/1000
            state.Set("HIPPOS", new Vector3(0f, 20f, 0f));   // L: x = +Y/1000

            var ops = BuildOps(state);
            AssertVector(new Vector3(0.02f, 0f, -0.01f), ops["Hip_L"].offset);
            Assert.True(ops["Hip_L"].hasOffset);
            Assert.False(ops["Hip_L"].hasScale);
        }

        [Fact]
        public void 片方だけ既定でも合成できる()
        {
            var state = new BodySliderState();
            state.Set("HIPSCL", new Vector3(1f, 1f, 2f));   // height → x

            var ops = BuildOps(state);
            AssertVector(new Vector3(2f, 1f, 1f), ops["Hip_R"].scale);
            Assert.False(ops.ContainsKey("Bip01 Pelvis_SCL_"));
        }

        [Fact]
        public void 合成の前に結果を空にする()
        {
            var state = new BodySliderState();
            var ops = new Dictionary<string, BodySliderBoneOp>
            {
                { "Bip01 Head", new BodySliderBoneOp() },
            };
            state.BuildBoneOps(ops);
            Assert.Empty(ops);
        }

        [Fact]
        public void 自分が書いた値のままなら戻し他から書き換えられていたら戻さない()
        {
            Assert.True(BodySliderState.ShouldRestore(new Vector3(2f, 2f, 2f), new Vector3(2f, 2f, 2f)));
            Assert.False(BodySliderState.ShouldRestore(new Vector3(1f, 2f, 2f), new Vector3(2f, 2f, 2f)));
        }

        [Fact]
        public void Clearで全項目を既定へ戻す()
        {
            var state = new BodySliderState();
            state.Set("THISCL", new Vector3(1.5f, 1f, 1f));
            state.Clear();
            Assert.True(state.isDefault);
        }
    }
}
