using System;
using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineLayerResetOnRemoveTests
    {
        // 設計メモ「決定事項」の表。実体を持つレイヤーと従属レイヤーだけが実装する
        private static readonly HashSet<string> EXPECTED_OVERRIDES = new HashSet<string>
        {
            "StageLightTimelineLayer",
            "StageLaserTimelineLayer",
            "PsylliumTimelineLayer",
            "ModelTimelineLayer",
            "LightTimelineLayer",
            "PngPlacementTimelineLayer",
            "TextTimelineLayer",
            "SubCameraTimelineLayer",
            "ModelBoneTimelineLayer",
            "ModelShapeKeyTimelineLayer",
            "ModelMaterialTimelineLayer",
        };

        private static bool HasOwnResetOnRemove(Type type)
        {
            var method = type.GetMethod("ResetOnRemove", Type.EmptyTypes);
            return method != null && method.DeclaringType == type;
        }

        [Fact]
        public void 後始末を実装しているレイヤーが期待どおり()
        {
            var types = TimelineLayerTestUtils.GetConcreteLayerTypes();
            Assert.NotEmpty(types);

            foreach (var type in types)
            {
                var expected = EXPECTED_OVERRIDES.Contains(type.Name);
                var actual = HasOwnResetOnRemove(type);
                Assert.True(expected == actual,
                    type.Name + " の ResetOnRemove 実装が期待と違う: expected=" + expected + " actual=" + actual);
            }
        }

        [Fact]
        public void 期待表のレイヤー型がすべて実在する()
        {
            var names = TimelineLayerTestUtils.GetConcreteLayerTypes().Select(t => t.Name).ToList();
            foreach (var name in EXPECTED_OVERRIDES)
            {
                Assert.Contains(name, names);
            }
        }

        [Fact]
        public void ライトだけは後始末と断面復元の両方を行う()
        {
            var lightType = TimelineLayerTestUtils.GetConcreteLayerTypes()
                .First(t => t.Name == "LightTimelineLayer");

            Assert.True(HasOwnResetOnRemove(lightType));
            Assert.True(TimelineLayerRestorePolicy.CanRestoreOnRemove(lightType));
        }
    }
}
