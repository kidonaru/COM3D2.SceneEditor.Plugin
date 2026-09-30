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
            Assert.Equal(15, TransformDataModel.ValueCount);
            Assert.Equal(TransformDataModel.ValueCount, trans.valueCount);

            var map = trans.GetCustomValueInfoMap();
            Assert.Equal((int)TransformDataModel.Index.AttachMaidSlotNo, map["attachMaidSlotNo"].index);
            Assert.Equal(CustomValueUIType.AttachTarget, map["attachMaidSlotNo"].uiType);
            Assert.Equal((int)TransformDataModel.Index.AttachPoint, map["attachPoint"].index);
            Assert.Equal(CustomValueUIType.AttachPoint, map["attachPoint"].uiType);
            // Null (設定なし) はアタッチなしの別表現になるため、キーでは選ばせない
            Assert.Equal((float)AttachPoint.Fix, map["attachPoint"].min);
            Assert.Equal((float)ModelAttachPoints.Max, map["attachPoint"].max);
            Assert.Equal((int)TransformDataModel.Index.WorldLerp, map["worldLerp"].index);
            Assert.Equal(CustomValueType.BoolValue, map["worldLerp"].type);

            Assert.Equal(-1, trans.attachMaidSlotNo);
            Assert.Equal(AttachPoint.Head, trans.attachPoint);
            Assert.False(trans.worldLerp);
            Assert.False(TransformDataModel.IsAttached(trans.attachPoint, trans.attachMaidSlotNo));
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
            Assert.True(TransformDataModel.IsAttached(trans.attachPoint, trans.attachMaidSlotNo));
        }

        [Theory]
        [InlineData("menu/cup.menu", "cup.menu")]
        [InlineData("cup.menu", "cup.menu")]
        [InlineData("cup.menu (2)", "cup.menu (2)")]
        public void 名前はパス付きmenuだけファイル名にする(string name, string expected)
        {
            Assert.Equal(expected, TransformDataModel.NormalizeName(name));
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

        [Fact]
        public void アタッチ先モデルは隠し文字列値で既定は空()
        {
            var trans = Create();
            Assert.Equal(1, trans.strValueCount);
            var info = trans.GetStrValueInfoMap()["attachModel"];
            Assert.Equal((int)TransformDataModel.StrIndex.AttachModel, info.index);
            Assert.True(info.hidden);
            Assert.Equal("", trans.attachModelName);
            Assert.False(trans.isAttachedToModel);
        }

        [Fact]
        public void モデルへのアタッチは目印のスロットと部位Headにそろう()
        {
            var trans = Create();
            trans.attachPoint = AttachPoint.Hand_R;
            trans.SetAttachedToModel("desk.menu (2)");

            Assert.Equal(ModelAttachTarget.ModelSlotNo, trans.attachMaidSlotNo);
            Assert.Equal(AttachPoint.Head, trans.attachPoint);
            Assert.Equal("desk.menu (2)", trans.attachModelName);
            Assert.True(trans.isAttachedToModel);
            Assert.True(TransformDataModel.IsAttached(trans.attachPoint, trans.attachMaidSlotNo, trans.attachModelName));
            // 旧来の判定 (旧 SE と同じ) ではアタッチなしに見える
            Assert.False(TransformDataModel.IsAttached(trans.attachPoint, trans.attachMaidSlotNo));
        }

        [Fact]
        public void アタッチなしとメイドへのアタッチはモデル名を消す()
        {
            var trans = Create();
            trans.SetAttachedToModel("desk.menu");
            trans.SetAttachTarget(1, "");
            Assert.Equal(1, trans.attachMaidSlotNo);
            Assert.Equal("", trans.attachModelName);

            trans.SetAttachedToModel("desk.menu");
            trans.SetUnattached();
            Assert.Equal(-1, trans.attachMaidSlotNo);
            Assert.Equal("", trans.attachModelName);
        }

        [Fact]
        public void 目印のスロットでモデル名が空のキーはアタッチなしへ補正される()
        {
            var values = new float[15];
            values[(int)TransformDataModel.Index.AttachMaidSlotNo] = ModelAttachTarget.ModelSlotNo;
            var trans = Create();
            trans.FromXml(new TransformXml { name = "test.menu", type = TransformType.Model, values = values });

            Assert.Equal(-1, trans.attachMaidSlotNo);
            Assert.Equal(AttachPoint.Head, trans.attachPoint);
        }

        [Fact]
        public void メイドへのアタッチに残ったモデル名は捨てる()
        {
            var values = new float[15];
            values[(int)TransformDataModel.Index.AttachMaidSlotNo] = 0f;
            values[(int)TransformDataModel.Index.AttachPoint] = (float)AttachPoint.Head;
            var trans = Create();
            trans.FromXml(new TransformXml
            {
                name = "test.menu",
                type = TransformType.Model,
                values = values,
                strValues = new[] { "desk.menu" },
            });

            Assert.Equal(0, trans.attachMaidSlotNo);
            Assert.Equal("", trans.attachModelName);
        }

        [Fact]
        public void 書き出したXMLはあとからキーを変えても変わらない()
        {
            // 操作履歴は変更前の XML を控えておき Undo で戻す。配列を共有すると控えまで書き換わる
            var trans = Create();
            trans.SetAttachedToModel("desk.menu");
            var xml = trans.ToXml();

            trans.SetUnattached();

            Assert.Equal("desk.menu", xml.strValues[(int)TransformDataModel.StrIndex.AttachModel]);
        }

        [Fact]
        public void モデルへのアタッチはXMLを往復する()
        {
            var trans = Create();
            trans.SetAttachedToModel("desk.menu (2)");
            var restored = Create();
            restored.FromXml(trans.ToXml());

            Assert.True(restored.isAttachedToModel);
            Assert.Equal("desk.menu (2)", restored.attachModelName);
            Assert.True(restored.IsSameValues(trans));
        }
    }
}
