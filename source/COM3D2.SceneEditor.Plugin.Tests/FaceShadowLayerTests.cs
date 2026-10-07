using System.Collections.Generic;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>顔の影レイヤーのキー形式を固定する。値の並びはタイムライン XML の保存形式</summary>
    public class FaceShadowLayerTests
    {
        private static MTEP.TransformDataFaceShadow CreateTransform(string name)
        {
            var trans = new MTEP.TransformDataFaceShadow();
            trans.Initialize(name);
            return trans;
        }

        [Fact]
        public void キーは有効1値でBool型()
        {
            var trans = CreateTransform(FaceShadowItems.Receive);
            Assert.Equal(MTEP.TransformType.FaceShadow, trans.type);
            Assert.Equal(1, trans.valueCount);
            Assert.Equal(0, (int)MTEP.TransformDataFaceShadow.Index.IsEnabled);
            Assert.Equal(MTEP.CustomValueType.BoolValue,
                trans.GetCustomValueInfoMap()["isEnabled"].type);
        }

        [Fact]
        public void 既定値へ戻す値も項目ごとのゲームの既定値()
        {
            Assert.Equal(1f, CreateTransform(FaceShadowItems.Receive).GetCustomValueInfoMap()["isEnabled"].defaultValue);
            Assert.Equal(0f, CreateTransform(FaceShadowItems.Cast).GetCustomValueInfoMap()["isEnabled"].defaultValue);
        }

        [Fact]
        public void 新しいキーの既定はゲームの既定値()
        {
            Assert.True(CreateTransform(FaceShadowItems.Receive).isEnabled);
            Assert.False(CreateTransform(FaceShadowItems.Cast).isEnabled);
        }

        [Fact]
        public void 上書きとキーのある項目だけを定義順でキーにする()
        {
            var overrides = new Dictionary<string, bool> { { FaceShadowItems.Cast, true } };
            var keyed = new List<string> { FaceShadowItems.Receive };

            var names = new List<string>();
            MTEP.MorphTimelineLayer.SelectFaceShadowBones(overrides.ContainsKey, keyed.Contains, names);

            Assert.Equal(new[] { FaceShadowItems.Receive, FaceShadowItems.Cast }, names);
        }

        [Fact]
        public void 上書きもキーも無ければキーにせず前回の結果も消す()
        {
            var names = new List<string> { FaceShadowItems.Cast };
            MTEP.MorphTimelineLayer.SelectFaceShadowBones(item => false, item => false, names);
            Assert.Empty(names);
        }

        [Fact]
        public void 項目名は表情レイヤーのボーン名として強制上書きと同じ接頭辞を持つ()
        {
            // 表情レイヤーのモーフ名と衝突させないため。XML の Bone 名なので変更しないこと
            Assert.Equal("faceReceiveShadows", FaceShadowItems.Receive);
            Assert.Equal("faceCastShadows", FaceShadowItems.Cast);
        }

        [Fact]
        public void 顔の影の項目だけを顔の影のボーンとして扱う()
        {
            Assert.True(MTEP.MorphTimelineLayer.IsFaceShadowBone(FaceShadowItems.Receive));
            Assert.True(MTEP.MorphTimelineLayer.IsFaceShadowBone(FaceShadowItems.Cast));
            Assert.False(MTEP.MorphTimelineLayer.IsFaceShadowBone(MTEP.FaceMorphUtils.FORCE_OVERRIDE_BONE_NAME));
            Assert.False(MTEP.MorphTimelineLayer.IsFaceShadowBone("eyeclose"));
        }
    }
}
