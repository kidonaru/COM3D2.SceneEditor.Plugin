using UnityEngine;
using Xunit;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    public class GravityTimelineLayerTests
    {
        private static MTEP.TransformDataGravity CreateTransform()
        {
            var trans = new MTEP.TransformDataGravity();
            trans.Initialize("hair");
            return trans;
        }

        [Fact]
        public void TransformDataGravity_は4値で有効フラグはBool型として扱われる()
        {
            var trans = CreateTransform();
            Assert.Equal(MTEP.TransformType.Gravity, trans.type);
            Assert.Equal(4, trans.valueCount);

            var infoMap = trans.GetCustomValueInfoMap();
            Assert.Equal(MTEP.CustomValueType.BoolValue, infoMap["enabled"].type);
            Assert.Equal(-1f, infoMap["x"].min);
            Assert.Equal(1f, infoMap["x"].max);
        }

        [Fact]
        public void TransformDataGravity_のoffsetとenabledは値配列へ書き戻される()
        {
            var trans = CreateTransform();
            trans.enabled = true;
            trans.offset = new Vector3(0.5f, -0.25f, 1f);

            Assert.True(trans.enabledValue.boolValue);
            Assert.Equal(0.5f, trans.offsetValues[0].value);
            Assert.Equal(-0.25f, trans.offsetValues[1].value);
            Assert.Equal(1f, trans.offsetValues[2].value);
            Assert.Equal(3, trans.tangentValues.Length);
        }

        [Fact]
        public void TransformDataGravity_のisDefaultは無効かつzeroのときだけtrueになる()
        {
            var trans = CreateTransform();
            Assert.True(trans.isDefault);

            trans.enabled = true;
            Assert.False(trans.isDefault);

            trans.enabled = false;
            trans.offset = new Vector3(0f, 0.1f, 0f);
            Assert.False(trans.isDefault);
        }

        [Fact]
        public void GravityItemInspector_は項目名からカテゴリを引き未知の名前ではnullを返す()
        {
            Assert.Equal("hair", GravityItemInspector.ResolveCategory("hair").id);
            Assert.Equal("skirt", GravityItemInspector.ResolveCategory("skirt").id);
            Assert.Null(GravityItemInspector.ResolveCategory("unknown"));
        }
    }
}
