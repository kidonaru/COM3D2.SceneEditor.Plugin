using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// version 38 で PNG 配置キーの明るさを 0〜255 の byte から、Inspector と同じ倍率 (1 = 元の明るさ) へ移す移行を固定する。
    /// ずれると旧タイムラインの PNG が真っ白 (255 倍) で読まれる
    /// </summary>
    public class PngBrightnessMigrationTests
    {
        // 値の並びは保存形式。実装の Index ではなくベタ書きで固定する
        private const int BrightnessIndex = 19;
        private const int PngValueCountAtV37 = 33;

        private static TransformXml CreatePngKey(float brightness, TransformType type = TransformType.PngObject)
        {
            var values = new float[PngValueCountAtV37];
            values[BrightnessIndex] = brightness;
            return new TransformXml { name = "logo", type = type, values = values };
        }

        private static TimelineXml CreateTimeline(int version, TransformXml transform)
        {
            var layer = new TimelineLayerXml { className = "PngPlacementTimelineLayer" };
            layer.keyFrames.Add(new FrameXml
            {
                frameNo = 0,
                bones = new List<BoneXml> { new BoneXml { transform = transform } },
            });

            var timeline = new TimelineXml { version = version };
            timeline.layers.Add(layer);
            return timeline;
        }

        private static float BrightnessOf(TimelineXml timeline)
        {
            return timeline.layers[0].keyFrames[0].bones[0].transform.values[BrightnessIndex];
        }

        [Theory]
        [InlineData(255f, 1f)]
        [InlineData(0f, 0f)]
        [InlineData(51f, 0.2f)]
        public void Initialize_v37のPNGキーの明るさは倍率へ変換する(float stored, float expected)
        {
            var timeline = CreateTimeline(37, CreatePngKey(stored));

            timeline.Initialize();

            Assert.Equal(expected, BrightnessOf(timeline), 4);
        }

        [Fact]
        public void Initialize_v38以降は変換しない()
        {
            var timeline = CreateTimeline(38, CreatePngKey(1.5f));

            timeline.Initialize();

            Assert.Equal(1.5f, BrightnessOf(timeline));
        }

        [Fact]
        public void Initialize_PNG以外のキーは変換しない()
        {
            var timeline = CreateTimeline(37, CreatePngKey(255f, TransformType.Model));

            timeline.Initialize();

            Assert.Equal(255f, BrightnessOf(timeline));
        }

        [Fact]
        public void Initialize_明るさまで値の無いPNGキーは素通しする()
        {
            var timeline = CreateTimeline(37, new TransformXml
            {
                name = "logo",
                type = TransformType.PngObject,
                values = new float[BrightnessIndex],
            });

            timeline.Initialize();

            Assert.Equal(BrightnessIndex, timeline.layers[0].keyFrames[0].bones[0].transform.values.Length);
        }

        [Fact]
        public void Initialize_MTE形式のキーは回転の移行で明るさがずれた後に換算する()
        {
            // MTE の PngObject は 31 値 (回転がオイラー角 3 値) で、明るさは index 18、ScaleZ は index 19。
            // v35 の移行で回転が 4 値になり 1 つずつ後ろへずれるため、v38 の換算はその後でなければならない
            var values = new float[31];
            values[18] = 51f;
            values[19] = 1f;
            var timeline = CreateTimeline(31, new TransformXml
            {
                name = "logo",
                type = TransformType.PngObject,
                values = values,
            });

            timeline.Initialize();

            var migrated = timeline.layers[0].keyFrames[0].bones[0].transform.values;
            Assert.Equal(0.2f, migrated[BrightnessIndex], 4);
            Assert.Equal(1f, migrated[BrightnessIndex + 1]);
        }

        private static TemplateLayerXml CreateTemplateLayer(int templateVersion, float brightness)
        {
            var template = new TemplateXml { templateName = "t", version = templateVersion };
            template.frames.Add(new FrameXml
            {
                frameNo = 0,
                bones = new List<BoneXml> { new BoneXml { transform = CreatePngKey(brightness) } },
            });
            var category = new TemplateCategoryXml { categoryName = "Default" };
            category.templates.Add(template);
            var layer = new TemplateLayerXml { layerName = "PngPlacementTimelineLayer" };
            layer.categories.Add(category);
            return layer;
        }

        private static TemplateXml FirstTemplate(TemplateLayerXml layer)
        {
            return layer.categories[0].templates[0];
        }

        [Fact]
        public void テンプレート_version無しのPNGキーは読込時に倍率へ換算する()
        {
            // version 属性の無いテンプレート (MTE・v37 以前の SE) は 0 として読まれる
            var layer = CreateTemplateLayer(0, 255f);

            layer.OnLoad();

            var template = FirstTemplate(layer);
            Assert.Equal(1f, template.frames[0].bones[0].transform.values[BrightnessIndex], 4);
            // 保存し直しても二重に換算しないよう、換算済みとして現行バージョンへ揃える
            Assert.Equal(TimelineData.CurrentVersion, template.version);
        }

        [Fact]
        public void テンプレート_v38以降のPNGキーは換算しない()
        {
            var layer = CreateTemplateLayer(38, 1.5f);

            layer.OnLoad();

            Assert.Equal(1.5f, FirstTemplate(layer).frames[0].bones[0].transform.values[BrightnessIndex]);
        }

        [Fact]
        public void リセットしたキーの明るさは1()
        {
            var trans = new TransformDataPngObject();
            trans.Initialize("logo");
            trans.Reset();

            Assert.Equal(1f, trans.brightness);
        }

        [Fact]
        public void キーの明るさは1を超えても保たれる()
        {
            var trans = new TransformDataPngObject();
            trans.Initialize("logo");
            trans.brightness = 1.75f;

            var restored = new TransformDataPngObject();
            restored.Initialize("logo");
            restored.FromXml(trans.ToXml());

            Assert.Equal(1.75f, restored.brightness);
        }

        [Fact]
        public void 現行バージョンは38()
        {
            Assert.Equal(38, TimelineData.CurrentVersion);
        }
    }
}
