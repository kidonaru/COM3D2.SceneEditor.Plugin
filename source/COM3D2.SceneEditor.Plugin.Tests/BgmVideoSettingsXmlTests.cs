using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using Xunit;

namespace COM3D2.SceneEditor.Plugin.Tests
{
    /// <summary>
    /// BGM / 動画設定 DTO と TimelineXml の写しが対称であることを固定する。
    /// 片方向だけ項目を落とすとタイムライン保存時に設定が黙って消えるため
    /// </summary>
    public class BgmVideoSettingsXmlTests
    {
        [Fact]
        public void BgmSettings_TimelineXmlとの往復で値が保持される()
        {
            var src = new BgmSettings
            {
                bgmPath = @"C:\music\test.ogg",
                bpm = 128.5f,
                isShowBPMLine = true,
                bpmLineOffsetFrame = -3.5f,
            };

            var xml = new TimelineXml();
            src.WriteTo(xml);
            var dst = new BgmSettings();
            dst.ReadFrom(xml);

            Assert.Equal(@"C:\music\test.ogg", dst.bgmPath);
            Assert.Equal(128.5f, dst.bpm);
            Assert.True(dst.isShowBPMLine);
            Assert.Equal(-3.5f, dst.bpmLineOffsetFrame);
        }

        [Fact]
        public void VideoSettings_VideoSettingsXmlとの往復で値が保持される()
        {
            var src = new VideoSettings
            {
                enabled = false,
                displayType = VideoDisplayType.Frontmost,
                path = @"C:\movie	est.mp4",
                position = new Vector3(1f, 2f, 3f),
                rotation = new Vector3(10f, 20f, 30f),
                scale = 2.5f,
                startTime = 1.25f,
                volume = 0.3f,
                alpha = 0.9f,
                guiPosition = new Vector2(0.1f, 0.2f),
                guiScale = 0.8f,
                guiAlpha = 0.7f,
                backmostPosition = new Vector2(0.3f, 0.4f),
                backmostScale = 1.5f,
                backmostAlpha = 0.6f,
                frontmostPosition = new Vector2(-0.5f, 0.5f),
                frontmostScale = 0.4f,
                frontmostAlpha = 0.2f,
            };

            var xml = src.ToXml();
            var dst = new VideoSettings();
            dst.ReadFrom(xml);

            Assert.False(dst.enabled);
            Assert.Equal(VideoDisplayType.Frontmost, dst.displayType);
            Assert.Equal(@"C:\movie	est.mp4", dst.path);
            Assert.Equal(new Vector3(1f, 2f, 3f), dst.position);
            Assert.Equal(new Vector3(10f, 20f, 30f), dst.rotation);
            Assert.Equal(2.5f, dst.scale);
            Assert.Equal(1.25f, dst.startTime);
            Assert.Equal(0.3f, dst.volume);
            Assert.Equal(0.9f, dst.alpha);
            Assert.Equal(new Vector2(0.1f, 0.2f), dst.guiPosition);
            Assert.Equal(0.8f, dst.guiScale);
            Assert.Equal(0.7f, dst.guiAlpha);
            Assert.Equal(new Vector2(0.3f, 0.4f), dst.backmostPosition);
            Assert.Equal(1.5f, dst.backmostScale);
            Assert.Equal(0.6f, dst.backmostAlpha);
            Assert.Equal(new Vector2(-0.5f, 0.5f), dst.frontmostPosition);
            Assert.Equal(0.4f, dst.frontmostScale);
            Assert.Equal(0.2f, dst.frontmostAlpha);
        }

        [Fact]
        public void TimelineXml_旧形式のフラット動画項目はInitializeで1件目として取り込まれる()
        {
            var xml = new TimelineXml
            {
                version = 10,
                videoEnabled = false,
                videoDisplayType = VideoDisplayType.Backmost,
                videoPath = @"C:\movie\legacy.mp4",
                videoStartTime = 2f,
                videoBackmostAlpha = 0.3f,
            };

            xml.Initialize();

            Assert.Single(xml.videos);
            Assert.False(xml.videos[0].enabled);
            Assert.Equal(VideoDisplayType.Backmost, xml.videos[0].displayType);
            Assert.Equal(@"C:\movie\legacy.mp4", xml.videos[0].path);
            Assert.Equal(2f, xml.videos[0].startTime);
            Assert.Equal(0.3f, xml.videos[0].backmostAlpha);
        }

        [Fact]
        public void TimelineXml_version4未満はDisplayOnGUIと開始オフセットを変換してから取り込む()
        {
            var xml = new TimelineXml
            {
                version = 3,
                videoDisplayOnGUI = false,
                videoStartTime = 5f,
                startOffsetTime = 1f,
            };

            xml.Initialize();

            Assert.Single(xml.videos);
            Assert.Equal(VideoDisplayType.Mesh, xml.videos[0].displayType);
            Assert.Equal(4f, xml.videos[0].startTime);
        }

        [Fact]
        public void TimelineXml_動画リストが既にあればフラット項目は取り込まない()
        {
            var xml = new TimelineXml { version = 33, videoPath = @"C:\movie\ignored.mp4" };
            xml.videos.Add(new VideoSettingsXml { path = @"C:\movie.mp4" });
            xml.videos.Add(new VideoSettingsXml { path = @"C:\movie.mp4" });

            xml.Initialize();

            Assert.Equal(2, xml.videos.Count);
            Assert.Equal(@"C:\movie.mp4", xml.videos[0].path);
        }

        [Fact]
        public void CopyFrom_全項目を写す()
        {
            var bgm = new BgmSettings { bgmPath = "a.ogg", bpm = 90f, isShowBPMLine = true, bpmLineOffsetFrame = 2f };
            var bgmCopy = new BgmSettings();
            bgmCopy.CopyFrom(bgm);
            Assert.Equal("a.ogg", bgmCopy.bgmPath);
            Assert.Equal(90f, bgmCopy.bpm);
            Assert.True(bgmCopy.isShowBPMLine);
            Assert.Equal(2f, bgmCopy.bpmLineOffsetFrame);

            var video = new VideoSettings { path = "b.mp4", displayType = VideoDisplayType.Mesh, frontmostAlpha = 0.1f };
            var videoCopy = new VideoSettings();
            videoCopy.CopyFrom(video);
            Assert.Equal("b.mp4", videoCopy.path);
            Assert.Equal(VideoDisplayType.Mesh, videoCopy.displayType);
            Assert.Equal(0.1f, videoCopy.frontmostAlpha);
        }
    }
}
