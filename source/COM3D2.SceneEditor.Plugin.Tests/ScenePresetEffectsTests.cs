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
            data.effects.video = new ScenePresetVideo
            {
                enabled = false,
                displayType = 3,
                path = @"C:\movie\a.mp4",
                position = new Vector3(1f, 2f, 3f),
                rotation = new Vector3(4f, 5f, 6f),
                scale = 2f,
                startTime = 0.5f,
                volume = 0.25f,
                alpha = 0.75f,
                guiPosition = new Vector2(0.1f, 0.2f),
                guiScale = 0.9f,
                guiAlpha = 0.8f,
                backmostPosition = new Vector2(0.3f, 0.4f),
                backmostScale = 1.1f,
                backmostAlpha = 0.6f,
                frontmostPosition = new Vector2(-0.7f, 0.7f),
                frontmostScale = 0.35f,
                frontmostAlpha = 0.5f,
            };

            var restored = RoundTrip(data);

            Assert.Equal(30, ScenePresetData.CurrentVersion);
            Assert.NotNull(restored.effects.sound);
            Assert.Equal("BGM020.ogg", restored.effects.sound.gameBgmFile);
            Assert.Equal(@"C:\music\dance.ogg", restored.effects.sound.bgmPath);
            Assert.Equal(128f, restored.effects.sound.bpm);
            Assert.True(restored.effects.sound.isShowBPMLine);
            Assert.Equal(1.5f, restored.effects.sound.bpmLineOffsetFrame);

            Assert.NotNull(restored.effects.video);
            Assert.False(restored.effects.video.enabled);
            Assert.Equal(3, restored.effects.video.displayType);
            Assert.Equal(@"C:\movie\a.mp4", restored.effects.video.path);
            Assert.Equal(new Vector3(1f, 2f, 3f), restored.effects.video.position);
            Assert.Equal(0.5f, restored.effects.video.startTime);
            Assert.Equal(new Vector2(-0.7f, 0.7f), restored.effects.video.frontmostPosition);
            Assert.Equal(0.5f, restored.effects.video.frontmostAlpha);
        }

        [Fact]
        public void V29Preset_WithoutSoundAndVideo_ReadsThemAsNull()
        {
            var serializer = new XmlSerializer(typeof(ScenePresetData));
            var xml = "<ScenePresetData version=\"29\" savedEffects=\"true\"><effects /></ScenePresetData>";
            using (var reader = new StringReader(xml))
            {
                var restored = (ScenePresetData)serializer.Deserialize(reader);
                Assert.NotNull(restored.effects);
                Assert.Null(restored.effects.sound);
                Assert.Null(restored.effects.video);
            }
        }
    }
}
