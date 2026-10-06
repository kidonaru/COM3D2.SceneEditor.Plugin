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
        public void TransformDataGravity_は5値で有効フラグとローカルはBool型として扱われる()
        {
            // 値の並びはタイムライン XML の保存形式。ベタ書きで固定する
            var trans = CreateTransform();
            Assert.Equal(MTEP.TransformType.Gravity, trans.type);
            Assert.Equal(5, trans.valueCount);
            Assert.Equal(4, (int)MTEP.TransformDataGravity.Index.Local);
            Assert.Equal(4, MTEP.TransformDataGravity.LegacyValueCount);

            var infoMap = trans.GetCustomValueInfoMap();
            Assert.Equal(MTEP.CustomValueType.BoolValue, infoMap["enabled"].type);
            Assert.Equal(MTEP.CustomValueType.BoolValue, infoMap["local"].type);
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

            trans.offset = Vector3.zero;
            trans.local = true;
            Assert.False(trans.isDefault);
        }

        [Fact]
        public void GravityItemInspector_は項目名からカテゴリを引き未知の名前ではnullを返す()
        {
            Assert.Equal("hair", GravityItemInspector.ResolveCategory("hair").id);
            Assert.Equal("skirt", GravityItemInspector.ResolveCategory("skirt").id);
            Assert.Null(GravityItemInspector.ResolveCategory("unknown"));
        }

        [Fact]
        public void TransformDataGravity_のlocalは値配列へ書き戻され補間対象に入らない()
        {
            var trans = CreateTransform();
            trans.local = true;

            Assert.True(trans.localValue.boolValue);
            Assert.Equal(1f, trans.values[(int)MTEP.TransformDataGravity.Index.Local].value);
            Assert.Equal(3, trans.tangentValues.Length);
        }

        [Theory]
        [InlineData(4)]
        [InlineData(3)]
        public void 旧キーはローカルOFFで読む(int valueCount)
        {
            var values = new float[valueCount];
            values[0] = 1f;
            var xml = new MTEP.TransformXml
            {
                name = "hair",
                type = MTEP.TransformType.Gravity,
                values = values,
                inTangents = new float[valueCount],
                outTangents = new float[valueCount],
                inSmoothBit = 0,
                outSmoothBit = 0,
            };

            var trans = CreateTransform();
            trans.local = true;
            trans.FromXml(xml);

            Assert.False(trans.local);
            Assert.True(trans.enabled);
        }

        [Fact]
        public void ローカルONのキーは往復で残る()
        {
            var trans = CreateTransform();
            trans.enabled = true;
            trans.local = true;
            trans.offset = new Vector3(0f, -1f, 0f);

            var restored = CreateTransform();
            restored.FromXml(trans.ToXml());

            Assert.True(restored.local);
            Assert.Equal(-1f, restored.offset.y);
        }
    }
}
