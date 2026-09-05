using System.IO;
using System.Xml.Serialization;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class ScenePresetEffectsTests
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
        public void Effects_RoundTrip_PreservesValues()
        {
            var data = new ScenePresetData { effects = new ScenePresetEffects() };
            data.effects.texts.Add(new ScenePresetText
            {
                text = "こんにちは",
                font = "Yu Gothic Bold",
                fontSize = 40,
                lineSpacing = 30f,
                alignment = 4,
                position = new Vector3(1f, 2f, 3f),
                rotation = new Vector3(0f, 0f, 45f),
                scale = new Vector3(2f, 2f, 2f),
                color = new Color(1f, 0.5f, 0.25f, 0.75f),
                sizeDeltaX = 800f,
                sizeDeltaY = 600f,
            });
            data.effects.subCameras.Add(new ScenePresetSubCamera
            {
                visible = false,
                fieldOfView = 50f,
                position = new Vector3(0f, 1f, -2f),
                rotation = new Vector3(10f, 20f, 30f),
                viewportX = 0.1f,
                viewportY = 0.2f,
                viewportWidth = 0.3f,
                viewportHeight = 0.4f,
                maidSlotNo = 2,
                maidPointType = 3,
                followRotation = true,
            });

            var restored = RoundTrip(data);

            Assert.NotNull(restored.effects);
            var text = Assert.Single(restored.effects.texts);
            Assert.Equal("こんにちは", text.text);
            Assert.Equal("Yu Gothic Bold", text.font);
            Assert.Equal(40, text.fontSize);
            Assert.Equal(4, text.alignment);
            Assert.Equal(new Vector3(1f, 2f, 3f), text.position);
            Assert.Equal(new Color(1f, 0.5f, 0.25f, 0.75f), text.color);
            Assert.Equal(800f, text.sizeDeltaX);

            var subCamera = Assert.Single(restored.effects.subCameras);
            Assert.False(subCamera.visible);
            Assert.Equal(50f, subCamera.fieldOfView);
            Assert.Equal(0.3f, subCamera.viewportWidth);
            Assert.Equal(2, subCamera.maidSlotNo);
            Assert.Equal(3, subCamera.maidPointType);
            Assert.True(subCamera.followRotation);
        }

        [Fact]
        public void V29Preset_WithPostEffectElements_IgnoresThemSafely()
        {
            // ポストエフェクト部を撤去する前の v29 が出力していた形。
            // XmlSerializer は未知要素・未知属性を黙って読み飛ばすことを確認する
            var xml =
                "<ScenePresetData version=\"29\" savedEffects=\"true\">" +
                "<effects paraffinEnabled=\"true\" distanceFogEnabled=\"false\" rimlightEnabled=\"false\">" +
                "<text text=\"hello\" fontSize=\"20\" />" +
                "<paraffin enabled=\"true\" useAdd=\"0.5\" />" +
                "<distanceFog fogEnd=\"25\" />" +
                "<rimlight isWorldSpace=\"true\" />" +
                "</effects>" +
                "</ScenePresetData>";

            var serializer = new XmlSerializer(typeof(ScenePresetData));
            using (var reader = new StringReader(xml))
            {
                var restored = (ScenePresetData)serializer.Deserialize(reader);
                Assert.NotNull(restored.effects);
                var text = Assert.Single(restored.effects.texts);
                Assert.Equal("hello", text.text);
            }
        }

        [Fact]
        public void OldPreset_WithoutEffects_ReadsEffectsAsNull()
        {
            var serializer = new XmlSerializer(typeof(ScenePresetData));
            using (var reader = new StringReader("<ScenePresetData version=\"28\" />"))
            {
                var restored = (ScenePresetData)serializer.Deserialize(reader);
                Assert.Null(restored.effects);
                // saved* の既定 true 規約 (v16) と同じく、effects null 側で「触らない」を担保する
                Assert.True(restored.savedEffects);
            }
        }

        [Fact]
        public void SoundAndVideo_RoundTrip_PreservesValues()
        {
            var data = new ScenePresetData { effects = new ScenePresetEffects() };
            data.effects.sound = new ScenePresetSound
            {
                gameBgmFile = "BGM020.ogg",
                bgmPath = @"C:\music\dance.ogg",
                bpm = 128f,
                isShowBPMLine = true,
                bpmLineOffsetFrame = 1.5f,
            };
            data.effects.videos.Add(new ScenePresetVideo
            {
                enabled = false,
                displayType = 3,
                path = @"C:\movie\a.mp4",
                position = new Vector3(1f, 2f, 3f),
                startTime = 0.5f,
                frontmostPosition = new Vector2(-0.7f, 0.7f),
                frontmostAlpha = 0.5f,
            });
            data.effects.videos.Add(new ScenePresetVideo
            {
                displayType = 2,
                path = @"C:\movie\b.mp4",
                backmostAlpha = 0.25f,
            });

            var restored = RoundTrip(data);

            Assert.Equal(32, ScenePresetData.CurrentVersion);
            Assert.NotNull(restored.effects.sound);
            Assert.Equal("BGM020.ogg", restored.effects.sound.gameBgmFile);
            Assert.Equal(@"C:\music\dance.ogg", restored.effects.sound.bgmPath);
            Assert.Equal(128f, restored.effects.sound.bpm);
            Assert.True(restored.effects.sound.isShowBPMLine);
            Assert.Equal(1.5f, restored.effects.sound.bpmLineOffsetFrame);

            Assert.Equal(2, restored.effects.videos.Count);
            Assert.False(restored.effects.videos[0].enabled);
            Assert.Equal(3, restored.effects.videos[0].displayType);
            Assert.Equal(@"C:\movie\a.mp4", restored.effects.videos[0].path);
            Assert.Equal(new Vector3(1f, 2f, 3f), restored.effects.videos[0].position);
            Assert.Equal(0.5f, restored.effects.videos[0].startTime);
            Assert.Equal(new Vector2(-0.7f, 0.7f), restored.effects.videos[0].frontmostPosition);
            Assert.Equal(0.5f, restored.effects.videos[0].frontmostAlpha);
            Assert.Equal(2, restored.effects.videos[1].displayType);
            Assert.Equal(@"C:\movie\b.mp4", restored.effects.videos[1].path);
            Assert.Equal(0.25f, restored.effects.videos[1].backmostAlpha);
        }

        [Fact]
        public void V31Preset_SingleVideoElement_ReadsAsOneItemList()
        {
            var serializer = new XmlSerializer(typeof(ScenePresetData));
            var xml = "<ScenePresetData version=\"31\" savedEffects=\"true\"><effects>"
                + "<video enabled=\"true\" displayType=\"2\"><path>C:\\movie\\old.mp4</path><backmostAlpha>0.4</backmostAlpha></video>"
                + "</effects></ScenePresetData>";
            using (var reader = new StringReader(xml))
            {
                var restored = (ScenePresetData)serializer.Deserialize(reader);
                Assert.Single(restored.effects.videos);
                Assert.Equal(2, restored.effects.videos[0].displayType);
                Assert.Equal(@"C:\movie\old.mp4", restored.effects.videos[0].path);
                Assert.Equal(0.4f, restored.effects.videos[0].backmostAlpha);
            }
        }

        [Fact]
        public void V29Preset_WithoutSoundAndVideo_ReadsSoundAsNullAndVideosAsEmpty()
        {
            var serializer = new XmlSerializer(typeof(ScenePresetData));
            var xml = "<ScenePresetData version=\"29\" savedEffects=\"true\"><effects /></ScenePresetData>";
            using (var reader = new StringReader(xml))
            {
                var restored = (ScenePresetData)serializer.Deserialize(reader);
                Assert.NotNull(restored.effects);
                Assert.Null(restored.effects.sound);
                Assert.Empty(restored.effects.videos);
            }
        }
    }
}
