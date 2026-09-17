using System.Collections.Generic;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// BoneEditStore の version はエントリ集合の増減だけを表す。
    /// タイムライン側の追跡集合の再構築判定に使うため、
    /// 値だけの変更で進んだり、集合が変わったのに進まなかったりしてはいけない。
    /// Transform を要する経路 (RecordEdit / ResetBone) は Unity ランタイムが無いため実機確認で担保する
    /// </summary>
    public class BoneEditStoreVersionTests
    {
        private static BoneEditEntry CreateEntry(string slotName, string boneName)
        {
            return new BoneEditEntry
            {
                slotName = slotName,
                itemFileName = "test.menu",
                boneName = boneName,
            };
        }

        [Fact]
        public void 初期状態のversionは0()
        {
            var store = new BoneEditStore();
            Assert.Equal(0, store.version);
        }

        [Fact]
        public void RestoreEntriesでversionが進む()
        {
            var store = new BoneEditStore();
            store.RestoreEntries(new List<BoneEditEntry> { CreateEntry("body", "Bip01") });
            Assert.True(store.version > 0);
        }

        [Fact]
        public void 空のClearではversionが進まない()
        {
            var store = new BoneEditStore();
            var before = store.version;
            store.Clear();
            Assert.Equal(before, store.version);
        }

        [Fact]
        public void 中身のあるClearでversionが進む()
        {
            var store = new BoneEditStore();
            store.RestoreEntries(new List<BoneEditEntry> { CreateEntry("body", "Bip01") });
            var before = store.version;
            store.Clear();
            Assert.True(store.version > before);
        }

        [Fact]
        public void アイテム変更でスロットを捨てるとversionが進む()
        {
            var store = new BoneEditStore();
            store.RestoreEntries(new List<BoneEditEntry> { CreateEntry("body", "Bip01") });
            var before = store.version;
            store.DiscardSlotIfItemChanged("body", "other.menu");
            Assert.True(store.version > before);
            Assert.Empty(store.GetEntries("body"));
        }

        [Fact]
        public void アイテムが同じならDiscardでversionが進まない()
        {
            var store = new BoneEditStore();
            store.RestoreEntries(new List<BoneEditEntry> { CreateEntry("body", "Bip01") });
            var before = store.version;
            store.DiscardSlotIfItemChanged("body", "test.menu");
            Assert.Equal(before, store.version);
        }
    }
}
