using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>シーンプリセット検索の収集・照合・相対パスを固定する</summary>
    public class ScenePresetSearchTests
    {
        private static TileViewContentBase Dir(string name)
        {
            return new TileViewContentBase
            {
                name = name,
                isDir = true,
                children = new List<ITileViewContent>(),
            };
        }

        private static TileViewContentBase File(string name)
        {
            return new TileViewContentBase { name = name };
        }

        /// <summary>
        /// root
        ///   Pose01, Kiss
        ///   [Sub] Pose02, kiss
        ///   [SceneCapture] Scene01, [Deep] Pose03
        /// </summary>
        private static TileViewContentBase CreateTree()
        {
            var root = Dir("ScenePreset");
            root.AddChild(File("Pose01"));
            root.AddChild(File("Kiss"));

            var sub = Dir("Sub");
            root.AddChild(sub);
            sub.AddChild(File("Pose02"));
            sub.AddChild(File("kiss"));

            var sc = Dir("SceneCapture");
            root.AddChild(sc);
            sc.AddChild(File("Scene01"));
            var deep = Dir("Deep");
            sc.AddChild(deep);
            deep.AddChild(File("Pose03"));

            return root;
        }

        private static List<string> CollectNames(ITileViewContent root, string text)
        {
            var result = new List<ITileViewContent>();
            ScenePresetSearch.Collect(root, text, result);
            return result.Select(item => item.name).ToList();
        }

        [Fact]
        public void 全階層から部分一致で集める()
        {
            Assert.Equal(new[] { "Pose01", "Pose02", "Pose03" }, CollectNames(CreateTree(), "pose"));
        }

        [Fact]
        public void 大文字小文字を区別しない()
        {
            Assert.Equal(new[] { "Kiss", "kiss" }, CollectNames(CreateTree(), "KISS"));
        }

        [Fact]
        public void 同名でも別フォルダなら両方返す()
        {
            var root = CreateTree();
            var result = new List<ITileViewContent>();
            ScenePresetSearch.Collect(root, "kiss", result);
            Assert.Equal(2, result.Count);
            Assert.NotEqual(
                ScenePresetSearch.GetDisplayPath(result[0], root),
                ScenePresetSearch.GetDisplayPath(result[1], root));
        }

        [Fact]
        public void フォルダ名には一致させない()
        {
            Assert.Empty(CollectNames(CreateTree(), "Deep"));
        }

        [Fact]
        public void 空文字は何も返さない()
        {
            Assert.Empty(CollectNames(CreateTree(), ""));
            Assert.Empty(CollectNames(CreateTree(), null));
        }

        [Fact]
        public void 空白も文字として照合する()
        {
            Assert.Empty(CollectNames(CreateTree(), " "));
        }

        [Fact]
        public void かなは同一視しない()
        {
            var root = Dir("ScenePreset");
            root.AddChild(File("キス"));
            Assert.Empty(CollectNames(root, "きす"));
            Assert.Equal(new[] { "キス" }, CollectNames(root, "キ"));
        }

        [Fact]
        public void 結果へ追記し元のツリーの親を変えない()
        {
            var root = CreateTree();
            var searchRoot = new TempTileViewContent
            {
                isDir = true,
                children = new List<ITileViewContent>(),
            };
            var result = new List<ITileViewContent>();
            ScenePresetSearch.Collect(root, "Pose03", result);
            foreach (var item in result)
            {
                searchRoot.AddChild(item);
            }

            Assert.Single(searchRoot.children);
            Assert.Equal("Deep", searchRoot.children[0].parent.name);
        }

        [Fact]
        public void 相対パス_直下はファイル名だけ()
        {
            var root = CreateTree();
            Assert.Equal("Pose01", ScenePresetSearch.GetDisplayPath(root.children[0], root));
        }

        [Fact]
        public void 相対パス_SceneCapture配下はフォルダ名から始まる()
        {
            var root = CreateTree();
            var result = new List<ITileViewContent>();
            ScenePresetSearch.Collect(root, "Pose03", result);
            Assert.Equal("SceneCapture/Deep/Pose03", ScenePresetSearch.GetDisplayPath(result[0], root));
        }

        [Fact]
        public void 相対パス_ルートに届かなければ辿れた分だけ返す()
        {
            var orphanDir = Dir("Orphan");
            var file = File("Lost");
            orphanDir.AddChild(file);
            Assert.Equal("Orphan/Lost", ScenePresetSearch.GetDisplayPath(file, Dir("Other")));
        }

        [Fact]
        public void 照合_IsMatch()
        {
            Assert.True(ScenePresetSearch.IsMatch("Pose01", "SE0"));
            Assert.False(ScenePresetSearch.IsMatch("Pose01", "x"));
            Assert.False(ScenePresetSearch.IsMatch("Pose01", ""));
        }
    }
}
