using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineTemplateXmlTests
    {
        private static TemplateLayerXml RoundTrip(TemplateLayerXml src)
        {
            var serializer = new XmlSerializer(typeof(TemplateLayerXml));
            using (var ms = new MemoryStream())
            {
                serializer.Serialize(ms, src);
                ms.Position = 0;
                return (TemplateLayerXml)serializer.Deserialize(ms);
            }
        }

        [Fact]
        public void Xmlラウンドトリップでレイヤー名とカテゴリ構造が保持される()
        {
            var src = new TemplateLayerXml { layerName = "MotionTimelineLayer" };
            var category = new TemplateCategoryXml { categoryName = "Default" };
            category.templates.Add(new TemplateXml
            {
                templateName = "手を振る",
                frames = { new FrameXml { frameNo = 5 } },
            });
            src.categories.Add(category);

            var dst = RoundTrip(src);

            Assert.Equal("MotionTimelineLayer", dst.layerName);
            var dstCategory = Assert.Single(dst.categories);
            Assert.Equal("Default", dstCategory.categoryName);
            var dstTemplate = Assert.Single(dstCategory.templates);
            Assert.Equal("手を振る", dstTemplate.templateName);
            Assert.Equal(5, Assert.Single(dstTemplate.frames).frameNo);
        }

        [Fact]
        public void OnLoadでDefaultカテゴリが先頭に補われる()
        {
            var layer = new TemplateLayerXml { layerName = "MotionTimelineLayer" };
            layer.categories.Add(new TemplateCategoryXml { categoryName = "その他" });

            layer.OnLoad();

            Assert.Equal("Default", layer.categories[0].categoryName);
            Assert.Equal(new[] { "Default", "その他" }, layer.categoryNames);
        }

        // AddCategory / MoveCategory / RemoveCategory は内部で MTEUtils.Log を呼び、
        // テストランナーのバックグラウンドスレッドから Unity の ECall に到達して
        // SecurityException になるため、ここではログを踏まない照会系 API のみ検証する。
        // 操作系の挙動は実機確認 (計画 Task 5) でカバーする
        [Fact]
        public void カテゴリの照会と移動可否判定が正しく動く()
        {
            var layer = new TemplateLayerXml { layerName = "MotionTimelineLayer" };
            layer.categories.Add(new TemplateCategoryXml { categoryName = "A" });
            layer.categories.Add(new TemplateCategoryXml { categoryName = "B" });
            layer.OnLoad();

            // OnLoad で Default が先頭へ補われ Default, A, B の並びになる
            Assert.True(layer.HasCategory("A"));
            Assert.False(layer.HasCategory("C"));
            Assert.Equal("B", layer.GetCategory("B").categoryName);
            Assert.Null(layer.GetCategory("C"));

            Assert.True(layer.CanMoveCategory("A", 1));
            Assert.True(layer.CanMoveCategory("A", -1));
            Assert.False(layer.CanMoveCategory("B", 1));
            Assert.False(layer.CanMoveCategory("C", 1));
        }

        [Fact]
        public void 先頭カテゴリはそれ以上上へ移動できない()
        {
            var layer = new TemplateLayerXml { layerName = "MotionTimelineLayer" };
            layer.OnLoad();

            Assert.False(layer.CanMoveCategory("Default", -1));
        }

        [Fact]
        public void ダーティ判定はカテゴリの変更にも反応する()
        {
            var layer = new TemplateLayerXml { layerName = "MotionTimelineLayer" };
            layer.OnLoad();
            layer.ClearDirtyRecursive();
            Assert.False(layer.IsDirtyRecursive());

            layer.categories[0].dirty = true;
            Assert.True(layer.IsDirtyRecursive());

            layer.ClearDirtyRecursive();
            Assert.False(layer.IsDirtyRecursive());
        }
    }
}
