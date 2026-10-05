using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// ライブ演出の状態 (Undo・シーンプリセット) のステージライト濃度を固定する。
    /// 濃度の無い旧プリセットは色のアルファが濃度だったので、適用時に換算しないと光の柱が既定の濃さに変わる
    /// </summary>
    public class StageLightIntensityCompatTests
    {
        [Fact]
        public void 未記録の濃度は色のアルファから換算し色のアルファは1にする()
        {
            Color color;
            float intensity;
            StageLightIntensityCompat.Resolve(
                new Color(0.2f, 0.4f, 0.6f, 0.35f), StageLightIntensityCompat.Unrecorded,
                out color, out intensity);

            Assert.Equal(0.35f, intensity);
            Assert.Equal(new Color(0.2f, 0.4f, 0.6f, 1f), color);
        }

        [Fact]
        public void 記録済みの濃度はそのまま使い色も触らない()
        {
            Color color;
            float intensity;
            StageLightIntensityCompat.Resolve(new Color(0.2f, 0.4f, 0.6f, 0.35f), 1.5f, out color, out intensity);

            Assert.Equal(1.5f, intensity);
            Assert.Equal(new Color(0.2f, 0.4f, 0.6f, 0.35f), color);
        }

        [Fact]
        public void 濃度0は記録済みとして扱う()
        {
            Color color;
            float intensity;
            StageLightIntensityCompat.Resolve(Color.white, 0f, out color, out intensity);

            Assert.Equal(0f, intensity);
        }

        [Fact]
        public void 新しく作った状態の濃度は未記録()
        {
            Assert.Equal(StageLightIntensityCompat.Unrecorded, new LiveEffectStageLightState().intensity);
            var controller = new LiveEffectStageLightControllerState();
            Assert.Equal(StageLightIntensityCompat.Unrecorded, controller.intensityMin);
            Assert.Equal(StageLightIntensityCompat.Unrecorded, controller.intensityMax);
        }

        private static ScenePresetData RoundTrip(ScenePresetData data)
        {
            var serializer = new XmlSerializer(typeof(ScenePresetData));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, data);
                using (var reader = new StringReader(writer.ToString()))
                {
                    return (ScenePresetData)serializer.Deserialize(reader);
                }
            }
        }

        [Fact]
        public void プリセットは濃度を往復で保つ()
        {
            var data = new ScenePresetData { effects = new ScenePresetEffects() };
            data.effects.liveEffect = new LiveEffectState();
            var controller = new LiveEffectStageLightControllerState { intensityMin = 0.1f, intensityMax = 1.9f };
            controller.lights.Add(new LiveEffectStageLightState { intensity = 1.25f });
            data.effects.liveEffect.stageLightControllers.Add(controller);

            var restored = Assert.Single(RoundTrip(data).effects.liveEffect.stageLightControllers);

            Assert.Equal(0.1f, restored.intensityMin);
            Assert.Equal(1.9f, restored.intensityMax);
            Assert.Equal(1.25f, Assert.Single(restored.lights).intensity);
        }

        [Fact]
        public void 濃度要素の無い旧プリセットは未記録として読む()
        {
            // v39 以前のプリセットには intensity 要素が無い
            const string xml =
                "<LiveEffectStageLightState><color><r>1</r><g>1</g><b>1</b><a>0.4</a></color></LiveEffectStageLightState>";
            var serializer = new XmlSerializer(typeof(LiveEffectStageLightState));
            using (var reader = new StringReader(xml))
            {
                var state = (LiveEffectStageLightState)serializer.Deserialize(reader);

                Assert.Equal(StageLightIntensityCompat.Unrecorded, state.intensity);
                Assert.Equal(0.4f, state.color.a);
            }
        }
    }
}
