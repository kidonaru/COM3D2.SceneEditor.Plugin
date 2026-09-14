using System.IO;
using System.Xml.Serialization;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class ScenePresetBgModelTests
    {
        private static ScenePresetData RoundTrip(ScenePresetData data)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetData));
            string xml;
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, data);
                xml = writer.ToString();
            }
            using (var reader = new StringReader(xml))
            {
                return (ScenePresetData)serializer.Deserialize(reader);
            }
        }

        [Fact]
        public void BgModels_RoundTrip_PreservesValues()
        {
            var data = new ScenePresetData { bgModels = new ScenePresetBgModels() };
            data.bgModels.models.Add(new ScenePresetBgModel
            {
                sourceName = "Stage/Speaker_L",
                group = 2,
                visible = false,
                position = new Vector3(1f, 2f, 3f),
                rotation = new Vector3(0f, 90f, 0f),
                scale = new Vector3(2f, 2f, 2f),
            });

            var restored = RoundTrip(data);

            var model = Assert.Single(restored.bgModels.models);
            Assert.Equal("Stage/Speaker_L", model.sourceName);
            Assert.Equal(2, model.group);
            Assert.False(model.visible);
            Assert.Equal(new Vector3(1f, 2f, 3f), model.position);
            Assert.Equal(new Vector3(0f, 90f, 0f), model.rotation);
            Assert.Equal(new Vector3(2f, 2f, 2f), model.scale);
        }

        [Fact]
        public void BgModels_EmptyList_RoundTripsAsEmptyNotNull()
        {
            // 「制御対象なし」(要素あり・0 件) と「未記録」(null) を区別できること
            var data = new ScenePresetData { bgModels = new ScenePresetBgModels() };

            var restored = RoundTrip(data);

            Assert.NotNull(restored.bgModels);
            Assert.Empty(restored.bgModels.models);
        }

        [Fact]
        public void V33Preset_WithoutBgModels_ReadsAsNull()
        {
            var serializer = new XmlSerializer(typeof(ScenePresetData));
            using (var reader = new StringReader("<ScenePresetData version=\"33\" />"))
            {
                var restored = (ScenePresetData)serializer.Deserialize(reader);
                Assert.Null(restored.bgModels);
            }
        }
    }
}
