using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;
using AttachPoint = PhotoTransTargetObject.AttachPoint;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// モデルキーのアタッチ値 (index 12〜14) を固定する。
    /// 旧 12 値データの補正がずれると、MTE 産や旧 SE 産のモデルがメイド 0 にアタッチされてしまう
    /// </summary>
    public class TransformDataModelTests
    {
        private static TransformDataModel Create()
        {
            var trans = new TransformDataModel();
            trans.Initialize("test.menu");
            trans.Reset();
            return trans;
        }

        [Fact]
        public void アタッチ値は15値の末尾3つで既定はなし_Head_OFF()
        {
            var trans = Create();
            Assert.Equal(15, trans.valueCount);

            var map = trans.GetCustomValueInfoMap();
            Assert.Equal((int)TransformDataModel.Index.AttachMaidSlotNo, map["attachMaidSlotNo"].index);
            Assert.Equal(CustomValueUIType.MaidSlot, map["attachMaidSlotNo"].uiType);
            Assert.Equal((int)TransformDataModel.Index.AttachPoint, map["attachPoint"].index);
            Assert.Equal(CustomValueUIType.AttachPoint, map["attachPoint"].uiType);
            Assert.Equal((int)TransformDataModel.Index.WorldLerp, map["worldLerp"].index);
            Assert.Equal(CustomValueType.BoolValue, map["worldLerp"].type);

            Assert.Equal(-1, trans.attachMaidSlotNo);
            Assert.Equal(AttachPoint.Head, trans.attachPoint);
            Assert.False(trans.worldLerp);
            Assert.False(trans.isAttached);
        }

        [Fact]
        public void 旧12値のキーはアタッチなしへ補正される()
        {
            var trans = Create();
            trans.FromXml(new TransformXml
            {
                name = "test.menu",
                type = TransformType.Model,
                values = new float[TransformDataModel.LegacyValueCount],
            });

            Assert.Equal(-1, trans.attachMaidSlotNo);
            Assert.Equal(AttachPoint.Head, trans.attachPoint);
            Assert.False(trans.worldLerp);
        }

        [Fact]
        public void 値15個のキーは補正しない()
        {
            var values = new float[15];
            values[(int)TransformDataModel.Index.AttachMaidSlotNo] = 0f;
            values[(int)TransformDataModel.Index.AttachPoint] = (float)AttachPoint.Hand_R;
            values[(int)TransformDataModel.Index.WorldLerp] = 1f;

            var trans = Create();
            trans.FromXml(new TransformXml { name = "test.menu", type = TransformType.Model, values = values });

            Assert.Equal(0, trans.attachMaidSlotNo);
            Assert.Equal(AttachPoint.Hand_R, trans.attachPoint);
            Assert.True(trans.worldLerp);
            Assert.True(trans.isAttached);
        }

        [Theory]
        [InlineData(AttachPoint.Null, 0, false)]
        [InlineData(AttachPoint.Head, -1, false)]
        [InlineData(AttachPoint.Head, 0, true)]
        public void IsAttachedは部位Nullかスロット負ならfalse(AttachPoint point, int slot, bool expected)
        {
            Assert.Equal(expected, TransformDataModel.IsAttached(point, slot));
        }

        [Fact]
        public void 再登録時は既存キーのワールド補間を引き継ぐ()
        {
            var existing = Create();
            existing.worldLerp = true;

            var trans = Create();
            trans.InheritKeySettings(existing);
            Assert.True(trans.worldLerp);

            var fresh = Create();
            fresh.InheritKeySettings(null);
            Assert.False(fresh.worldLerp);
        }
    }
}
