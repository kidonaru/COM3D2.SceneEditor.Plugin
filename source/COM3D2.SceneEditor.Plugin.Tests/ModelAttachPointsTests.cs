using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;
using AttachPoint = PhotoTransTargetObject.AttachPoint;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// モデルのアタッチ部位の一覧。値はキーに保存されるため、並びがずれると保存済みのアタッチが別の部位になる
    /// </summary>
    public class ModelAttachPointsTests
    {
        [Fact]
        public void 名前はゲームの部位の後ろに胸と骨盤を足した並び()
        {
            Assert.Equal((int)ModelAttachPoints.Max + 1, ModelAttachPoints.Names.Count);
            Assert.Equal("設定なし", ModelAttachPoints.Names[(int)AttachPoint.Null]);
            Assert.Equal("左足首", ModelAttachPoints.Names[(int)AttachPoint.Foot_L]);
            Assert.Equal("胸", ModelAttachPoints.Names[(int)ModelAttachPoints.Chest]);
            Assert.Equal("骨盤", ModelAttachPoints.Names[(int)ModelAttachPoints.Pelvis]);
        }

        [Theory]
        [InlineData(19, "Bip01 Spine1a")]
        [InlineData(20, "Bip01 Pelvis")]
        [InlineData((int)AttachPoint.Hand_R, null)]
        [InlineData((int)AttachPoint.Null, null)]
        public void 独自値だけボーン名で引く(int point, string expected)
        {
            Assert.Equal(expected, ModelAttachPoints.GetExtraBoneName((AttachPoint)point));
        }

        [Fact]
        public void 親ボーンに一致する部位を探す()
        {
            var hand = new object();
            var pelvis = new object();
            var bones = new Dictionary<AttachPoint, object>
            {
                { AttachPoint.Hand_R, hand },
                { ModelAttachPoints.Pelvis, pelvis },
            };
            Func<AttachPoint, object> resolve = p =>
            {
                object bone;
                return bones.TryGetValue(p, out bone) ? bone : null;
            };

            AttachPoint found;
            Assert.True(ModelAttachPoints.TryFindAttachPoint(resolve, pelvis, out found));
            Assert.Equal(ModelAttachPoints.Pelvis, found);
            Assert.True(ModelAttachPoints.TryFindAttachPoint(resolve, hand, out found));
            Assert.Equal(AttachPoint.Hand_R, found);

            Assert.False(ModelAttachPoints.TryFindAttachPoint(resolve, new object(), out found));
            Assert.Equal(AttachPoint.Null, found);
        }
    }
}
