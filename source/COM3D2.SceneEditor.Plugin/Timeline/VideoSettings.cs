using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 動画の読み込み・表示設定。
    /// TimelineData が持つがタイムライン未読込時は MovieManager が standalone 値として保持する
    /// </summary>
    public class VideoSettings
    {
        public bool enabled = true;
        public VideoDisplayType displayType = VideoDisplayType.GUI;
        public string path = "";
        public Vector3 position = new Vector3(0, 0, 0);
        public Vector3 rotation = new Vector3(0, 0, 0);
        public float scale = 1f;
        public float startTime = 0f;
        public float volume = 0.5f;
        public float alpha = 1f;
        public Vector2 guiPosition = new Vector2(0, 0);
        public float guiScale = 1f;
        public float guiAlpha = 1f;
        public Vector2 backmostPosition = new Vector2(0, 0);
        public float backmostScale = 1f;
        public float backmostAlpha = 0.5f;
        public Vector2 frontmostPosition = new Vector2(-0.8f, 0.8f);
        public float frontmostScale = 0.38f;
        public float frontmostAlpha = 1f;

        public void CopyFrom(VideoSettings src)
        {
            enabled = src.enabled;
            displayType = src.displayType;
            path = src.path;
            position = src.position;
            rotation = src.rotation;
            scale = src.scale;
            startTime = src.startTime;
            volume = src.volume;
            alpha = src.alpha;
            guiPosition = src.guiPosition;
            guiScale = src.guiScale;
            guiAlpha = src.guiAlpha;
            backmostPosition = src.backmostPosition;
            backmostScale = src.backmostScale;
            backmostAlpha = src.backmostAlpha;
            frontmostPosition = src.frontmostPosition;
            frontmostScale = src.frontmostScale;
            frontmostAlpha = src.frontmostAlpha;
        }

        // TimelineXml の要素名 (video 接頭辞) は互換維持のため変えない
        public void ReadFrom(TimelineXml xml)
        {
            enabled = xml.videoEnabled;
            displayType = xml.videoDisplayType;
            path = xml.videoPath;
            position = xml.videoPosition;
            rotation = xml.videoRotation;
            scale = xml.videoScale;
            startTime = xml.videoStartTime;
            volume = xml.videoVolume;
            alpha = xml.videoAlpha;
            guiPosition = xml.videoGUIPosition;
            guiScale = xml.videoGUIScale;
            guiAlpha = xml.videoGUIAlpha;
            backmostPosition = xml.videoBackmostPosition;
            backmostScale = xml.videoBackmostScale;
            backmostAlpha = xml.videoBackmostAlpha;
            frontmostPosition = xml.videoFrontmostPosition;
            frontmostScale = xml.videoFrontmostScale;
            frontmostAlpha = xml.videoFrontmostAlpha;
        }

        public void WriteTo(TimelineXml xml)
        {
            xml.videoEnabled = enabled;
            xml.videoDisplayType = displayType;
            xml.videoPath = path;
            xml.videoPosition = position;
            xml.videoRotation = rotation;
            xml.videoScale = scale;
            xml.videoStartTime = startTime;
            xml.videoVolume = volume;
            xml.videoAlpha = alpha;
            xml.videoGUIPosition = guiPosition;
            xml.videoGUIScale = guiScale;
            xml.videoGUIAlpha = guiAlpha;
            xml.videoBackmostPosition = backmostPosition;
            xml.videoBackmostScale = backmostScale;
            xml.videoBackmostAlpha = backmostAlpha;
            xml.videoFrontmostPosition = frontmostPosition;
            xml.videoFrontmostScale = frontmostScale;
            xml.videoFrontmostAlpha = frontmostAlpha;
        }
    }
}
