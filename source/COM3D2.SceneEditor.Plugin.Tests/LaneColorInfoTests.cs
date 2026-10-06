using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class LaneColorInfoTests
    {
        /// <summary>values[0..2] に副色の RGB、values[3..6] に主色の RGBA を持つ (主色がマップの先頭ではない)</summary>
        private class FakeColorTransform : TransformDataBase
        {
            public override TransformType type => TransformType.None;
            public override int valueCount => 7;

            private static readonly Dictionary<string, ColorValueInfo> ColorValueInfoMap =
                new Dictionary<string, ColorValueInfo>
                {
                    { ColorKey.Sub, ColorValueInfo.Rgb("副色", 0, Color.black) },
                    { ColorKey.Main, ColorValueInfo.Rgba("色", 3, Color.white) },
                };

            public override Dictionary<string, ColorValueInfo> GetColorValueInfoMap() => ColorValueInfoMap;
        }

        /// <summary>主色を持たず、values[0..2] と values[3..5] に RGB を持つ型</summary>
        private class FakeNoMainColorTransform : TransformDataBase
        {
            public override TransformType type => TransformType.None;
            public override int valueCount => 6;

            private static readonly Dictionary<string, ColorValueInfo> ColorValueInfoMap =
                new Dictionary<string, ColorValueInfo>
                {
                    { "first", ColorValueInfo.Rgb("1", 0, Color.black) },
                    { "second", ColorValueInfo.Rgb("2", 3, Color.black) },
                };

            public override Dictionary<string, ColorValueInfo> GetColorValueInfoMap() => ColorValueInfoMap;
        }

        /// <summary>色を持たず、values[0] を表示フラグに持つ型</summary>
        private class FakeVisibleTransform : TransformDataBase
        {
            public override TransformType type => TransformType.None;
            public override int valueCount => 1;
            public override bool hasVisible => true;
            public override ValueData visibleValue => values[0];
        }

        private class FakePlainTransform : TransformDataBase
        {
            public override TransformType type => TransformType.None;
            public override int valueCount => 1;
        }

        private static T Create<T>() where T : TransformDataBase, new()
        {
            var trans = new T();
            trans.Initialize("test");
            return trans;
        }

        [Fact]
        public void 複数色を持つ型は主色を出す()
        {
            var trans = Create<FakeColorTransform>();
            trans.SetColorValue(TransformDataBase.ColorKey.Main, new Color(0.1f, 0.2f, 0.3f, 0.4f));
            trans.SetColorValue(TransformDataBase.ColorKey.Sub, new Color(0.5f, 0.6f, 0.7f));

            var info = trans.GetLaneColorInfo();

            Assert.Equal(new Color(0.1f, 0.2f, 0.3f, 0.4f), trans.GetLaneColor(info));
            Assert.False(info.isStep);
        }

        [Fact]
        public void 主色が無ければ色マップの先頭を出す()
        {
            var trans = Create<FakeNoMainColorTransform>();
            trans.SetColorValue("first", new Color(0.1f, 0.2f, 0.3f));
            trans.SetColorValue("second", new Color(0.5f, 0.6f, 0.7f));

            Assert.Equal(new Color(0.1f, 0.2f, 0.3f, 1f), trans.GetLaneColor(trans.GetLaneColorInfo()));
        }

        [Fact]
        public void 既定の帯定義は同じ型で使い回す()
        {
            Assert.Same(
                Create<FakeColorTransform>().GetLaneColorInfo(),
                Create<FakeColorTransform>().GetLaneColorInfo());
        }

        [Fact]
        public void 色も表示フラグも持たない型は帯を持たない()
        {
            Assert.Null(Create<FakePlainTransform>().GetLaneColorInfo());
        }

        [Fact]
        public void 色を持たず表示フラグを持つ型は表示のONOFFを出す()
        {
            var trans = Create<FakeVisibleTransform>();
            var info = trans.GetLaneColorInfo();

            Assert.True(info.isStep);
            trans.visible = true;
            Assert.Equal(LaneColorInfo.DefaultColor, trans.GetLaneColor(info));
            trans.visible = false;
            Assert.Equal(Color.clear, trans.GetLaneColor(info));
        }

        [Theory]
        [InlineData(0.25f, 0.25f)]
        [InlineData(-0.5f, 0f)]
        [InlineData(1.5f, 1f)]
        public void 値の透明度は0から1に丸める(float value, float expectedAlpha)
        {
            var trans = Create<TransformDataMorph>();
            trans.morphValue = value;

            var info = trans.GetLaneColorInfo();

            Assert.False(info.isStep);
            var expected = LaneColorInfo.DefaultColor;
            expected.a = expectedAlpha;
            Assert.Equal(expected, trans.GetLaneColor(info));
        }

        public static IEnumerable<object[]> LaneCases()
        {
            // 型, 値の添字, ON/OFF か
            yield return new object[] { new TransformDataShapeKey(), (int)TransformDataShapeKey.Index.Weight, false };
            yield return new object[] { new TransformDataModelShapeKey(), (int)TransformDataModelShapeKey.Index.Weight, false };
            yield return new object[] { new TransformDataAnimation(), (int)TransformDataAnimation.Index.Weight, false };
            yield return new object[] { new TransformDataUndress(), (int)TransformDataUndress.Index.IsVisible, true };
            yield return new object[] { new TransformDataNodeVisibility(), (int)TransformDataNodeVisibility.Index.IsVisible, true };
            yield return new object[] { new TransformDataGravity(), (int)TransformDataGravity.Index.Enabled, true };
            yield return new object[] { new TransformDataFaceSetting(), (int)TransformDataFaceSetting.Index.ForceOverride, true };
            yield return new object[] { new TransformDataIKHold(), (int)TransformDataIKHold.Index.IsHold, true };
        }

        [Theory]
        [MemberData(nameof(LaneCases))]
        public void 各型の帯は対象の値を既定色で出す(TransformDataBase trans, int valueIndex, bool isStep)
        {
            trans.Initialize("test");
            var info = trans.GetLaneColorInfo();

            Assert.Equal(isStep, info.isStep);
            trans.values[valueIndex].value = 0f;
            Assert.Equal(0f, trans.GetLaneColor(info).a);
            trans.values[valueIndex].value = 1f;
            Assert.Equal(LaneColorInfo.DefaultColor, trans.GetLaneColor(info));
        }

        [Fact]
        public void IK固定はアニメのONOFFを帯に出さない()
        {
            var trans = Create<TransformDataIKHold>();
            trans.values[(int)TransformDataIKHold.Index.IsHold].value = 0f;
            trans.values[(int)TransformDataIKHold.Index.IsAnime].value = 1f;

            Assert.Equal(Color.clear, trans.GetLaneColor(trans.GetLaneColorInfo()));
        }

        [Theory]
        [InlineData(0f, 0f, false)]
        [InlineData(1f, 0f, true)]
        [InlineData(0f, 1f, true)]
        [InlineData(1f, 1f, true)]
        public void 接地はどちらかの足が接地していればON(float left, float right, bool expectedOn)
        {
            var trans = Create<TransformDataGrounding>();
            trans.values[(int)TransformDataGrounding.Index.IsGroundingFootL].value = left;
            trans.values[(int)TransformDataGrounding.Index.IsGroundingFootR].value = right;

            var color = trans.GetLaneColor(trans.GetLaneColorInfo());

            Assert.Equal(expectedOn ? LaneColorInfo.DefaultColor : Color.clear, color);
        }
    }
}
