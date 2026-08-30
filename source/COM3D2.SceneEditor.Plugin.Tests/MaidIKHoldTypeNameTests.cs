using System;
using System.Linq;
using COM3D2.MotionTimelineEditor.Plugin;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// キーフレームの IK ボーン名は MTE の IKHoldType メンバ名そのもの。
    /// SE の MaidIKHoldType と名前がずれると、既存タイムラインの IK キーが
    /// 一切引けなくなるため、対応表をテストで固定する
    /// </summary>
    public class MaidIKHoldTypeNameTests
    {
        [Fact]
        public void MTEのIKHoldType名がすべてSE側へ解決できる()
        {
            var names = Enum.GetValues(typeof(IKHoldType))
                .Cast<IKHoldType>()
                .Where(t => t != IKHoldType.Max)
                .Select(t => t.ToString());

            foreach (var name in names)
            {
                MaidIKHoldType type;
                Assert.True(
                    MaidIKHoldController.TryParseHoldType(name, out type),
                    "解決できませんでした: " + name);
                Assert.Equal(name, type.ToString());
            }
        }

        [Fact]
        public void 未知の名前は解決できない()
        {
            MaidIKHoldType type;
            Assert.False(MaidIKHoldController.TryParseHoldType("Head_Tip", out type));
        }

        [Fact]
        public void nullや空文字でも例外にならない()
        {
            MaidIKHoldType type;
            Assert.False(MaidIKHoldController.TryParseHoldType(null, out type));
            Assert.False(MaidIKHoldController.TryParseHoldType("", out type));
        }
    }
}
