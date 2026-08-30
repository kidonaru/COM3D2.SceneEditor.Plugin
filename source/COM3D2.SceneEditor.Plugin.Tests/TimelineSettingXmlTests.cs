using System.IO;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor.Plugin;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// Phase W1 の設定 UI が編集する項目が XML 往復で保存されることを固定する。
    /// UI から変更できても保存されない項目があると設定が黙って消えるため
    /// </summary>
    public class TimelineSettingXmlTests
    {
        private static TimelineXml RoundTrip(TimelineXml src)
        {
            var serializer = new XmlSerializer(typeof(TimelineXml));
            using (var ms = new MemoryStream())
            {
                serializer.Serialize(ms, src);
                ms.Position = 0;
                return (TimelineXml)serializer.Deserialize(ms);
            }
        }

        [Fact]
        public void 設定UIで編集する項目が往復で保持される()
        {
            var src = new TimelineXml
            {
                directoryName = "テストフォルダ",
                frameRate = 60f,
                useHeadKey = true,
                useMuneKeyL = true,
                useMuneKeyR = true,
                isLoopAnm = false,
                isGroundLinkedToBackground = true,
                singleFrameType = SingleFrameType.Advance,
                isEasingAppliedToNextKeyframe = true,
                isTangentCamera = true,
                isTangentLight = true,
                isTangentMove = true,
                isTangentModel = true,
                isTangentModelBone = true,
                isTangentModelShapeKey = true,
                usePostEffectExtraColor = true,
                usePostEffectExtraBlend = true,
            };

            var dst = RoundTrip(src);

            Assert.Equal("テストフォルダ", dst.directoryName);
            Assert.Equal(60f, dst.frameRate);
            Assert.True(dst.useHeadKey);
            Assert.True(dst.useMuneKeyL);
            Assert.True(dst.useMuneKeyR);
            Assert.False(dst.isLoopAnm);
            Assert.True(dst.isGroundLinkedToBackground);
            Assert.Equal(SingleFrameType.Advance, dst.singleFrameType);
            Assert.True(dst.isEasingAppliedToNextKeyframe);
            Assert.True(dst.isTangentCamera);
            Assert.True(dst.isTangentLight);
            Assert.True(dst.isTangentMove);
            Assert.True(dst.isTangentModel);
            Assert.True(dst.isTangentModelBone);
            Assert.True(dst.isTangentModelShapeKey);
            Assert.True(dst.usePostEffectExtraColor);
            Assert.True(dst.usePostEffectExtraBlend);
        }
    }
}
