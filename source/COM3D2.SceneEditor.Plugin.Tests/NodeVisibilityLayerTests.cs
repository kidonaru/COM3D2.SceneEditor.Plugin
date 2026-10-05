using System.Collections.Generic;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>ノード表示レイヤーのキー形式を固定する。値の並びはタイムライン XML の保存形式</summary>
    public class NodeVisibilityLayerTests
    {
        private static MTEP.TransformDataNodeVisibility CreateTransform()
        {
            var trans = new MTEP.TransformDataNodeVisibility();
            trans.Initialize("Mune_L");
            return trans;
        }

        [Fact]
        public void キーは表示1値でBool型()
        {
            var trans = CreateTransform();
            Assert.Equal(MTEP.TransformType.NodeVisibility, trans.type);
            Assert.Equal(1, trans.valueCount);
            Assert.Equal(0, (int)MTEP.TransformDataNodeVisibility.Index.IsVisible);
            Assert.Equal(MTEP.CustomValueType.BoolValue,
                trans.GetCustomValueInfoMap()["isVisible"].type);
        }

        [Fact]
        public void 新しいキーの既定は表示()
        {
            Assert.True(CreateTransform().isVisible);
        }

        [Fact]
        public void 表示フラグは値配列へ書き戻される()
        {
            var trans = CreateTransform();
            trans.isVisible = false;

            Assert.False(trans.isVisibleValue.boolValue);
            Assert.Equal(0f, trans.values[0].value);
        }

        [Fact]
        public void キーは上書きのあるノードとキー済みノードだけを定義順で作る()
        {
            var overrides = new Dictionary<string, bool> { { "Mune_R", true } };
            var keyed = new List<string> { "Mune_L", "Mune_R" };

            var keys = MTEP.NodeVisibilityTimelineLayer.BuildKeys(
                overrides, keyed, node => node != "Mune_L");

            Assert.Equal(2, keys.Count);
            Assert.Equal("Mune_L", keys[0].Key);
            Assert.False(keys[0].Value);
            Assert.Equal("Mune_R", keys[1].Key);
            Assert.True(keys[1].Value);
        }

        [Fact]
        public void 上書きもキーも無いノードはキーにしない()
        {
            var keys = MTEP.NodeVisibilityTimelineLayer.BuildKeys(
                new Dictionary<string, bool>(), new List<string>(), _ => false);

            Assert.Empty(keys);
        }

        [Fact]
        public void 履歴スコープはメイド必須()
        {
            Assert.True(HistoryScopeUtils.RequiresMaid(HistoryScope.NodeVisibility));
        }

        [Fact]
        public void Inspectorは項目名からノードを引き未知の名前ではnullを返す()
        {
            Assert.Equal("左胸下", NodeVisibilityItemInspector.ResolveNode("Mune_L").displayName);
            Assert.Null(NodeVisibilityItemInspector.ResolveNode("unknown"));
        }
    }
}
