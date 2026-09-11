using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineLayerCategoryTests
    {
        // 設計書 §3.1 の表。レイヤー型を増やしたらここにも足すこと
        private static readonly Dictionary<string, TimelineLayerCategory> EXPECTED =
            new Dictionary<string, TimelineLayerCategory>
        {
            { "MotionTimelineLayer", TimelineLayerCategory.Maid },
            { "AnimationTimelineLayer", TimelineLayerCategory.Maid },
            { "MorphTimelineLayer", TimelineLayerCategory.Maid },
            { "MoveTimelineLayer", TimelineLayerCategory.Maid },
            { "EyesTimelineLayer", TimelineLayerCategory.Maid },
            { "ShapeKeyTimelineLayer", TimelineLayerCategory.Maid },
            { "VoiceTimelineLayer", TimelineLayerCategory.Maid },
            { "DressTimelineLayer", TimelineLayerCategory.Maid },
            { "UndressTimelineLayer", TimelineLayerCategory.Maid },
            { "MaidMaterialTimelineLayer", TimelineLayerCategory.Maid },
            { "CameraTimelineLayer", TimelineLayerCategory.Camera },
            { "SubCameraTimelineLayer", TimelineLayerCategory.Camera },
            { "ModelTimelineLayer", TimelineLayerCategory.Model },
            { "ModelBoneTimelineLayer", TimelineLayerCategory.Model },
            { "ModelShapeKeyTimelineLayer", TimelineLayerCategory.Model },
            { "ModelMaterialTimelineLayer", TimelineLayerCategory.Model },
            { "BGTimelineLayer", TimelineLayerCategory.Background },
            { "BGColorTimelineLayer", TimelineLayerCategory.Background },
            { "BGModelTimelineLayer", TimelineLayerCategory.Background },
            { "BGModelMaterialTimelineLayer", TimelineLayerCategory.Background },
            { "PngPlacementTimelineLayer", TimelineLayerCategory.Background },
            { "LightTimelineLayer", TimelineLayerCategory.Effect },
            { "StageLightTimelineLayer", TimelineLayerCategory.Effect },
            { "StageLaserTimelineLayer", TimelineLayerCategory.Effect },
            { "PsylliumTimelineLayer", TimelineLayerCategory.Effect },
            { "SeTimelineLayer", TimelineLayerCategory.Other },
            { "PostEffectTimelineLayer", TimelineLayerCategory.Other },
            { "TextTimelineLayer", TimelineLayerCategory.Other },
        };

        // プラグイン DLL 内の具象レイヤー型。ゲーム依存の型読み込みに失敗しても
        // 読めた分だけで検証できるよう ReflectionTypeLoadException は握る
        private static List<Type> GetConcreteLayerTypes()
        {
            var assembly = typeof(ITimelineLayer).Assembly;
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToArray();
            }
            return types
                .Where(t => !t.IsAbstract && typeof(ITimelineLayer).IsAssignableFrom(t))
                .ToList();
        }

        [Fact]
        public void 全レイヤー型が期待どおりのカテゴリを持つ()
        {
            var types = GetConcreteLayerTypes();
            Assert.NotEmpty(types);

            foreach (var type in types)
            {
                var attr = type.GetCustomAttribute<TimelineLayerDescAttribute>();
                Assert.True(attr != null, type.Name + " に TimelineLayerDesc が無い");

                TimelineLayerCategory expected;
                Assert.True(EXPECTED.TryGetValue(type.Name, out expected),
                    type.Name + " が期待表に無い (設計書 §3.1 とテストを更新すること)");
                Assert.Equal(expected, attr.Category);
            }
        }

        [Fact]
        public void 期待表の型がすべて存在する()
        {
            var names = new HashSet<string>(GetConcreteLayerTypes().Select(t => t.Name));
            foreach (var name in EXPECTED.Keys)
            {
                Assert.Contains(name, names);
            }
        }

        [Fact]
        public void 全カテゴリに表示名がある()
        {
            foreach (TimelineLayerCategory category in Enum.GetValues(typeof(TimelineLayerCategory)))
            {
                Assert.False(string.IsNullOrEmpty(category.ToDisplayName()), category + " の表示名が空");
            }
        }
    }
}
