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
        public void 登録項目は定義順に並べ未知の名前は捨てる()
        {
            var names = MTEP.BodySliderTimelineLayer.OrderByDefinition(
                new List<string> { "HANDSCL_R", "UNKNOWN", "THISCL", "MUNEPOS" });

            Assert.Equal(new[] { "THISCL", "MUNEPOS", "HANDSCL_R" }, names.ToArray());
        }

        [Fact]
        public void 登録が無ければ項目も無い()
        {
            Assert.Empty(MTEP.BodySliderTimelineLayer.OrderByDefinition(new List<string>()));
        }

        private static MTEP.BoneData CreateRow(int frameNo, string key, Vector3 values)
        {
            var frame = new MTEP.FrameData(null, frameNo);
            var trans = CreateKey(key);
            trans.vector = values;
            var bone = frame.CreateBone(trans);
            frame.SetBone(bone);
            return bone;
        }

        [Fact]
        public void 最初のキーが0Fより後の項目には0Fの既定値の行を補う()
        {
            var rowsMap = new Dictionary<string, List<MTEP.BoneData>>
            {
                { "UPARMSCL_L", new List<MTEP.BoneData> { CreateRow(60, "UPARMSCL_L", new Vector3(2f, 2f, 2f)) } },
                { "THIPOS", new List<MTEP.BoneData> { CreateRow(30, "THIPOS", new Vector3(10f, 0f, 0f)) } },
            };

            MTEP.BodySliderTimelineLayer.PrependDefaultFirstRows(rowsMap, new MTEP.FrameData(null, 0));

            var arm = rowsMap["UPARMSCL_L"];
            Assert.Equal(2, arm.Count);
            Assert.Equal(0, arm[0].frameNo);
            Assert.Equal(Vector3.one, ((MTEP.TransformDataBodySlider)arm[0].transform).vector);
            Assert.Equal(60, arm[1].frameNo);
            Assert.Equal(Vector3.zero, ((MTEP.TransformDataBodySlider)rowsMap["THIPOS"][0].transform).vector);
        }

        [Fact]
        public void 先頭フレームにキーのある項目と未知の項目には行を補わない()
        {
            var rowsMap = new Dictionary<string, List<MTEP.BoneData>>
            {
                { "THISCL", new List<MTEP.BoneData> { CreateRow(0, "THISCL", new Vector3(1.5f, 1f, 1f)), CreateRow(60, "THISCL", Vector3.one) } },
                { "UNKNOWN", new List<MTEP.BoneData> { CreateRow(60, "UNKNOWN", Vector3.one) } },
            };

            MTEP.BodySliderTimelineLayer.PrependDefaultFirstRows(rowsMap, new MTEP.FrameData(null, 0));

            Assert.Equal(2, rowsMap["THISCL"].Count);
            Assert.Equal(1.5f, ((MTEP.TransformDataBodySlider)rowsMap["THISCL"][0].transform).vector.x);
            Assert.Single(rowsMap["UNKNOWN"]);
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
