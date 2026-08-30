using System.Collections.Generic;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 集約ストアの作り直し判定を固定する。
    /// モデル修飾名は group 振り直し (ModelHackManager.FixGroup) で変わりうるため、
    /// 「記録の version が動かない名前変更」にも Invalidate 経由で追従できなければならない
    /// </summary>
    public class ModelTrackedNameStoreTests
    {
        /// <summary>テスト用の疑似モデル。キーは string、生名と version を自分で持つ</summary>
        private class FakeModel
        {
            public string prefix;
            public List<string> rawNames = new List<string>();
            public int version;
            public bool resolvable = true;
        }

        private readonly Dictionary<string, FakeModel> _models = new Dictionary<string, FakeModel>();
        private readonly List<string> _keys = new List<string>();

        private FakeModel AddModel(string key, string prefix, params string[] rawNames)
        {
            var model = new FakeModel { prefix = prefix };
            model.rawNames.AddRange(rawNames);
            _models[key] = model;
            _keys.Add(key);
            return model;
        }

        private void Sync(ModelTrackedNameStore<string> tracked)
        {
            tracked.Sync(
                _keys,
                key => _models[key].version,
                (key, result) =>
                {
                    var model = _models[key];
                    if (!model.resolvable)
                    {
                        return false;
                    }
                    foreach (var name in model.rawNames)
                    {
                        result.Add(model.prefix + "/" + name);
                    }
                    return true;
                });
        }

        [Fact]
        public void 初回のSyncで修飾名が集約される()
        {
            AddModel("a", "model.menu", "Key1", "Key2");
            var tracked = new ModelTrackedNameStore<string>();

            Sync(tracked);

            Assert.True(tracked.store.IsModified("model.menu/Key1"));
            Assert.True(tracked.store.IsModified("model.menu/Key2"));
        }

        [Fact]
        public void versionが変わると集約し直す()
        {
            var model = AddModel("a", "model.menu", "Key1");
            var tracked = new ModelTrackedNameStore<string>();
            Sync(tracked);

            model.rawNames.Add("Key2");
            model.version++;
            Sync(tracked);

            Assert.True(tracked.store.IsModified("model.menu/Key2"));
        }

        [Fact]
        public void versionが同じなら集約し直さない()
        {
            var model = AddModel("a", "model.menu", "Key1");
            var tracked = new ModelTrackedNameStore<string>();
            Sync(tracked);

            // version を動かさずに名前だけ変える (group 振り直し相当)
            model.prefix = "model.menu (2)";
            Sync(tracked);

            Assert.True(tracked.store.IsModified("model.menu/Key1"));
            Assert.False(tracked.store.IsModified("model.menu (2)/Key1"));
        }

        [Fact]
        public void Invalidateすればversionが同じでも集約し直す()
        {
            var model = AddModel("a", "model.menu", "Key1");
            var tracked = new ModelTrackedNameStore<string>();
            Sync(tracked);

            model.prefix = "model.menu (2)";
            tracked.Invalidate();
            Sync(tracked);

            Assert.True(tracked.store.IsModified("model.menu (2)/Key1"));
            Assert.False(tracked.store.IsModified("model.menu/Key1"));
        }

        [Fact]
        public void 未解決モデルは一定フレーム後に再試行される()
        {
            var model = AddModel("a", "model.menu", "Key1");
            model.resolvable = false;
            var tracked = new ModelTrackedNameStore<string>();
            Sync(tracked);
            Assert.False(tracked.store.IsModified("model.menu/Key1"));

            // 名前が解決できるようになっても version は動かない (タイムラインのロード相当)
            model.resolvable = true;
            for (var i = 0; i < ModelTrackedNameStore<string>.RetryInterval; i++)
            {
                Sync(tracked);
            }

            Assert.True(tracked.store.IsModified("model.menu/Key1"));
        }

        [Fact]
        public void 解決できたモデルだけが集約される()
        {
            AddModel("a", "model.menu", "Key1");
            var unresolved = AddModel("b", "other.menu", "Key2");
            unresolved.resolvable = false;
            var tracked = new ModelTrackedNameStore<string>();

            Sync(tracked);

            Assert.True(tracked.store.IsModified("model.menu/Key1"));
            Assert.False(tracked.store.IsModified("other.menu/Key2"));
        }

        [Fact]
        public void Clearで集約が空になる()
        {
            AddModel("a", "model.menu", "Key1");
            var tracked = new ModelTrackedNameStore<string>();
            Sync(tracked);

            tracked.Clear();

            Assert.True(tracked.store.isEmpty);
        }
    }
}
