using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineLayerRestorePolicyTests
    {
        // 設計メモ「決定事項」の表。レイヤー型を増やしたらここにも足すこと
        private static readonly HashSet<string> EXPECTED_EXCLUDED = new HashSet<string>
        {
            // 何もしない (再生トリガや着替えをやり直してしまう)
            "MotionTimelineLayer",
            "VoiceTimelineLayer",
            "SeTimelineLayer",
            "DressTimelineLayer",
            // 実体ごと破棄する
            "StageLightTimelineLayer",
            "StageLaserTimelineLayer",
            "PsylliumTimelineLayer",
            "ModelTimelineLayer",
            "PngPlacementTimelineLayer",
            "TextTimelineLayer",
            "SubCameraTimelineLayer",
            // 実体は残して既定値へ戻す
            "ModelBoneTimelineLayer",
            "ModelShapeKeyTimelineLayer",
            "ModelMaterialTimelineLayer",
        };

        [Fact]
        public void 復元対象外のレイヤーだけがfalseを返す()
        {
            var types = TimelineLayerTestUtils.GetConcreteLayerTypes();
            Assert.NotEmpty(types);

            foreach (var type in types)
            {
                var expected = !EXPECTED_EXCLUDED.Contains(type.Name);
                var actual = TimelineLayerRestorePolicy.CanRestoreOnRemove(type);
                Assert.True(expected == actual,
                    type.Name + " の復元可否が期待と違う: expected=" + expected + " actual=" + actual);
            }
        }

        [Fact]
        public void 期待表のレイヤー型がすべて実在する()
        {
            var names = TimelineLayerTestUtils.GetConcreteLayerTypes()
                .Select(t => t.Name).ToList();
            foreach (var name in EXPECTED_EXCLUDED)
            {
                Assert.Contains(name, names);
            }
        }

        [Fact]
        public void 型がnullなら復元しない()
        {
            Assert.False(TimelineLayerRestorePolicy.CanRestoreOnRemove(null));
        }

        [Fact]
        public void レイヤーでない型は復元しない()
        {
            Assert.False(TimelineLayerRestorePolicy.CanRestoreOnRemove(typeof(string)));
        }
    }
}
