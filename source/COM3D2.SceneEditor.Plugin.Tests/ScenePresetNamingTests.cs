using System;
using System.IO;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>プリセット保存ポップアップの日時の既定名を固定する</summary>
    public class ScenePresetNamingTests
    {
        [Fact]
        public void 日時名_年月日と時分秒をアンダースコアでつなぐ()
        {
            var name = ScenePresetNaming.CreateDateName(new DateTime(2026, 9, 8, 7, 5, 3));
            Assert.Equal("20260908_070503", name);
        }

        [Fact]
        public void 日時名_プリセット名の禁則を含まない()
        {
            var name = ScenePresetNaming.CreateDateName(new DateTime(2026, 12, 31, 23, 59, 59));
            Assert.DoesNotContain(".", name);
            Assert.Equal(-1, name.IndexOfAny(Path.GetInvalidFileNameChars()));
        }
    }
}
