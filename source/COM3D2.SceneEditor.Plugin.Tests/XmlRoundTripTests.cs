using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    // MTE 実プロジェクト XML のゴールデンテスト。
    // 読み込み → 保存 → 再読み込み → 保存 で XML が安定することを検証する。
    // バージョン移行 (version < N の分岐) は通さず、シリアライザ純粋往復のみを対象とする。
    public class XmlRoundTripTests
    {
        // カレントディレクトリ非依存でフィクスチャを解決する
        private static string FixtureDir =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures");

        public static TheoryData<string> FixtureFiles()
        {
            var data = new TheoryData<string>();
            foreach (var f in Directory.GetFiles(FixtureDir, "*.xml"))
                data.Add(Path.GetFileName(f));
            return data;
        }

        // 旧スキーマの要素 (Light/LightType 等) は現行クラスから削除済みで、実経路では
        // 読み込み時のバージョン移行が引き受ける。移行を通さない本テストでは欠損して
        // 見えるため、*.legacy.xml は情報保存性の検証対象から除外する (安定性は検証する)
        public static TheoryData<string> CurrentSchemaFixtureFiles()
        {
            var data = new TheoryData<string>();
            foreach (var f in Directory.GetFiles(FixtureDir, "*.xml"))
                if (!f.EndsWith(".legacy.xml"))
                    data.Add(Path.GetFileName(f));
            return data;
        }

        [Fact]
        public void フィクスチャが1件以上存在する()
        {
            // フィクスチャ 0 件だと Theory がサイレントスキップになるのを防ぐ番兵
            Assert.NotEmpty(Directory.GetFiles(FixtureDir, "*.xml"));
            // 現行スキーマ用 (legacy 除外後) も 0 件にならないことを保証する
            Assert.NotEmpty(CurrentSchemaFixtureFiles());
        }

        private static string SerializeToString(TimelineXml xml, XmlSerializer serializer)
        {
            using (var sw = new StringWriter())
            {
                serializer.Serialize(sw, xml);
                return sw.ToString();
            }
        }

        [Theory]
        [MemberData(nameof(FixtureFiles))]
        public void フィクスチャXMLの往復が安定する(string fileName)
        {
            var path = Path.Combine(FixtureDir, fileName);
            var serializer = new XmlSerializer(typeof(TimelineXml));

            TimelineXml first;
            using (var fs = File.OpenRead(path))
                first = (TimelineXml)serializer.Deserialize(fs);
            var save1 = SerializeToString(first, serializer);

            TimelineXml second;
            using (var sr = new StringReader(save1))
                second = (TimelineXml)serializer.Deserialize(sr);
            var save2 = SerializeToString(second, serializer);

            Assert.Equal(save1, save2); // 保存の安定性
        }

        [Theory]
        [MemberData(nameof(CurrentSchemaFixtureFiles))]
        public void フィクスチャXMLの情報が保存される(string fileName)
        {
            var path = Path.Combine(FixtureDir, fileName);
            var serializer = new XmlSerializer(typeof(TimelineXml));

            TimelineXml loaded;
            using (var fs = File.OpenRead(path))
                loaded = (TimelineXml)serializer.Deserialize(fs);
            var saved = XDocument.Parse(SerializeToString(loaded, serializer));
            var original = XDocument.Load(path);

            // 完全一致 (DeepEquals) は、保存側にスキーマ進化で追加された新フィールドの
            // 既定値が出力されるため恒常的に不一致になる (v25 フィクスチャで確認済み)。
            // そのため「original の全要素・属性が saved に同値で存在する (欠損なし)」の
            // サブセット検証に緩和する。データ欠損は引き続き検出できる。
            var errors = new System.Collections.Generic.List<string>();
            AssertSubset(original.Root, saved.Root, "/" + original.Root.Name, errors);
            Assert.True(errors.Count == 0,
                $"元 XML の情報が保存されていません: {fileName}\n" + string.Join("\n", errors.Take(20)));
        }

        // original の要素ツリーが saved に (値も含めて) 含まれることを検証する。
        // 同名兄弟要素 (Frame 等のリスト) は出現順で対応付ける。
        private static void AssertSubset(XElement original, XElement saved, string xmlPath,
            System.Collections.Generic.List<string> errors)
        {
            foreach (var attr in original.Attributes().Where(a => !a.IsNamespaceDeclaration))
            {
                var savedAttr = saved.Attribute(attr.Name);
                if (savedAttr == null)
                    errors.Add($"{xmlPath}/@{attr.Name}: 属性が欠損");
                else if (savedAttr.Value != attr.Value)
                    errors.Add($"{xmlPath}/@{attr.Name}: 値不一致 '{attr.Value}' → '{savedAttr.Value}'");
            }

            if (!original.HasElements)
            {
                if (original.Value != saved.Value)
                    errors.Add($"{xmlPath}: 値不一致 '{original.Value}' → '{saved.Value}'");
                return;
            }

            // 同名兄弟要素 (Frame 等のリスト) は出現順で 1 対 1 に対応付けて比較する
            var savedGroups = saved.Elements().GroupBy(e => e.Name)
                .ToDictionary(g => g.Key, g => g.ToList());
            var indexByName = new System.Collections.Generic.Dictionary<XName, int>();
            foreach (var child in original.Elements())
            {
                indexByName.TryGetValue(child.Name, out var idx);
                indexByName[child.Name] = idx + 1;
                if (!savedGroups.TryGetValue(child.Name, out var candidates) || idx >= candidates.Count)
                {
                    errors.Add($"{xmlPath}/{child.Name}[{idx}]: 要素が欠損");
                    continue;
                }
                AssertSubset(child, candidates[idx], $"{xmlPath}/{child.Name}[{idx}]", errors);
            }
        }
    }
}
