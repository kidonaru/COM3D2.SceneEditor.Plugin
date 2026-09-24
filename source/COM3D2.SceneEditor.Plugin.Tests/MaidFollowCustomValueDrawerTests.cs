using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;
using AttachPoint = PhotoTransTargetObject.AttachPoint;
using RowKey = COM3D2.SceneEditor.Plugin.MaidFollowCustomValueDrawer.RowKey;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// 行キーの等価性。キーフレーム詳細は BoneData の参照、一括は boxing した
    /// TransformType を owner に渡すため、両方の比較が成り立つ必要がある
    /// </summary>
    public class MaidFollowCustomValueDrawerTests
    {
        [Fact]
        public void 同じ参照と同じキーなら等しい()
        {
            var owner = new object();

            Assert.Equal(new RowKey(owner, "maidSlotNo"), new RowKey(owner, "maidSlotNo"));
            Assert.Equal(
                new RowKey(owner, "maidSlotNo").GetHashCode(),
                new RowKey(owner, "maidSlotNo").GetHashCode());
        }

        [Fact]
        public void 同じ参照でもキーが違えば等しくない()
        {
            var owner = new object();

            Assert.NotEqual(new RowKey(owner, "maidSlotNo"), new RowKey(owner, "maidPointType"));
        }

        [Fact]
        public void 別の参照なら等しくない()
        {
            Assert.NotEqual(new RowKey(new object(), "maidSlotNo"), new RowKey(new object(), "maidSlotNo"));
        }

        [Fact]
        public void boxingした列挙は値が同じなら等しい()
        {
            // 一括編集は毎フレーム boxing し直した TransformType を owner に渡す
            object a = TransformType.Camera;
            object b = TransformType.Camera;

            Assert.NotSame(a, b);
            Assert.Equal(new RowKey(a, "maidSlotNo"), new RowKey(b, "maidSlotNo"));
            Assert.Equal(
                new RowKey(a, "maidSlotNo").GetHashCode(),
                new RowKey(b, "maidSlotNo").GetHashCode());
        }

        [Fact]
        public void boxingした列挙は値が違えば等しくない()
        {
            object camera = TransformType.Camera;
            object subCamera = TransformType.SubCamera;

            Assert.NotEqual(new RowKey(camera, "maidSlotNo"), new RowKey(subCamera, "maidSlotNo"));
        }

        [Theory]
        [InlineData(AttachPoint.Fix, 0)]
        [InlineData(AttachPoint.Hand_R, (int)AttachPoint.Hand_R - 1)]
        [InlineData(AttachPoint.Foot_L, (int)AttachPoint.Foot_L - 1)]
        [InlineData(ModelAttachPoints.Pelvis, (int)ModelAttachPoints.Pelvis - 1)]
        // 旧ビルドで入り得た Null は先頭へ寄せる
        [InlineData(AttachPoint.Null, 0)]
        public void 部位コンボはNullを除いた並びで値と添字を変換する(AttachPoint point, int index)
        {
            Assert.Equal(index, MaidFollowCustomValueDrawer.ToAttachPointIndex((float)point));
            if (point != AttachPoint.Null)
            {
                Assert.Equal((float)point, MaidFollowCustomValueDrawer.ToAttachPointValue(index));
            }
        }

        [Theory]
        [InlineData(CustomValueUIType.MaidSlot, true)]
        [InlineData(CustomValueUIType.MaidPoint, true)]
        [InlineData(CustomValueUIType.AttachPoint, true)]
        [InlineData(CustomValueUIType.Default, false)]
        public void コンボで描くのはメイドと部位の値だけ(CustomValueUIType uiType, bool expected)
        {
            // false だとインスペクタが数値入力で描き、モデルキーのアタッチ部位を enum 番号で打つことになる
            Assert.Equal(expected, MaidFollowCustomValueDrawer.IsComboValue(new CustomValueInfo { uiType = uiType }));
        }
    }
}
