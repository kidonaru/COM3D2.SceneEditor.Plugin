using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class ColorValueInfoTests
    {
        /// <summary>values[0..3] に RGBA、values[4..6] に RGB、values[7] に非色の値を持つ型</summary>
        private class FakeColorTransform : TransformDataBase
        {
            public override TransformType type => TransformType.None;
            public override int valueCount => 8;
            public override bool hasTangent => true;
            public override ValueData[] tangentValues => valuesWithoutColors;

            private static readonly Dictionary<string, ColorValueInfo> ColorValueInfoMap =
                new Dictionary<string, ColorValueInfo>
                {
                    { ColorKey.Main, ColorValueInfo.Rgba("色", 0, new Color(0.1f, 0.2f, 0.3f, 0.4f)) },
                    { "rgb", ColorValueInfo.Rgb("RGB", 4, Color.black) },
                };

            public override Dictionary<string, ColorValueInfo> GetColorValueInfoMap() => ColorValueInfoMap;
        }

        private static FakeColorTransform Create()
        {
            var trans = new FakeColorTransform();
            trans.Initialize("fake");
            return trans;
        }

        [Fact]
        public void Rgbaは連続4成分でアルファ有り()
        {
            var info = ColorValueInfo.Rgba("色", 5, Color.white);
            Assert.Equal(5, info.indexR);
            Assert.Equal(6, info.indexG);
            Assert.Equal(7, info.indexB);
            Assert.Equal(8, info.indexA);
            Assert.True(info.hasAlpha);
        }

        [Fact]
        public void Rgbはアルファ無し()
        {
            var info = ColorValueInfo.Rgb("色", 2, Color.white);
            Assert.Equal(4, info.indexB);
            Assert.Equal(-1, info.indexA);
            Assert.False(info.hasAlpha);
        }

        [Fact]
        public void SetColorValueで書いた色をGetColorValueで読める()
        {
            var trans = Create();
            trans.SetColorValue(ColorKeyMain, new Color(0.5f, 0.6f, 0.7f, 0.8f));
            Assert.Equal(new Color(0.5f, 0.6f, 0.7f, 0.8f), trans.GetColorValue(ColorKeyMain));
            Assert.Equal(0.8f, trans.values[3].value);
        }

        [Fact]
        public void RGBのみの色はアルファ1で読めアルファは書かれない()
        {
            var trans = Create();
            trans.values[7].value = 9f;
            trans.SetColorValue("rgb", new Color(0.1f, 0.2f, 0.3f, 0.5f));
            Assert.Equal(new Color(0.1f, 0.2f, 0.3f, 1f), trans.GetColorValue("rgb"));
            Assert.Equal(9f, trans.values[7].value);
        }

        [Fact]
        public void 糖衣のcolorはMainキーと同じ値()
        {
            var trans = Create();
            trans.color = new Color(0.2f, 0.4f, 0.6f, 0.8f);
            Assert.Equal(trans.GetColorValue(ColorKeyMain), trans.color);
        }

        [Fact]
        public void 既定値と存在判定と名前()
        {
            var trans = Create();
            Assert.Equal(new Color(0.1f, 0.2f, 0.3f, 0.4f), trans.GetDefaultColorValue(ColorKeyMain));
            Assert.True(trans.HasColorValue("rgb"));
            Assert.False(trans.HasColorValue("none"));
            Assert.Equal("RGB", trans.GetColorValueName("rgb"));
        }

        [Fact]
        public void Resetで色が既定値に戻る()
        {
            var trans = Create();
            trans.SetColorValue(ColorKeyMain, Color.red);
            trans.SetColorValue("rgb", Color.red);
            trans.Reset();
            Assert.Equal(new Color(0.1f, 0.2f, 0.3f, 0.4f), trans.GetColorValue(ColorKeyMain));
            Assert.Equal(new Color(0f, 0f, 0f, 1f), trans.GetColorValue("rgb"));
        }

        [Fact]
        public void valuesWithoutColorsは色成分を含まない()
        {
            var trans = Create();
            var tangents = trans.tangentValues;
            Assert.Single(tangents);
            Assert.Same(trans.values[7], tangents[0]);
        }

        /// <summary>アセンブリ内の具象 TransformData を全部生成する</summary>
        private static IEnumerable<TransformDataBase> AllTransforms()
        {
            var baseType = typeof(TransformDataBase);
            foreach (var type in baseType.Assembly.GetTypes())
            {
                if (!type.IsSubclassOf(baseType) || type.IsAbstract
                    || type.GetConstructor(System.Type.EmptyTypes) == null)
                {
                    continue;
                }
                var trans = (TransformDataBase)System.Activator.CreateInstance(type);
                trans.Initialize("test");
                yield return trans;
            }
        }

        [Fact]
        public void 旧colorValuesと新マップは同じ値を指す()
        {
            foreach (var trans in AllTransforms())
            {
                if (trans.colorValues.Length > 0)
                {
                    var info = trans.GetColorValueInfo("color");
                    Assert.NotNull(info);
                    Assert.Same(trans.colorValues[0], trans.values[info.indexR]);
                    Assert.Same(trans.colorValues[1], trans.values[info.indexG]);
                    Assert.Same(trans.colorValues[2], trans.values[info.indexB]);
                    Assert.Equal(trans.colorValues.Length == 4, info.hasAlpha);
                    if (info.hasAlpha)
                    {
                        Assert.Same(trans.colorValues[3], trans.values[info.indexA]);
                    }
                    Assert.Equal(trans.initialColor, info.defaultValue);
                }
                if (trans.subColorValues.Length > 0)
                {
                    var info = trans.GetColorValueInfo("subColor");
                    Assert.NotNull(info);
                    Assert.Same(trans.subColorValues[0], trans.values[info.indexR]);
                    Assert.Same(trans.subColorValues[3], trans.values[info.indexA]);
                    Assert.Equal(trans.initialSubColor, info.defaultValue);
                }
            }
        }

        [Fact]
        public void ModelMaterialの追加色は型付きアクセサと一致する()
        {
            var trans = new TransformDataModelMaterial();
            trans.Initialize("mm");
            Assert.Same(trans.ShadowColorValues[0], trans.values[trans.GetColorValueInfo("ShadowColor").indexR]);
            Assert.Same(trans.RimColorValues[0], trans.values[trans.GetColorValueInfo("RimColor").indexR]);
            Assert.Same(trans.OutlineColorValues[0], trans.values[trans.GetColorValueInfo("OutlineColor").indexR]);
            Assert.Same(trans.EmissionColorValues[0], trans.values[trans.GetColorValueInfo("EmissionColor").indexR]);
            Assert.Same(trans.MatcapColorValues[0], trans.values[trans.GetColorValueInfo("MatcapColor").indexR]);
            Assert.Same(trans.MatcapMaskColorValues[0], trans.values[trans.GetColorValueInfo("MatcapMaskColor").indexR]);
            Assert.Same(trans.RimLightColorValues[0], trans.values[trans.GetColorValueInfo("RimLightColor").indexR]);
        }

        [Fact]
        public void PsylliumBarとBloomの追加色は型付きアクセサと一致する()
        {
            var bar = new TransformDataPsylliumBar();
            bar.Initialize("bar");
            Assert.Same(bar.color1aValues[0], bar.values[bar.GetColorValueInfo("color1a").indexR]);
            Assert.Same(bar.color2cValues[3], bar.values[bar.GetColorValueInfo("color2c").indexA]);

            var bloom = new TransformDataBloom();
            bloom.Initialize("bloom");
            bloom.flareColorB = new Color(0.1f, 0.2f, 0.3f, 0.4f);
            Assert.Equal(bloom.flareColorB, bloom.GetColorValue("flareColorB"));
            bloom.flareColorD = new Color(0.5f, 0.6f, 0.7f, 0.8f);
            Assert.Equal(bloom.flareColorD, bloom.GetColorValue("flareColorD"));
        }

        private const string ColorKeyMain = "color";
    }
}
