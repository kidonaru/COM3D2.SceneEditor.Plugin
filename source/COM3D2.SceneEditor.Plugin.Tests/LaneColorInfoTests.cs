using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class LaneColorInfoTests
    {
        /// <summary>values[0..3] に RGBA、values[4..6] に RGB を持つ型</summary>
        private class FakeColorTransform : TransformDataBase
        {
            public override TransformType type => TransformType.None;
            public override int valueCount => 7;

            private static readonly Dictionary<string, ColorValueInfo> ColorValueInfoMap =
                new Dictionary<string, ColorValueInfo>
                {
                    { ColorKey.Main, ColorValueInfo.Rgba("色", 0, Color.white) },
                    { "rgb", ColorValueInfo.Rgb("RGB", 4, Color.black) },
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
        public void 既定の帯定義は色マップの順に色を出す()
        {
            var trans = Create<FakeColorTransform>();
            trans.SetColorValue(TransformDataBase.ColorKey.Main, new Color(0.1f, 0.2f, 0.3f, 0.4f));
            trans.SetColorValue("rgb", new Color(0.5f, 0.6f, 0.7f));

            var infos = trans.GetLaneColorInfos();

            Assert.Equal(2, infos.Length);
            Assert.Equal(new Color(0.1f, 0.2f, 0.3f, 0.4f), trans.GetLaneColor(infos[0]));
            Assert.Equal(new Color(0.5f, 0.6f, 0.7f, 1f), trans.GetLaneColor(infos[1]));
            Assert.False(infos[0].isStep);
        }

        [Fact]
        public void 既定の帯定義は同じ型で使い回す()
        {
            Assert.Same(
                Create<FakeColorTransform>().GetLaneColorInfos(),
                Create<FakeColorTransform>().GetLaneColorInfos());
        }

        [Fact]
        public void 色も表示フラグも持たない型は帯を持たない()
        {
            Assert.Empty(Create<FakePlainTransform>().GetLaneColorInfos());
        }

        [Fact]
        public void 色を持たず表示フラグを持つ型は表示のONOFFを出す()
        {
            var trans = Create<FakeVisibleTransform>();
            var infos = trans.GetLaneColorInfos();

            Assert.Single(infos);
            Assert.True(infos[0].isStep);

            trans.visible = true;
            Assert.Equal(Color.white, trans.GetLaneColor(infos[0]));
            trans.visible = false;
            Assert.Equal(Color.clear, trans.GetLaneColor(infos[0]));
        }

        [Theory]
        [InlineData(0.25f, 0.25f)]
        [InlineData(-0.5f, 0f)]
        [InlineData(1.5f, 1f)]
        public void 値の透明度は0から1に丸める(float value, float expectedAlpha)
        {
            var trans = Create<TransformDataMorph>();
            trans.morphValue = value;

            var infos = trans.GetLaneColorInfos();

            Assert.Single(infos);
            Assert.False(infos[0].isStep);
            Assert.Equal(new Color(1f, 1f, 1f, expectedAlpha), trans.GetLaneColor(infos[0]));
        }

        [Fact]
        public void ONOFFの帯は値で白と透明が切り替わる()
        {
            var trans = Create<TransformDataUndress>();
            var info = trans.GetLaneColorInfos()[0];

            Assert.True(info.isStep);
            trans.isVisibleValue.value = 1f;
            Assert.Equal(Color.white, trans.GetLaneColor(info));
            trans.isVisibleValue.value = 0f;
            Assert.Equal(Color.clear, trans.GetLaneColor(info));
        }

        public static IEnumerable<object[]> LaneCases()
        {
            // 型, 帯の段, 値の添字, ON/OFF か
            yield return new object[] { new TransformDataShapeKey(), 0, (int)TransformDataShapeKey.Index.Weight, false };
            yield return new object[] { new TransformDataModelShapeKey(), 0, (int)TransformDataModelShapeKey.Index.Weight, false };
            yield return new object[] { new TransformDataAnimation(), 0, (int)TransformDataAnimation.Index.Weight, false };
            yield return new object[] { new TransformDataGravity(), 0, (int)TransformDataGravity.Index.Enabled, true };
            yield return new object[] { new TransformDataFaceSetting(), 0, (int)TransformDataFaceSetting.Index.ForceOverride, true };
            yield return new object[] { new TransformDataGrounding(), 0, (int)TransformDataGrounding.Index.IsGroundingFootL, true };
            yield return new object[] { new TransformDataGrounding(), 1, (int)TransformDataGrounding.Index.IsGroundingFootR, true };
        }

        [Theory]
        [MemberData(nameof(LaneCases))]
        public void 各型の帯は対象の値を白で出す(TransformDataBase trans, int laneIndex, int valueIndex, bool isStep)
        {
            trans.Initialize("test");
            var info = trans.GetLaneColorInfos()[laneIndex];

            Assert.Equal(isStep, info.isStep);
            trans.values[valueIndex].value = 0f;
            Assert.Equal(0f, trans.GetLaneColor(info).a);
            trans.values[valueIndex].value = 1f;
            Assert.Equal(Color.white, trans.GetLaneColor(info));
        }

        [Fact]
        public void IK固定は2段の帯を持つ()
        {
            var trans = Create<TransformDataIKHold>();
            var infos = trans.GetLaneColorInfos();

            Assert.Equal(2, infos.Length);
            trans.values[(int)TransformDataIKHold.Index.IsHold].value = 1f;
            trans.values[(int)TransformDataIKHold.Index.IsAnime].value = 0f;
            Assert.Equal(Color.white, trans.GetLaneColor(infos[0]));
            Assert.Equal(Color.clear, trans.GetLaneColor(infos[1]));
        }
    }
}
