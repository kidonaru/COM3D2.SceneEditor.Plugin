using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// レイヤー型を列挙するテスト用ヘルパー。
    /// 型ロードの条件 (ゲーム依存の型で落ちる分を握る範囲) を 1 か所に集約し、
    /// 複数のテストクラスで食い違わないようにする
    /// </summary>
    internal static class TimelineLayerTestUtils
    {
        /// <summary>
        /// プラグイン DLL 内の具象レイヤー型。ゲーム依存の型読み込みに失敗しても
        /// 読めた分だけで検証できるよう ReflectionTypeLoadException は握る
        /// </summary>
        public static List<Type> GetConcreteLayerTypes()
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
    }
}
