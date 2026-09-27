using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// タイムラインのテクスチャ差し替え (&lt;MaterialShader&gt; の &lt;Texture&gt;) の保存と読込を固定する。
    /// テクスチャだけのエントリは &lt;Shader&gt; を書かない (旧版はシェーダーが空のエントリを読まない)
    /// </summary>
    public class MaterialTextureXmlTests
    {
        private static string Serialize(TimelineXml xml)
        {
            var serializer = new XmlSerializer(typeof(TimelineXml));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, xml);
                return writer.ToString();
            }
        }

        private static TimelineXml Deserialize(string text)
        {
            var serializer = new XmlSerializer(typeof(TimelineXml));
            using (var reader = new StringReader(text))
            {
                return (TimelineXml)serializer.Deserialize(reader);
            }
        }

        private static TimelineMaterialShaderData TextureOnly()
        {
            return new TimelineMaterialShaderData
            {
                maidSlotNo = 0, owner = "body", material = "skin", index = 0,
                textures = new List<MaterialTextureOverride>
                {
                    new MaterialTextureOverride("_ShadowRateToon", "Toon/0_影なし.png"),
                    new MaterialTextureOverride("_ToonRamp", "Toon/2_影標準.png"),
                },
            };
        }

        [Fact]
        public void テクスチャだけのエントリを往復する()
        {
            var xml = new TimelineXml { version = TimelineData.CurrentVersion };
            xml.materialShaders.Add(TextureOnly().ToXml());

            var text = Serialize(xml);
            Assert.DoesNotContain("<Shader", text);
            Assert.Contains("<Texture prop=\"_ShadowRateToon\" file=\"Toon/0_影なし.png\" />", text);

            var data = new TimelineData();
            data.FromXml(Deserialize(text));
            Assert.Single(data.materialShaders);
            Assert.True(TextureOnly().ContentEquals(data.materialShaders[0]));
        }

        [Fact]
        public void シェーダーとテクスチャを併せて往復する()
        {
            var entry = TextureOnly();
            entry.shader = "com3d2mod/Standard_NPRToonV2_Lit_";
            var xml = new TimelineXml { version = TimelineData.CurrentVersion };
            xml.materialShaders.Add(entry.ToXml());

            var restored = new TimelineMaterialShaderData();
            restored.FromXml(Deserialize(Serialize(xml)).materialShaders[0]);

            Assert.True(entry.ContentEquals(restored));
        }

        [Fact]
        public void 要素の無い旧エントリはテクスチャなしとして読む()
        {
            var restored = Deserialize(
                "<?xml version=\"1.0\" encoding=\"utf-16\"?><TimelineData version=\"38\"><MaterialShaders>"
                + "<MaterialShader><MaidSlotNo>0</MaidSlotNo><Owner>wear</Owner><Material>a</Material>"
                + "<Index>0</Index><Shader>CM3D2/Lighted</Shader></MaterialShader></MaterialShaders></TimelineData>");

            var data = new TimelineMaterialShaderData();
            data.FromXml(restored.materialShaders[0]);
            Assert.Empty(data.textures);
            Assert.Equal("CM3D2/Lighted", data.shader);
        }

        [Fact]
        public void 不正なテクスチャ指定は読まない()
        {
            var xml = new TimelineMaterialShaderXml
            {
                maidSlotNo = 0, owner = "body", material = "skin",
                textures = new List<TimelineMaterialTextureXml>
                {
                    new TimelineMaterialTextureXml { property = "_ToonRamp", file = "../../evil.png" },
                    new TimelineMaterialTextureXml { property = "_ToonRamp", file = "Texture/a.png" },
                    new TimelineMaterialTextureXml { property = "_Color", file = "Toon/a.png" },
                    new TimelineMaterialTextureXml { property = "_ShadowRateToon", file = "Toon\\b.png" },
                    new TimelineMaterialTextureXml { property = "_ShadowRateToon", file = "Toon/c.png" },
                },
            };

            var data = new TimelineMaterialShaderData();
            data.FromXml(xml);

            // 区切りは揃え、同じプロパティは先勝ち
            Assert.Single(data.textures);
            Assert.Equal("_ShadowRateToon", data.textures[0].property);
            Assert.Equal("Toon/b.png", data.textures[0].file);
        }

        [Fact]
        public void シェーダーも有効なテクスチャも無いエントリはタイムラインに読まない()
        {
            var xml = new TimelineXml { version = TimelineData.CurrentVersion };
            xml.materialShaders.Add(new TimelineMaterialShaderXml
            {
                maidSlotNo = 0, owner = "body", material = "skin",
                textures = new List<TimelineMaterialTextureXml>
                {
                    new TimelineMaterialTextureXml { property = "_ToonRamp", file = "../x.png" },
                },
            });
            xml.materialShaders.Add(TextureOnly().ToXml());

            var data = new TimelineData();
            data.FromXml(xml);

            Assert.Single(data.materialShaders);
        }

        [Fact]
        public void テクスチャが違えば内容が違う()
        {
            var a = TextureOnly();
            var b = TextureOnly();
            b.textures[1] = new MaterialTextureOverride("_ToonRamp", "Toon/3_影濃いめ.png");

            Assert.True(a.IsSameTarget(b));
            Assert.False(a.ContentEquals(b));
        }

        [Fact]
        public void 複製はテクスチャ一覧を共有しない()
        {
            var original = TextureOnly();
            var clone = original.Clone();
            clone.textures.RemoveAt(0);

            Assert.Equal(2, original.textures.Count);
        }
    }
}
