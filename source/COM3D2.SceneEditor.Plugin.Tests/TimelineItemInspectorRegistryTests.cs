using System;
using System.Collections.Generic;
using COM3D2.SceneEditor.Plugin;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class TimelineItemInspectorRegistryTests
    {
        /// <summary>テスト用の最小メニュー項目。描画系メソッドは使わない</summary>
        private class FakeMenuItem : MTEP.IBoneMenuItem
        {
            public string name { get; set; }
            public string displayName { get; set; }
            public bool isSelectedMenu { get; set; }
            public bool isVisibleMenu { get; set; }
            public bool isOpenMenu { get; set; }
            public bool isSetMenu => children != null;
            public MTEP.IBoneMenuItem parent { get; set; }
            public List<MTEP.IBoneMenuItem> children { get; set; }

            public void SelectMenu(bool isMultiSelect) { }
            public bool HasVisibleBone(MTEP.FrameData frame) => false;
            public bool IsFullBones(MTEP.FrameData frame) => false;
            public bool IsTargetBone(MTEP.BoneData bone) => false;
            public bool IsSelectedFrame(MTEP.FrameData frame) => false;
            public void SelectFrame(MTEP.FrameData frame, bool isMultiSelect) { }
            public void AddKey() { }
            public void RemoveKey() { }
        }

        private class FakeInspector : ITimelineItemInspector
        {
            public void DrawItems(
                COM3D2.MotionTimelineEditor.GUIView view,
                MTEP.ITimelineLayer layer,
                IList<MTEP.IBoneMenuItem> items) { }
            public string FindItemName(MTEP.ITimelineLayer layer) => null;
        }

        [Fact]
        public void Register_と_Find_で登録したプロバイダを型で引ける()
        {
            var inspector = new FakeInspector();
            TimelineItemInspectorRegistry.Register(typeof(string), inspector);
            Assert.Same(inspector, TimelineItemInspectorRegistry.Find(typeof(string)));
            Assert.Null(TimelineItemInspectorRegistry.Find(typeof(int)));
        }

        [Fact]
        public void Register_同じ型の再登録は置き換える()
        {
            var first = new FakeInspector();
            var second = new FakeInspector();
            TimelineItemInspectorRegistry.Register(typeof(double), first);
            TimelineItemInspectorRegistry.Register(typeof(double), second);
            Assert.Same(second, TimelineItemInspectorRegistry.Find(typeof(double)));
        }

        [Fact]
        public void CollectLeafItems_セット行は子へ展開し重複を除いて葉だけ返す()
        {
            var childA = new FakeMenuItem { name = "a" };
            var childB = new FakeMenuItem { name = "b" };
            var set = new FakeMenuItem
            {
                name = "set",
                children = new List<MTEP.IBoneMenuItem> { childA, childB },
            };
            var single = new FakeMenuItem { name = "c" };

            // GetSelectedItems はセットと子が両方含まれる形で返すので、その形を入力にする
            var source = new List<MTEP.IBoneMenuItem> { set, childA, childB, single };
            var result = new List<MTEP.IBoneMenuItem>();
            TimelineItemInspectorRegistry.CollectLeafItems(source, result);

            Assert.Equal(new MTEP.IBoneMenuItem[] { childA, childB, single }, result);
        }

        [Fact]
        public void CollectLeafItems_呼び出しごとに結果リストをクリアする()
        {
            var item = new FakeMenuItem { name = "a" };
            var result = new List<MTEP.IBoneMenuItem> { new FakeMenuItem { name = "stale" } };
            TimelineItemInspectorRegistry.CollectLeafItems(
                new List<MTEP.IBoneMenuItem> { item }, result);
            Assert.Equal(new MTEP.IBoneMenuItem[] { item }, result);
        }
    }
}
