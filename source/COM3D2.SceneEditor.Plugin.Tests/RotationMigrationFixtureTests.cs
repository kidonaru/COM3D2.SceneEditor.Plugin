using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// version 35 の回転移行を、実プロジェクト由来のフィクスチャに対して検証する。
    /// 合成データのテストは添字を正しく数えたかしか見ないので、本物のキーでも通しておく。
    ///
    /// フィクスチャのステージ効果・ポストエフェクト系は MTE 時代の 2〜4 値スタブで、
    /// 移行の値数条件を満たさず素通しされる。実長のキーを持つのは PngObject と Text だけ
    /// </summary>
    public class RotationMigrationFixtureTests
    {
        private static string FixtureDir =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures");

        private static TimelineXml LoadAndMigrate(string fileName)
        {
            var serializer = new XmlSerializer(typeof(TimelineXml));
            using (var stream = new FileStream(
                Path.Combine(FixtureDir, fileName), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var xml = (TimelineXml)serializer.Deserialize(stream);
                xml.Initialize();
                return xml;
            }
        }

        private static List<TransformXml> CollectTransforms(TimelineXml xml, TransformType type)
        {
            var result = new List<TransformXml>();
            foreach (var layer in xml.layers)
            {
                foreach (var keyFrame in layer.keyFrames)
                {
                    if (keyFrame.bones == null)
                    {
                        continue;
                    }
                    foreach (var bone in keyFrame.bones)
                    {
                        if (bone.transform != null && bone.transform.type == type)
                        {
                            result.Add(bone.transform);
                        }
                    }
                }
            }
            return result;
        }

        private static void AssertUnitQuaternion(TransformXml transform, int head)
        {
            var q = new Quaternion(
                transform.values[head], transform.values[head + 1],
                transform.values[head + 2], transform.values[head + 3]);
            var magnitude = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);

            // 添字がずれていれば回転でない値が混ざり、単位長から外れる
            Assert.Equal(1f, magnitude, 3);
        }

        [Theory]
        // 値数・回転先頭添字は実装の valueCount / Index ではなく期待値をベタ書きする
        [InlineData("l7-pngobject.xml", TransformType.PngObject, 32, 3)]
        // version 31 保存なので v32 のポストエフェクト移行を挟んでから v35 が走る
        [InlineData("l8-dcm-layers.xml", TransformType.Text, 21, 3)]
        public void 実長のキーは移行後の値数と単位長の回転を持つ(
            string fileName, TransformType type, int expectedCount, int rotationHead)
        {
            var xml = LoadAndMigrate(fileName);
            var transforms = CollectTransforms(xml, type);

            // 対象レコードが 0 件だとテストが素通りするので番兵を置く
            Assert.NotEmpty(transforms);

            foreach (var transform in transforms)
            {
                Assert.Equal(expectedCount, transform.values.Length);
                AssertUnitQuaternion(transform, rotationHead);
            }
        }

        [Theory]
        // MTE 時代のスタブは値数が足りず、移行の条件を満たさない。
        // 無理に変換すると壊れるので素通しされることを固定する
        [InlineData("l5-stage-effects.xml", TransformType.StageLight, 4)]
        [InlineData("l5-stage-effects.xml", TransformType.StageLaser, 2)]
        [InlineData("l5-stage-effects.xml", TransformType.StageLaserController, 2)]
        [InlineData("l5-stage-effects.xml", TransformType.PsylliumTransform, 3)]
        [InlineData("l6-posteffect.xml", TransformType.Rimlight, 2)]
        public void 値数の足りないスタブは素通しされる(
            string fileName, TransformType type, int expectedCount)
        {
            var xml = LoadAndMigrate(fileName);
            var transforms = CollectTransforms(xml, type);

            Assert.NotEmpty(transforms);

            foreach (var transform in transforms)
            {
                Assert.Equal(expectedCount, transform.values.Length);
            }
        }

        [Fact]
        public void 移行済みのXMLを再度読み込んでも二重変換されない()
        {
            var xml = LoadAndMigrate("l7-pngobject.xml");

            // Initialize は version を書き換えないので、保存相当の版数へ上げてから再実行する
            xml.version = TimelineData.CurrentVersion;
            var transforms = CollectTransforms(xml, TransformType.PngObject);
            Assert.NotEmpty(transforms);

            var before = new List<float[]>();
            foreach (var transform in transforms)
            {
                before.Add((float[])transform.values.Clone());
            }

            xml.Initialize();

            for (var i = 0; i < transforms.Count; i++)
            {
                Assert.Equal(before[i], transforms[i].values);
            }
        }
    }
}
