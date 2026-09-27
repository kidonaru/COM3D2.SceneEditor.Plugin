using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>現在のシェーダー変更と未適用の保留の併合を固定する</summary>
    public class MaterialShaderSyncTests
    {
        private static TimelineMaterialShaderData Entry(
            int maidSlotNo, string owner, string material, int index, string shader)
        {
            return new TimelineMaterialShaderData
            {
                maidSlotNo = maidSlotNo, owner = owner, material = material, index = index, shader = shader,
            };
        }

        [Fact]
        public void 同じ対象では現在の状態が勝つ()
        {
            var live = new List<TimelineMaterialShaderData> { Entry(0, "wear", "a", 0, "CM3D2/Lighted") };
            var pending = new List<TimelineMaterialShaderData> { Entry(0, "wear", "a", 0, "com3d2mod/X") };

            var merged = MaterialShaderSync.Merge(live, pending);

            Assert.Single(merged);
            Assert.Equal("CM3D2/Lighted", merged[0].shader);
        }

        [Fact]
        public void 保留は現在の状態と重ならなければ残る()
        {
            var live = new List<TimelineMaterialShaderData> { Entry(0, "wear", "a", 0, "CM3D2/Lighted") };
            var pending = new List<TimelineMaterialShaderData> { Entry(-1, "Model1", "m", 0, "com3d2mod/X") };

            var merged = MaterialShaderSync.Merge(live, pending);

            Assert.Equal(2, merged.Count);
        }

        [Fact]
        public void メイド番号_所有者_マテリアル_位置の順に並べる()
        {
            var live = new List<TimelineMaterialShaderData>
            {
                Entry(1, "wear", "a", 0, "s"),
                Entry(0, "wear", "b", 0, "s"),
                Entry(0, "wear", "a", 1, "s"),
                Entry(-1, "Model1", "m", 0, "s"),
                Entry(0, "body", "z", 0, "s"),
            };

            var merged = MaterialShaderSync.Merge(live, new List<TimelineMaterialShaderData>());

            Assert.Equal(-1, merged[0].maidSlotNo);
            Assert.Equal("body", merged[1].owner);
            Assert.Equal("a", merged[2].material);
            Assert.Equal("b", merged[3].material);
            Assert.Equal(1, merged[4].maidSlotNo);
        }

        [Fact]
        public void 並びと中身が同じなら等しい()
        {
            var a = new List<TimelineMaterialShaderData> { Entry(0, "wear", "a", 0, "s") };
            var b = new List<TimelineMaterialShaderData> { Entry(0, "wear", "a", 0, "s") };
            var c = new List<TimelineMaterialShaderData> { Entry(0, "wear", "a", 0, "t") };

            Assert.True(MaterialShaderSync.ListEquals(a, b));
            Assert.False(MaterialShaderSync.ListEquals(a, c));
            Assert.False(MaterialShaderSync.ListEquals(a, new List<TimelineMaterialShaderData>()));
        }

        [Fact]
        public void 作り直されたエントリは保留へ戻る()
        {
            var pending = new List<TimelineMaterialShaderData>();
            var live = new List<TimelineMaterialShaderData>();

            Assert.True(MaterialShaderSync.Requeue(pending, live, Entry(0, "wear", "a", 0, "s")));
            Assert.Single(pending);
        }

        [Fact]
        public void 同じ対象が現在か保留にあれば保留へ戻さない()
        {
            var pending = new List<TimelineMaterialShaderData> { Entry(0, "wear", "a", 0, "old") };
            var live = new List<TimelineMaterialShaderData> { Entry(0, "wear", "b", 0, "s") };

            Assert.False(MaterialShaderSync.Requeue(pending, live, Entry(0, "wear", "a", 0, "new")));
            Assert.False(MaterialShaderSync.Requeue(pending, live, Entry(0, "wear", "b", 0, "new")));
            Assert.Single(pending);
            Assert.Equal("old", pending[0].shader);
        }

        [Fact]
        public void 併合でもテクスチャは現在の状態が勝つ()
        {
            var liveEntry = Entry(0, "body", "skin", 0, "");
            liveEntry.textures.Add(new MaterialTextureOverride("_ToonRamp", "Toon/a.png"));
            var pendingEntry = Entry(0, "body", "skin", 0, "");
            pendingEntry.textures.Add(new MaterialTextureOverride("_ToonRamp", "Toon/b.png"));

            var merged = MaterialShaderSync.Merge(
                new List<TimelineMaterialShaderData> { liveEntry },
                new List<TimelineMaterialShaderData> { pendingEntry });

            Assert.Single(merged);
            Assert.Equal("Toon/a.png", merged[0].textures[0].file);
        }
    }
}
