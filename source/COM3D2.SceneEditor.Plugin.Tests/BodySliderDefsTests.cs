using System.Linq;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>体型スライダーの項目定義を固定する。変換は MaidVoicePitch v0.2.17.6 の WideSlider に合わせている</summary>
    public class BodySliderDefsTests
    {
        private static void AssertVector(Vector3 expected, Vector3 actual)
        {
            Assert.Equal(expected.x, actual.x, 5);
            Assert.Equal(expected.y, actual.y, 5);
            Assert.Equal(expected.z, actual.z, 5);
        }

        [Fact]
        public void 項目キーは重複しない()
        {
            var keys = BodySliderDefs.items.Select(i => i.key).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }

        [Fact]
        public void 腕は左右別の項目で肩は左右共通()
        {
            Assert.NotNull(BodySliderDefs.Find("UPARMSCL_L"));
            Assert.NotNull(BodySliderDefs.Find("UPARMSCL_R"));
            Assert.NotNull(BodySliderDefs.Find("FARMSCL_L"));
            Assert.NotNull(BodySliderDefs.Find("HANDSCL_R"));
            Assert.Null(BodySliderDefs.Find("UPARMSCL"));
            Assert.NotNull(BodySliderDefs.Find("KATASCL"));
            Assert.NotNull(BodySliderDefs.Find("CLVPOS"));

            var left = BodySliderDefs.Find("UPARMSCL_L");
            Assert.Single(left.targets);
            Assert.Equal("Bip01 L UpperArm", left.targets[0].boneName);
            Assert.Equal("上腕スケーリング(左)", left.displayName);
        }

        [Fact]
        public void 対象外の項目は定義に無い()
        {
            Assert.Null(BodySliderDefs.Find("S1ABASESCL"));
            Assert.Null(BodySliderDefs.Find("FARMFIX"));
            Assert.Null(BodySliderDefs.Find(null));
            Assert.Null(BodySliderDefs.Find(""));
        }

        [Fact]
        public void 全項目がいずれかのグループに属する()
        {
            foreach (var item in BodySliderDefs.items)
            {
                Assert.Contains(item.group, BodySliderDefs.groupNames);
            }
            var total = BodySliderDefs.groupNames.Sum(g => BodySliderDefs.ItemsInGroup(g).Count());
            Assert.Equal(BodySliderDefs.items.Count, total);
        }

        [Fact]
        public void スケールは幅奥行高さを骨のzyxへ割り当てる()
        {
            var item = BodySliderDefs.Find("THISCL");
            // values = (width, depth, height)
            AssertVector(new Vector3(3f, 2f, 1f), item.ToScale(new Vector3(1f, 2f, 3f)));
        }

        [Fact]
        public void 位置は係数で割り左右で符号が変わる()
        {
            var item = BodySliderDefs.Find("THIPOS");
            var left = item.targets.First(t => t.boneName == "Bip01 L Thigh");
            var right = item.targets.First(t => t.boneName == "Bip01 R Thigh");
            var values = new Vector3(10f, 999f, 20f); // y は使わない

            AssertVector(new Vector3(0f, 0.02f, -0.01f), item.ToOffset(values, left));
            AssertVector(new Vector3(0f, 0.02f, 0.01f), item.ToOffset(values, right));
        }

        [Fact]
        public void 胸の位置は軸を入れ替える()
        {
            var item = BodySliderDefs.Find("MUNEPOS");
            var left = item.targets.First(t => t.boneName == "Mune_L");
            var right = item.targets.First(t => t.boneName == "Mune_R");
            var values = new Vector3(0.1f, 0.2f, 0.3f);

            AssertVector(new Vector3(0.03f, -0.02f, 0.01f), item.ToOffset(values, left));
            AssertVector(new Vector3(0.03f, -0.02f, -0.01f), item.ToOffset(values, right));
        }

        [Fact]
        public void 足の位置は尻の骨にも掛かる()
        {
            var item = BodySliderDefs.Find("THIPOS");
            Assert.Contains(item.targets, t => t.boneName == "Hip_L");
            Assert.Contains(item.targets, t => t.boneName == "Hip_R");
        }

        [Fact]
        public void 範囲はModsParamに合わせる()
        {
            var thi = BodySliderDefs.Find("THISCL").components[0];
            Assert.Equal(0.1f, thi.min);
            Assert.Equal(2f, thi.max);
            var arm = BodySliderDefs.Find("UPARMSCL_L").components[0];
            Assert.Equal(3f, arm.max);
            var pos = BodySliderDefs.Find("THIPOS").components[0];
            Assert.Equal(-100f, pos.min);
            Assert.Equal(200f, pos.max);
        }

        [Fact]
        public void 非表示の成分はModsParamに合わせる()
        {
            Assert.False(BodySliderDefs.Find("THISCL").components[2].visible);  // height
            Assert.True(BodySliderDefs.Find("THISCL").components[0].visible);
            Assert.False(BodySliderDefs.Find("THIPOS").components[1].visible);  // y
            Assert.False(BodySliderDefs.Find("SKTPOS").components[0].visible);  // x
            Assert.False(BodySliderDefs.Find("SKTPOS").components[1].visible);  // y
            Assert.True(BodySliderDefs.Find("SKTPOS").components[2].visible);   // z
        }

        [Theory]
        [InlineData(float.NaN, 1f)]
        [InlineData(0f, 0.1f)]
        [InlineData(9f, 2f)]
        [InlineData(1.5f, 1.5f)]
        public void スケールは範囲へ丸めNaNは1にする(float input, float expected)
        {
            var item = BodySliderDefs.Find("THISCL");
            Assert.Equal(expected, item.Clamp(new Vector3(input, 1f, 1f)).x);
        }

        [Fact]
        public void 位置のNaNは0にする()
        {
            var item = BodySliderDefs.Find("SPIPOS");
            Assert.Equal(0f, item.Clamp(new Vector3(float.NaN, 0f, 0f)).x);
            Assert.True(item.IsDefault(new Vector3(float.NaN, 0f, 0f)));
        }

        [Fact]
        public void 既定値はスケール1と位置0()
        {
            Assert.Equal(Vector3.one, BodySliderDefs.Find("THISCL").defaultValues);
            Assert.Equal(Vector3.zero, BodySliderDefs.Find("THIPOS").defaultValues);
            Assert.True(BodySliderDefs.Find("THISCL").IsDefault(new Vector3(1.000001f, 1f, 1f)));
            Assert.False(BodySliderDefs.Find("THISCL").IsDefault(new Vector3(1.5f, 1f, 1f)));
        }

        [Fact]
        public void 対象骨の判定はスケールと位置の両方を含む()
        {
            Assert.True(BodySliderDefs.IsTargetBone("Bip01 L UpperArm"));
            Assert.True(BodySliderDefs.IsTargetBone("Bip01 Spine"));
            Assert.True(BodySliderDefs.IsTargetBone("Hip_R"));
            Assert.False(BodySliderDefs.IsTargetBone("Bip01 Head"));
            Assert.False(BodySliderDefs.IsTargetBone(null));
        }

        [Fact]
        public void 旧メイドスケールの骨名は腕の左右別項目へ対応する()
        {
            Assert.Equal("UPARMSCL_L", BodySliderDefs.legacyMaidScaleKeys["Bip01 L UpperArm"]);
            Assert.Equal("FARMSCL_R", BodySliderDefs.legacyMaidScaleKeys["Bip01 R Forearm"]);
            Assert.Equal("HANDSCL_L", BodySliderDefs.legacyMaidScaleKeys["Bip01 L Hand"]);
            Assert.Equal(6, BodySliderDefs.legacyMaidScaleKeys.Count);
        }
    }
}
