using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class LiveEffectStateTests
    {
        private static LiveEffectState BuildSample()
        {
            var state = new LiveEffectState();

            var lightController = new LiveEffectStageLightControllerState
            {
                autoColor = true,
                colorMin = Color.red,
                colorMax = Color.blue,
                lightInfo = new StageLightInfo { spotAngle = 42f },
            };
            lightController.lights.Add(new LiveEffectStageLightState
            {
                visible = true,
                position = new Vector3(1f, 2f, 3f),
                color = Color.green,
            });
            state.stageLightControllers.Add(lightController);

            var laserController = new LiveEffectStageLaserControllerState
            {
                position = new Vector3(0f, 1f, 0f),
                laserInfo = new StageLaserInfo { laserRange = 7f },
            };
            laserController.lasers.Add(new LiveEffectStageLaserState { intensity = 0.5f });
            state.stageLaserControllers.Add(laserController);

            var psylliumController = new LiveEffectPsylliumControllerState
            {
                visible = true,
                position = new Vector3(0f, 0f, 11f),
            };
            psylliumController.barConfig.baseScale = 2f;
            psylliumController.areas.Add(new PsylliumAreaConfig());
            psylliumController.patterns.Add(new LiveEffectPsylliumPatternState());
            var placement = new PsylliumPlacement { areaIndex = 0, name = "arena" };
            placement.points.Add(new PsylliumPlacementPoint { x = 1f, y = 0f, z = 2f, yaw = 90f });
            psylliumController.placements.Add(placement);
            state.psylliumControllers.Add(psylliumController);

            return state;
        }

        [Fact]
        public void 同じ内容のStateは等価と判定される()
        {
            Assert.True(PresetDtoUtils.AreEqual(BuildSample(), BuildSample()));
        }

        [Fact]
        public void 値が違うStateは非等価と判定される()
        {
            var a = BuildSample();
            var b = BuildSample();
            b.stageLightControllers[0].lights[0].position = new Vector3(9f, 9f, 9f);

            Assert.False(PresetDtoUtils.AreEqual(a, b));
        }

        [Fact]
        public void サイリウムのバー設定の違いも検出できる()
        {
            var a = BuildSample();
            var b = BuildSample();
            b.psylliumControllers[0].barConfig.baseScale = 3f;

            Assert.False(PresetDtoUtils.AreEqual(a, b));
        }

        [Fact]
        public void サイリウム配置の違いも検出できる()
        {
            var a = BuildSample();
            var b = BuildSample();
            b.psylliumControllers[0].placements[0].points[0].yaw = 45f;

            Assert.False(PresetDtoUtils.AreEqual(a, b));
        }
    }
}
