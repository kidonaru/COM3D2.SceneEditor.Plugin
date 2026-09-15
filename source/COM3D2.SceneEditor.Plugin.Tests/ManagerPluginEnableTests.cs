using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using COM3D2.MotionTimelineEditor;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class ManagerPluginEnableTests
    {
        // 設計メモ「決定事項」の表。
        // タイムラインと無関係な基盤だけが再有効化で戻す。
        // 空実装を持つ 3 つは「タイムライン由来なので戻さない」という判断の表明として含める。
        // SE 側にも同名の StudioLightManager があるため、キーは FullName で持つ
        private static readonly HashSet<string> EXPECTED_OVERRIDES = new HashSet<string>
        {
            "COM3D2.MotionTimelineEditor.Plugin.StudioLightManager",
            "COM3D2.MotionTimelineEditor.Plugin.CameraManager",
            "COM3D2.MotionTimelineEditor.Plugin.SubCameraManager",
            "COM3D2.MotionTimelineEditor.Plugin.TimelineTextManager",
            "COM3D2.MotionTimelineEditor.Plugin.StageLightManager",
            "COM3D2.MotionTimelineEditor.Plugin.StageLaserManager",
            "COM3D2.MotionTimelineEditor.Plugin.PsylliumManager",

            // 復帰対象ではなく、各マネージャへ OnPluginEnable を配るディスパッチャ
            "COM3D2.SceneEditor.Plugin.TimelineIntegration+TimelineUpdateManager",
        };

        private static List<Type> GetConcreteManagerTypes()
        {
            var assembly = typeof(IManager).Assembly;
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToArray();
            }
            var managers = types
                .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IManager).IsAssignableFrom(t))
                .ToList();

            // ManagerBase のような「他のマネージャの土台」は、仮想メソッドを宣言しているだけで
            // 自身はマネージャではないので対象から外す
            return managers
                .Where(t => !managers.Any(other => other != t && t.IsAssignableFrom(other)))
                .ToList();
        }

        /// <summary>自前で OnPluginEnable を実装しているか（基底からの継承なら false）</summary>
        private static bool HasOwnOnPluginEnable(Type type)
        {
            var method = type.GetMethod("OnPluginEnable", Type.EmptyTypes);
            return method != null && method.DeclaringType == type;
        }

        [Fact]
        public void 再有効化で戻すマネージャが期待どおり()
        {
            var types = GetConcreteManagerTypes();
            Assert.NotEmpty(types);

            foreach (var type in types)
            {
                var expected = EXPECTED_OVERRIDES.Contains(type.FullName);
                var actual = HasOwnOnPluginEnable(type);
                Assert.True(expected == actual,
                    type.FullName + " の OnPluginEnable 実装が期待と違う: expected=" + expected + " actual=" + actual);
            }
        }

        [Fact]
        public void 期待表のマネージャ型がすべて実在する()
        {
            var names = GetConcreteManagerTypes().Select(t => t.FullName).ToList();
            foreach (var name in EXPECTED_OVERRIDES)
            {
                Assert.Contains(name, names);
            }
        }
    }
}
