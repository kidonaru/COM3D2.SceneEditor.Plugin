using System.Collections.Generic;
using System.Linq;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>Hierarchy 配置物ビューの木の組み立てと変化検知を固定する</summary>
    public class PlacedObjectTreeTests
    {
        private static PlacedObjectSource Src(
            PlacedObjectCategory category, int id, string label, int parentId = PlacedObjectTree.NoParent)
        {
            return new PlacedObjectSource
            {
                category = category,
                id = id,
                label = label,
                parentId = parentId,
            };
        }

        private static PlacedObjectNode FindCategory(PlacedObjectTree tree, PlacedObjectCategory category)
        {
            var id = PlacedObjectTree.GetCategoryId(category);
            return tree.roots.FirstOrDefault(n => n.id == id);
        }

        private static List<PlacedObjectNode> AllNodes(IEnumerable<PlacedObjectNode> nodes)
        {
            var result = new List<PlacedObjectNode>();
            foreach (var node in nodes)
            {
                result.Add(node);
                result.AddRange(AllNodes(node.children));
            }
            return result;
        }

        [Fact]
        public void 組み立て_空の一覧はカテゴリを出さない()
        {
            Assert.Empty(PlacedObjectTree.Build(new List<PlacedObjectSource>()).roots);
            Assert.Empty(PlacedObjectTree.Build(null).roots);
        }

        [Fact]
        public void 組み立て_カテゴリは定義順で並び空のカテゴリは省く()
        {
            var tree = PlacedObjectTree.Build(new[]
            {
                Src(PlacedObjectCategory.Light, 50, "Light1"),
                Src(PlacedObjectCategory.Maid, 10, "メイドA"),
                Src(PlacedObjectCategory.Png, 40, "PNG1"),
            });

            Assert.Equal(
                new[]
                {
                    PlacedObjectTree.GetCategoryId(PlacedObjectCategory.Maid),
                    PlacedObjectTree.GetCategoryId(PlacedObjectCategory.Png),
                    PlacedObjectTree.GetCategoryId(PlacedObjectCategory.Light),
                },
                tree.roots.Select(n => n.id).ToArray());
            Assert.All(tree.roots, n => Assert.True(n.isCategory));
            Assert.All(tree.roots, n => Assert.Null(n.target));
        }

        [Fact]
        public void 組み立て_カテゴリ見出しに直下の件数を出す()
        {
            var tree = PlacedObjectTree.Build(new[]
            {
                Src(PlacedObjectCategory.Maid, 10, "メイドA"),
                Src(PlacedObjectCategory.Maid, 11, "メイドB"),
            });

            Assert.Equal("メイド (2)", FindCategory(tree, PlacedObjectCategory.Maid).label);
        }

        [Fact]
        public void 組み立て_カテゴリ内は入力順を保つ()
        {
            var tree = PlacedObjectTree.Build(new[]
            {
                Src(PlacedObjectCategory.Png, 42, "c"),
                Src(PlacedObjectCategory.Png, 40, "a"),
                Src(PlacedObjectCategory.Png, 41, "b"),
            });

            Assert.Equal(new[] { "c", "a", "b" },
                FindCategory(tree, PlacedObjectCategory.Png).children.Select(n => n.label).ToArray());
        }

        [Fact]
        public void アタッチ_モデルは付け先メイドの子になる()
        {
            var tree = PlacedObjectTree.Build(new[]
            {
                Src(PlacedObjectCategory.Maid, 10, "メイドA"),
                Src(PlacedObjectCategory.Model, 20, "帽子", parentId: 10),
            });

            var maid = FindCategory(tree, PlacedObjectCategory.Maid).children.Single();
            Assert.Equal(20, maid.children.Single().id);
            Assert.Same(maid, maid.children[0].parent);
            // モデルがすべてアタッチ中ならモデルの見出しは出ない
            Assert.Null(FindCategory(tree, PlacedObjectCategory.Model));
        }

        [Fact]
        public void アタッチ_付け先メイドが一覧に無いモデルはモデルカテゴリ直下に出す()
        {
            var tree = PlacedObjectTree.Build(new[]
            {
                Src(PlacedObjectCategory.Model, 20, "帽子", parentId: 99),
            });

            Assert.Equal(20, FindCategory(tree, PlacedObjectCategory.Model).children.Single().id);
        }

        [Fact]
        public void アタッチ_親が入力の後ろにあっても付く()
        {
            var tree = PlacedObjectTree.Build(new[]
            {
                Src(PlacedObjectCategory.Model, 20, "帽子", parentId: 10),
                Src(PlacedObjectCategory.Maid, 10, "メイドA"),
            });

            Assert.Equal(20, FindCategory(tree, PlacedObjectCategory.Maid).children.Single().children.Single().id);
        }

        [Fact]
        public void 背景_親IDの入れ子を保つ()
        {
            var tree = PlacedObjectTree.Build(new[]
            {
                Src(PlacedObjectCategory.Background, 30, "Stage"),
                Src(PlacedObjectCategory.Background, 31, "Floor", parentId: 30),
                Src(PlacedObjectCategory.Background, 32, "Tile", parentId: 31),
                Src(PlacedObjectCategory.Background, 33, "Wall", parentId: 30),
            });

            var stage = FindCategory(tree, PlacedObjectCategory.Background).children.Single();
            Assert.Equal(new[] { 31, 33 }, stage.children.Select(n => n.id).ToArray());
            Assert.Equal(32, stage.children[0].children.Single().id);
            Assert.Equal("背景 (1)", FindCategory(tree, PlacedObjectCategory.Background).label);
        }

        [Fact]
        public void 異常入力_同じIDの重複は最初の1件だけ出す()
        {
            var tree = PlacedObjectTree.Build(new[]
            {
                Src(PlacedObjectCategory.Model, 20, "先"),
                Src(PlacedObjectCategory.Png, 20, "後"),
            });

            var all = AllNodes(tree.roots).Where(n => !n.isCategory).ToList();
            Assert.Single(all);
            Assert.Equal("先", all[0].label);
            Assert.Null(FindCategory(tree, PlacedObjectCategory.Png));
        }

        [Fact]
        public void 異常入力_親子が循環しても全件が出る()
        {
            var tree = PlacedObjectTree.Build(new[]
            {
                Src(PlacedObjectCategory.Background, 30, "A", parentId: 31),
                Src(PlacedObjectCategory.Background, 31, "B", parentId: 30),
            });

            var ids = AllNodes(tree.roots).Where(n => !n.isCategory).Select(n => n.id).OrderBy(i => i).ToArray();
            Assert.Equal(new[] { 30, 31 }, ids);
        }

        [Fact]
        public void 異常入力_自分自身を親にしてもカテゴリ直下に出る()
        {
            var tree = PlacedObjectTree.Build(new[]
            {
                Src(PlacedObjectCategory.Png, 40, "PNG1", parentId: 40),
            });

            Assert.Equal(40, FindCategory(tree, PlacedObjectCategory.Png).children.Single().id);
        }

        [Fact]
        public void 異常入力_ラベルがnullでも空文字として扱う()
        {
            var tree = PlacedObjectTree.Build(new[] { Src(PlacedObjectCategory.Png, 40, null) });

            Assert.Equal("", FindCategory(tree, PlacedObjectCategory.Png).children.Single().label);
        }

        [Fact]
        public void 祖先_祖先IDはルート側から並ぶ()
        {
            var tree = PlacedObjectTree.Build(new[]
            {
                Src(PlacedObjectCategory.Maid, 10, "メイドA"),
                Src(PlacedObjectCategory.Model, 20, "帽子", parentId: 10),
            });

            Assert.True(tree.Contains(20));
            Assert.Equal(
                new[] { PlacedObjectTree.GetCategoryId(PlacedObjectCategory.Maid), 10 },
                tree.GetAncestorIds(20).ToArray());
        }

        [Fact]
        public void 祖先_一覧に無いIDは空でContainsはfalse()
        {
            var tree = PlacedObjectTree.Build(new[] { Src(PlacedObjectCategory.Maid, 10, "メイドA") });

            Assert.False(tree.Contains(999));
            Assert.Empty(tree.GetAncestorIds(999));
        }

        [Fact]
        public void カテゴリID_カテゴリごとに異なり0ではない()
        {
            var ids = new[]
            {
                PlacedObjectCategory.Maid, PlacedObjectCategory.Model, PlacedObjectCategory.Background,
                PlacedObjectCategory.Png, PlacedObjectCategory.Light, PlacedObjectCategory.SubCamera,
            }.Select(PlacedObjectTree.GetCategoryId).ToArray();

            Assert.Equal(ids.Length, ids.Distinct().Count());
            Assert.DoesNotContain(PlacedObjectTree.NoParent, ids);
        }

        [Fact]
        public void 変化検知_同じ内容なら変化なし()
        {
            var a = new List<PlacedObjectSource> { Src(PlacedObjectCategory.Maid, 10, "メイドA") };
            var b = new List<PlacedObjectSource> { Src(PlacedObjectCategory.Maid, 10, "メイドA") };

            Assert.True(PlacedObjectTree.SameSources(a, b));
            Assert.True(PlacedObjectTree.SameSources(null, new List<PlacedObjectSource>()));
        }

        [Fact]
        public void 変化検知_ラベルの変化を拾う()
        {
            Assert.False(PlacedObjectTree.SameSources(
                new[] { Src(PlacedObjectCategory.Model, 20, "帽子") },
                new[] { Src(PlacedObjectCategory.Model, 20, "帽子2") }));
        }

        [Fact]
        public void 変化検知_親の変化を拾う()
        {
            Assert.False(PlacedObjectTree.SameSources(
                new[] { Src(PlacedObjectCategory.Model, 20, "帽子") },
                new[] { Src(PlacedObjectCategory.Model, 20, "帽子", parentId: 10) }));
        }

        [Fact]
        public void 変化検知_並びと件数の変化を拾う()
        {
            var x = Src(PlacedObjectCategory.Png, 40, "a");
            var y = Src(PlacedObjectCategory.Png, 41, "b");

            Assert.False(PlacedObjectTree.SameSources(new[] { x, y }, new[] { y, x }));
            Assert.False(PlacedObjectTree.SameSources(new[] { x, y }, new[] { x }));
        }

        [Fact]
        public void 変化検知_カテゴリの変化を拾う()
        {
            Assert.False(PlacedObjectTree.SameSources(
                new[] { Src(PlacedObjectCategory.Model, 20, "a") },
                new[] { Src(PlacedObjectCategory.Png, 20, "a") }));
        }

        [Fact]
        public void 変化検知_片方だけnullの要素は変化とみなす()
        {
            var x = Src(PlacedObjectCategory.Png, 40, "a");

            Assert.False(PlacedObjectTree.SameSources(new[] { x }, new PlacedObjectSource[] { null }));
            Assert.False(PlacedObjectTree.SameSources(new PlacedObjectSource[] { null }, new[] { x }));
            Assert.True(PlacedObjectTree.SameSources(new PlacedObjectSource[] { null }, new PlacedObjectSource[] { null }));
        }

        [Fact]
        public void nullの要素は読み飛ばして残りを出す()
        {
            var tree = PlacedObjectTree.Build(new[]
            {
                null,
                Src(PlacedObjectCategory.Png, 40, "a"),
            });

            Assert.True(tree.Contains(40));
            Assert.Single(tree.roots);
            Assert.Single(tree.roots[0].children);
        }
    }
}
