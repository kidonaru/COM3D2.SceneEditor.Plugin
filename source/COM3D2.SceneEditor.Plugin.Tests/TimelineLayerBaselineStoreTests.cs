using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineLayerBaselineStoreTests
    {
        private static FrameXml CreateFrame(params string[] boneNames)
        {
            return new FrameXml
            {
                frameNo = 0,
                bones = boneNames
                    .Select(name => new BoneXml { transform = new TransformXml { name = name } })
                    .ToList(),
            };
        }

        private static List<string> NamesOf(FrameXml frame)
        {
            return frame.bones.Select(bone => bone.transform.name).ToList();
        }

        [Fact]
        public void 記録した断面をそのまま取り出せる()
        {
            var store = new TimelineLayerBaselineStore();
            var frame = CreateFrame("Light0");

            store.Set(typeof(LightTimelineLayer), 0, frame);

            FrameXml actual;
            Assert.True(store.TryGet(typeof(LightTimelineLayer), 0, out actual));
            Assert.Same(frame, actual);
        }

        [Fact]
        public void スロット番号が違えば別の断面になる()
        {
            var store = new TimelineLayerBaselineStore();
            var frame0 = CreateFrame("A");
            var frame1 = CreateFrame("B");

            store.Set(typeof(MorphTimelineLayer), 0, frame0);
            store.Set(typeof(MorphTimelineLayer), 1, frame1);

            FrameXml actual;
            Assert.True(store.TryGet(typeof(MorphTimelineLayer), 1, out actual));
            Assert.Same(frame1, actual);
        }

        [Fact]
        public void Setは同じキーを上書きする()
        {
            var store = new TimelineLayerBaselineStore();
            var older = CreateFrame("A");
            var newer = CreateFrame("B");

            store.Set(typeof(LightTimelineLayer), 0, older);
            store.Set(typeof(LightTimelineLayer), 0, newer);

            FrameXml actual;
            Assert.True(store.TryGet(typeof(LightTimelineLayer), 0, out actual));
            Assert.Same(newer, actual);
            Assert.Equal(1, store.count);
        }

        [Fact]
        public void 未記録のキーは取り出せない()
        {
            var store = new TimelineLayerBaselineStore();
            store.Set(typeof(LightTimelineLayer), 0, CreateFrame("A"));

            FrameXml actual;
            Assert.False(store.TryGet(typeof(MorphTimelineLayer), 0, out actual));
            Assert.Null(actual);
            Assert.False(store.TryGet(typeof(LightTimelineLayer), 1, out actual));
        }

        [Fact]
        public void nullは記録しない()
        {
            var store = new TimelineLayerBaselineStore();

            store.Set(null, 0, CreateFrame("A"));
            store.Set(typeof(LightTimelineLayer), 0, null);

            Assert.Equal(0, store.count);

            FrameXml actual;
            Assert.False(store.TryGet(null, 0, out actual));
        }

        [Fact]
        public void クリアで全件消える()
        {
            var store = new TimelineLayerBaselineStore();
            store.Set(typeof(LightTimelineLayer), 0, CreateFrame("A"));
            store.Set(typeof(MorphTimelineLayer), 1, CreateFrame("B"));

            store.Clear();

            Assert.Equal(0, store.count);
            FrameXml actual;
            Assert.False(store.TryGet(typeof(LightTimelineLayer), 0, out actual));
        }

        [Fact]
        public void Mergeは指定した未記録のボーンだけ足す()
        {
            var store = new TimelineLayerBaselineStore();
            store.Set(typeof(MorphTimelineLayer), 0, CreateFrame("hoho"));

            // 追加指定は eyeclose だけ。mayu は指定外なので足さない
            store.Merge(typeof(MorphTimelineLayer), 0,
                CreateFrame("hoho", "eyeclose", "mayu"), new List<string> { "eyeclose" });

            FrameXml actual;
            Assert.True(store.TryGet(typeof(MorphTimelineLayer), 0, out actual));
            Assert.Equal(new List<string> { "hoho", "eyeclose" }, NamesOf(actual));
        }

        [Fact]
        public void Mergeは記録済みのボーンを上書きしない()
        {
            var store = new TimelineLayerBaselineStore();
            var original = CreateFrame("hoho");
            var originalBone = original.bones[0];
            store.Set(typeof(MorphTimelineLayer), 0, original);

            store.Merge(typeof(MorphTimelineLayer), 0,
                CreateFrame("hoho"), new List<string> { "hoho" });

            FrameXml actual;
            Assert.True(store.TryGet(typeof(MorphTimelineLayer), 0, out actual));
            Assert.Single(actual.bones);
            Assert.Same(originalBone, actual.bones[0]);
        }

        [Fact]
        public void 断面が無いキーへのMergeは指定分だけで新規作成する()
        {
            var store = new TimelineLayerBaselineStore();

            store.Merge(typeof(MorphTimelineLayer), 0,
                CreateFrame("hoho", "eyeclose"), new List<string> { "eyeclose" });

            FrameXml actual;
            Assert.True(store.TryGet(typeof(MorphTimelineLayer), 0, out actual));
            Assert.Equal(new List<string> { "eyeclose" }, NamesOf(actual));
        }

        [Fact]
        public void Mergeは一致するボーンが無ければ断面を作らない()
        {
            var store = new TimelineLayerBaselineStore();

            store.Merge(typeof(MorphTimelineLayer), 0,
                CreateFrame("hoho"), new List<string> { "mayu" });

            Assert.Equal(0, store.count);
        }

        [Fact]
        public void Mergeは空指定や欠損入力で何もしない()
        {
            var store = new TimelineLayerBaselineStore();

            store.Merge(typeof(MorphTimelineLayer), 0, CreateFrame("A"), new List<string>());
            store.Merge(typeof(MorphTimelineLayer), 0, null, new List<string> { "A" });
            store.Merge(typeof(MorphTimelineLayer), 0, CreateFrame("A"), null);
            store.Merge(null, 0, CreateFrame("A"), new List<string> { "A" });

            Assert.Equal(0, store.count);
        }

        [Fact]
        public void 未記録の対象数はマイナス1を返す()
        {
            var store = new TimelineLayerBaselineStore();

            Assert.Equal(-1, store.GetCoverage(typeof(LightTimelineLayer), 0));
            Assert.Equal(-1, store.GetCoverage(null, 0));
        }

        [Fact]
        public void 対象数はキーごとに記録される()
        {
            var store = new TimelineLayerBaselineStore();

            store.SetCoverage(typeof(LightTimelineLayer), 0, 3);
            store.SetCoverage(typeof(LightTimelineLayer), 1, 5);
            store.SetCoverage(typeof(MorphTimelineLayer), 0, 7);
            store.SetCoverage(null, 0, 9);

            Assert.Equal(3, store.GetCoverage(typeof(LightTimelineLayer), 0));
            Assert.Equal(5, store.GetCoverage(typeof(LightTimelineLayer), 1));
            Assert.Equal(7, store.GetCoverage(typeof(MorphTimelineLayer), 0));
            Assert.Equal(-1, store.GetCoverage(typeof(MorphTimelineLayer), 1));
        }

        [Fact]
        public void 対象数は上書きできる()
        {
            var store = new TimelineLayerBaselineStore();

            store.SetCoverage(typeof(LightTimelineLayer), 0, 3);
            store.SetCoverage(typeof(LightTimelineLayer), 0, 1);

            Assert.Equal(1, store.GetCoverage(typeof(LightTimelineLayer), 0));
        }

        [Fact]
        public void クリアで対象数も消える()
        {
            var store = new TimelineLayerBaselineStore();
            store.SetCoverage(typeof(LightTimelineLayer), 0, 3);

            store.Clear();

            Assert.Equal(-1, store.GetCoverage(typeof(LightTimelineLayer), 0));
        }
    }
}
