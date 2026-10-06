using System.Collections.Generic;
using UnityEngine;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>体型スライダーレイヤーのキー形式を固定する。値の並びはタイムライン XML の保存形式</summary>
    public class BodySliderLayerTests
    {
        private static MTEP.TransformDataBodySlider CreateKey(string key)
        {
            var trans = new MTEP.TransformDataBodySlider();
            trans.Initialize(key);
            return trans;
        }

        [Fact]
        public void キーは3値でタンジェント補間する()
        {
            var trans = CreateKey("THISCL");
            Assert.Equal(MTEP.TransformType.BodySlider, trans.type);
            Assert.Equal(3, trans.valueCount);
            Assert.Equal(3, trans.tangentValues.Length);
        }

        [Fact]
        public void スケールの既定は1で位置の既定は0()
        {
            Assert.Equal(Vector3.one, CreateKey("THISCL").vector);
            Assert.True(CreateKey("THISCL").isDefault);
            Assert.Equal(Vector3.zero, CreateKey("THIPOS").vector);
        }

        [Fact]
        public void カスタム値は項目の成分名と範囲を持つ()
        {
            var map = CreateKey("THIPOS").GetCustomValueInfoMap();
            Assert.Equal(3, map.Count);
            Assert.Equal(0, map["x"].index);
            Assert.Equal(-100f, map["x"].min);
            Assert.Equal(200f, map["x"].max);
            Assert.Equal(0f, map["x"].defaultValue);
            Assert.Equal(2, CreateKey("THISCL").GetCustomValueInfoMap()["height"].index);
        }

        [Fact]
        public void 未知の項目名はカスタム値を持たない()
        {
            Assert.Empty(CreateKey("UNKNOWN").GetCustomValueInfoMap());
        }

        [Fact]
        public void リセットしたキーは項目の既定値に戻る()
        {
            var trans = CreateKey("UPARMSCL_L");
            trans.vector = new Vector3(2f, 2f, 2f);
            Assert.False(trans.isDefault);

            trans.Reset();
            Assert.Equal(Vector3.one, trans.vector);
        }

        [Fact]
        public void XMLの往復で値を保つ()
        {
            var trans = CreateKey("MUNEPOS");
            trans.vector = new Vector3(0.1f, -0.2f, 0.3f);

            var loaded = CreateKey("MUNEPOS");
            loaded.FromXml(trans.ToXml());

            Assert.Equal(new Vector3(0.1f, -0.2f, 0.3f), loaded.vector);
            Assert.Equal("MUNEPOS", loaded.name);
        }

        [Fact]
        public void キーは既定でない項目とキー済み項目だけを定義順で作る()
        {
            var names = MTEP.BodySliderTimelineLayer.BuildKeyNames(
                new List<string> { "HANDSCL_R", "THISCL" },
                new List<string> { "THIPOS" });

            Assert.Equal(new[] { "THISCL", "THIPOS", "HANDSCL_R" }, names.ToArray());
        }

        [Fact]
        public void 値もキーも無ければキーにしない()
        {
            Assert.Empty(MTEP.BodySliderTimelineLayer.BuildKeyNames(new List<string>(), new List<string>()));
        }

        [Fact]
        public void 履歴スコープはメイド必須()
        {
            Assert.True(HistoryScopeUtils.RequiresMaid(HistoryScope.BodySlider));
        }

        [Fact]
        public void Inspectorは項目名から定義を引き未知の名前ではnullを返す()
        {
            Assert.Equal("上腕スケーリング(左)", BodySliderItemInspector.ResolveItem("UPARMSCL_L").displayName);
            Assert.Null(BodySliderItemInspector.ResolveItem("unknown"));
        }
    }
}
