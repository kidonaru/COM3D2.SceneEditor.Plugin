using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    // MTE 互換の総点検: タイムライン XML に現れる全レイヤーが SE に登録済みで
    // 「読み込み時に破棄されない」ことを機械検証する
    public class MteCompatibilityTests
    {
        private static string BaseDir => AppDomain.CurrentDomain.BaseDirectory;

        // TimelineIntegration.cs の RegisterLayer 呼び出しから登録済みレイヤー名を抽出する。
        // リポジトリ内でのみ実行可能な開発用テスト (ソースツリー前提)
        private static HashSet<string> GetRegisteredLayerNames()
        {
            var path = Path.GetFullPath(Path.Combine(
                BaseDir, @"..\..\..\..\COM3D2.SceneEditor.Plugin\Timeline\TimelineIntegration.cs"));
            Assert.True(File.Exists(path), "TimelineIntegration.cs が見つかりません: " + path);

            var source = File.ReadAllText(path);
            var names = new HashSet<string>();
            foreach (Match m in Regex.Matches(source, @"RegisterLayer\(\s*typeof\(MTEP\.(\w+)\)"))
            {
                names.Add(m.Groups[1].Value);
            }
            Assert.NotEmpty(names);
            return names;
        }

        private static IEnumerable<string> GetLayerClassNames(string xmlPath)
        {
            var doc = XDocument.Load(xmlPath);
            return doc.Root.Elements("Layer")
                .Select(l => (string) l.Element("ClassName"))
                .Where(n => !string.IsNullOrEmpty(n));
        }

        [Fact]
        public void 全フィクスチャのレイヤーが登録済みである()
        {
            var registered = GetRegisteredLayerNames();
            var fixtureDir = Path.Combine(BaseDir, "Fixtures");
            var missing = new List<string>();

            foreach (var xml in Directory.GetFiles(fixtureDir, "*.xml"))
            {
                foreach (var className in GetLayerClassNames(xml))
                {
                    if (!registered.Contains(className))
                    {
                        missing.Add(Path.GetFileName(xml) + ": " + className);
                    }
                }
            }

            Assert.True(missing.Count == 0,
                "未登録レイヤー (読み込み時に破棄される):\n" + string.Join("\n", missing));
        }

        [Fact]
        public void ローカルの実プロジェクトXMLのレイヤーが登録済みである()
        {
            // 実プロジェクト XML はリポジトリにコミットしないため、ローカルにある場合のみ検証する
            var dir = Environment.GetEnvironmentVariable("COM3D2_TIMELINE_DIR")
                ?? @"W:\COM3D2_5\PhotoModeData\_Timeline";
            if (!Directory.Exists(dir))
            {
                return; // オプトイン: ディレクトリが無い環境ではスキップ
            }

            var registered = GetRegisteredLayerNames();
            var missing = new List<string>();

            foreach (var xml in Directory.GetFiles(dir, "*.xml", SearchOption.AllDirectories))
            {
                try
                {
                    foreach (var className in GetLayerClassNames(xml))
                    {
                        if (!registered.Contains(className))
                        {
                            missing.Add(Path.GetFileName(xml) + ": " + className);
                        }
                    }
                }
                catch (System.Xml.XmlException)
                {
                    // タイムライン以外の XML が混在していても無視する
                }
            }

            Assert.True(missing.Count == 0,
                "未登録レイヤー (読み込み時に破棄される):\n" + string.Join("\n", missing.Distinct()));
        }
    }
}
