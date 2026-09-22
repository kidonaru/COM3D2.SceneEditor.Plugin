using System;
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// キー割り当ては SceneEditor.xml (SE 本体の Config) が唯一の持ち主。
    /// 既定の取りこぼしは実行時の KeyNotFoundException になるためここで固定する
    /// </summary>
    public class KeyBindConfigTests
    {
        [Fact]
        public void 全てのKeyBindTypeに既定の割り当てがある()
        {
            var config = new Config();

            foreach (KeyBindType type in Enum.GetValues(typeof(KeyBindType)))
            {
                Assert.True(
                    config.keyBinds.ContainsKey(type),
                    "既定の割り当てがありません: " + type);
            }
        }

        [Fact]
        public void タイムライン操作のキーがSceneEditor側にある()
        {
            var config = new Config();

            // KeyBind.ToString は KeyCode.Return を "Enter" として返す
            Assert.Equal("Enter", config.GetKeyName(KeyBindType.AddKeyFrame));
            Assert.Equal("Shift+Enter", config.GetKeyName(KeyBindType.AddKeyFrameAll));
            Assert.Equal("Backspace", config.GetKeyName(KeyBindType.RemoveKeyFrame));
            Assert.Equal("Space", config.GetKeyName(KeyBindType.Play));
            Assert.Equal("Shift", config.GetKeyName(KeyBindType.MultiSelect));
        }

        [Fact]
        public void レイヤーカテゴリはenum定義順に1から6のキーへ対応する()
        {
            var config = new Config();

            Assert.Equal("Alpha1", config.GetKeyName(Config.GetCategoryKeyBindType(MTEP.TimelineLayerCategory.Maid)));
            Assert.Equal("Alpha2", config.GetKeyName(Config.GetCategoryKeyBindType(MTEP.TimelineLayerCategory.Camera)));
            Assert.Equal("Alpha3", config.GetKeyName(Config.GetCategoryKeyBindType(MTEP.TimelineLayerCategory.Model)));
            Assert.Equal("Alpha4", config.GetKeyName(Config.GetCategoryKeyBindType(MTEP.TimelineLayerCategory.Background)));
            Assert.Equal("Alpha5", config.GetKeyName(Config.GetCategoryKeyBindType(MTEP.TimelineLayerCategory.Effect)));
            Assert.Equal("Alpha6", config.GetKeyName(Config.GetCategoryKeyBindType(MTEP.TimelineLayerCategory.Other)));
        }

        [Fact]
        public void カテゴリ数とカテゴリ切替キー数が一致する()
        {
            // TimelineLayerCategory へ値を足したら SelectCategoryN と既定値も足す必要がある
            var categoryCount = Enum.GetValues(typeof(MTEP.TimelineLayerCategory)).Length;
            var keyCount = 0;
            foreach (KeyBindType type in Enum.GetValues(typeof(KeyBindType)))
            {
                if (type.ToString().StartsWith("SelectCategory"))
                {
                    keyCount++;
                }
            }

            Assert.Equal(categoryCount, keyCount);
        }

        [Fact]
        public void カテゴリキーの表示名は数字キーの接頭辞を落とす()
        {
            var config = new Config();

            Assert.Equal("1", config.GetCategoryKeyLabel(MTEP.TimelineLayerCategory.Maid));

            config.keyBinds[Config.GetCategoryKeyBindType(MTEP.TimelineLayerCategory.Maid)] = new KeyBind("Ctrl+Keypad1");
            Assert.Equal("Ctrl+1", config.GetCategoryKeyLabel(MTEP.TimelineLayerCategory.Maid));

            config.keyBinds[Config.GetCategoryKeyBindType(MTEP.TimelineLayerCategory.Maid)] = new KeyBind("F5");
            Assert.Equal("F5", config.GetCategoryKeyLabel(MTEP.TimelineLayerCategory.Maid));

            // 未割り当てなら空文字 (コンボ表示側で接頭辞を省く)
            config.keyBinds[Config.GetCategoryKeyBindType(MTEP.TimelineLayerCategory.Maid)] = new KeyBind("");
            Assert.Equal("", config.GetCategoryKeyLabel(MTEP.TimelineLayerCategory.Maid));
        }

        [Fact]
        public void 旧TimelineXmlのkeyBind要素は読み飛ばして他の項目を復元する()
        {
            // クラスから削除済みの項目。既存ユーザーの Timeline.xml には残っている
            const string xml =
                "<?xml version=\"1.0\"?>"
                + "<Config xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">"
                + "<keyBind><key>AddKeyFrame</key><value>Return</value></keyBind>"
                + "<keyBind><key>EditMode</key><value>F1</value></keyBind>"
                + "<disablePoseHistory>false</disablePoseHistory>"
                + "<voiceMaxLength>12.5</voiceMaxLength>"
                + "</Config>";

            var serializer = new XmlSerializer(typeof(MTEP.Config));
            using (var reader = new StringReader(xml))
            {
                var config = (MTEP.Config) serializer.Deserialize(reader);

                Assert.Equal(12.5f, config.voiceMaxLength);
            }
        }
    }
}
