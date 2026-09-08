using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    // 演出系スナップショットの無変化判定は DTO の XML 直列化で行う。
    // フィールドを列挙して比較しないため、DTO へ項目を足しても判定が漏れない
    public class PresetDtoUtilsTests
    {
        private static ScenePresetText MakeText()
        {
            return new ScenePresetText
            {
                text = "abc",
                fontSize = 20,
                position = new Vector3(1f, 2f, 3f),
                color = Color.red,
            };
        }

        [Fact]
        public void 同じ内容なら等しい()
        {
            Assert.True(PresetDtoUtils.AreEqual(MakeText(), MakeText()));
        }

        [Fact]
        public void 値が違えば等しくない()
        {
            var b = MakeText();
            b.position = new Vector3(1f, 2f, 4f);
            Assert.False(PresetDtoUtils.AreEqual(MakeText(), b));
        }

        [Fact]
        public void リストの要素数が違えば等しくない()
        {
            var a = new List<ScenePresetText> { MakeText() };
            var b = new List<ScenePresetText> { MakeText(), MakeText() };
            Assert.False(PresetDtoUtils.AreEqual(a, b));
        }

        [Fact]
        public void 片方がnullなら等しくない()
        {
            Assert.False(PresetDtoUtils.AreEqual(MakeText(), null));
            Assert.True(PresetDtoUtils.AreEqual<ScenePresetText>(null, null));
        }
    }
}
