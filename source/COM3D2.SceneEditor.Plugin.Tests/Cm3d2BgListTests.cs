using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// CM3D2 の phot_bg_list.nei の行から背景一覧を作る規則を固定する。
    /// ゲームの PhotoBGData.Create とほぼ同じ絞り込みに加え、2.5 の一覧との重複を除く。
    /// ゲームは有効 ID リストが読めないと全行を弾くが、ここでは空なら絞り込まない (本家と異なる)
    /// </summary>
    public class Cm3d2BgListTests
    {
        private static readonly string[] NoExisting = new string[0];

        private static bool AllPacks(string pack)
        {
            return true;
        }

        private static string[] Row(string id, string category, string name, string prefab, string pack = "")
        {
            return new[] { id, category, name, prefab, pack };
        }

        [Fact]
        public void 行_列を項目へ写しカテゴリにCM3D2を付ける()
        {
            var rows = new List<string[]> { Row("200", "夜伽", "サロン:昼", "Salon_Day") };

            var entries = Cm3d2BgList.Build(rows, null, AllPacks, NoExisting);

            Assert.Single(entries);
            Assert.Equal("CM3D2:夜伽", entries[0].category);
            Assert.Equal("サロン:昼", entries[0].name);
            Assert.Equal("Salon_Day", entries[0].prefabName);
        }

        [Fact]
        public void 行_表示名が空ならprefab名を表示名にする()
        {
            var rows = new List<string[]> { Row("1", "一般", "", "Pool") };

            var entries = Cm3d2BgList.Build(rows, null, AllPacks, NoExisting);

            Assert.Equal("Pool", entries[0].name);
        }

        [Fact]
        public void 行_prefab名が空の行は出さない()
        {
            var rows = new List<string[]> { Row("1", "一般", "名前だけ", "") };

            Assert.Empty(Cm3d2BgList.Build(rows, null, AllPacks, NoExisting));
        }

        [Fact]
        public void 行_IDが数値でない行は出さない()
        {
            var rows = new List<string[]> { Row("abc", "一般", "A", "PrefabA"), Row("", "一般", "B", "PrefabB") };

            Assert.Empty(Cm3d2BgList.Build(rows, null, AllPacks, NoExisting));
        }

        [Fact]
        public void 行_列が足りない行も落ちずに必要パックなしとして扱う()
        {
            var rows = new List<string[]> { new[] { "1", "一般", "A", "PrefabA" } };

            var entries = Cm3d2BgList.Build(rows, null, pack => false, NoExisting);

            Assert.Single(entries);
        }

        [Fact]
        public void 行_nullの行と空の入力は無視する()
        {
            Assert.Empty(Cm3d2BgList.Build(null, null, AllPacks, NoExisting));
            Assert.Empty(Cm3d2BgList.Build(new List<string[]> { null }, null, AllPacks, NoExisting));
        }

        [Fact]
        public void 重複_2点5と同じprefab名は出さない()
        {
            var rows = new List<string[]>
            {
                Row("1", "一般", "サロン", "Salon"),
                Row("2", "一般", "プール", "Pool"),
            };

            var entries = Cm3d2BgList.Build(rows, null, AllPacks, new[] { "Salon", "" , null });

            Assert.Single(entries);
            Assert.Equal("Pool", entries[0].prefabName);
        }

        [Fact]
        public void 重複_2点5と大文字小文字違いのprefab名は出さない()
        {
            // BgMgr.CreateAssetBundle は小文字化して引くため同じ背景になる (実機: SMRoom2 ⇔ smroom2)
            var rows = new List<string[]>
            {
                Row("1", "夜伽", "地下室", "SMRoom2"),
                Row("2", "一般", "電車", "Train"),
            };

            Assert.Empty(Cm3d2BgList.Build(rows, null, AllPacks, new[] { "smroom2", "train" }));
        }

        [Fact]
        public void 重複_CM3D2内で重なるprefab名は先の行だけ残す()
        {
            var rows = new List<string[]>
            {
                Row("1", "一般", "先", "Pool"),
                Row("2", "夜伽", "後", "pool"),
            };

            var entries = Cm3d2BgList.Build(rows, null, AllPacks, NoExisting);

            Assert.Single(entries);
            Assert.Equal("先", entries[0].name);
        }

        [Fact]
        public void 有効ID_リストに無い行は出さない()
        {
            var rows = new List<string[]> { Row("1", "一般", "A", "PrefabA"), Row("2", "一般", "B", "PrefabB") };

            var entries = Cm3d2BgList.Build(rows, new HashSet<int> { 2 }, AllPacks, NoExisting);

            Assert.Single(entries);
            Assert.Equal("PrefabB", entries[0].prefabName);
        }

        [Fact]
        public void 有効ID_リストが空なら絞り込まない()
        {
            // 有効 ID リストが読めない環境で CM3D2 の背景が全部消えないようにする
            var rows = new List<string[]> { Row("1", "一般", "A", "PrefabA") };

            Assert.Single(Cm3d2BgList.Build(rows, new HashSet<int>(), AllPacks, NoExisting));
        }

        [Fact]
        public void 必要パック_未導入の行は出さない()
        {
            var rows = new List<string[]>
            {
                Row("1", "一般", "A", "PrefabA", "pack_a"),
                Row("2", "一般", "B", "PrefabB", "pack_b"),
            };

            var entries = Cm3d2BgList.Build(rows, null, pack => pack == "pack_b", NoExisting);

            Assert.Single(entries);
            Assert.Equal("PrefabB", entries[0].prefabName);
        }

        [Fact]
        public void 必要パック_空なら判定しない()
        {
            var called = false;
            var rows = new List<string[]> { Row("1", "一般", "A", "PrefabA", "") };

            var entries = Cm3d2BgList.Build(rows, null, pack => { called = true; return false; }, NoExisting);

            Assert.Single(entries);
            Assert.False(called);
        }

        [Fact]
        public void 検索_prefab名は大文字小文字を無視して引く()
        {
            var entries = new List<Cm3d2BgEntry> { new Cm3d2BgEntry("CM3D2:一般", "プール", "Pool") };

            Assert.Same(entries[0], Cm3d2BgList.Find(entries, "pool"));
            Assert.Null(Cm3d2BgList.Find(entries, "Salon"));
            Assert.Null(Cm3d2BgList.Find(entries, null));
            Assert.Null(Cm3d2BgList.Find(null, "Pool"));
        }

        [Fact]
        public void カテゴリ_出現順で重複なく並べる()
        {
            var entries = new List<Cm3d2BgEntry>
            {
                new Cm3d2BgEntry("CM3D2:夜伽", "A", "PrefabA"),
                new Cm3d2BgEntry("CM3D2:一般", "B", "PrefabB"),
                new Cm3d2BgEntry("CM3D2:夜伽", "C", "PrefabC"),
            };

            Assert.Equal(new[] { "CM3D2:夜伽", "CM3D2:一般" }, Cm3d2BgList.GetCategories(entries));
        }

        [Fact]
        public void カテゴリ_接頭辞は2点5のカテゴリと衝突しない()
        {
            Assert.Equal("CM3D2:昼", Cm3d2BgList.ToCategoryName("昼"));
            Assert.Equal("CM3D2:", Cm3d2BgList.ToCategoryName(null));
        }
    }
}
