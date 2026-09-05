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
        // 動画の音は既定で鳴らさない (BGM と重なって驚くのを避ける)
        public float volume = 0f;
        public float alpha = 1f;
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
            guiScale = src.guiScale;
            guiAlpha = src.guiAlpha;
            backmostPosition = src.backmostPosition;
            backmostScale = src.backmostScale;
            backmostAlpha = src.backmostAlpha;
            frontmostPosition = src.frontmostPosition;
            frontmostScale = src.frontmostScale;
            frontmostAlpha = src.frontmostAlpha;
        }

        public void ReadFrom(VideoSettingsXml xml)
        {
            enabled = xml.enabled;
            displayType = xml.displayType;
            path = xml.path;
            position = xml.position;
            rotation = xml.rotation;
            scale = xml.scale;
            startTime = xml.startTime;
            volume = xml.volume;
            alpha = xml.alpha;
            guiScale = xml.guiScale;
            guiAlpha = xml.guiAlpha;
            backmostPosition = xml.backmostPosition;
            backmostScale = xml.backmostScale;
            backmostAlpha = xml.backmostAlpha;
            frontmostPosition = xml.frontmostPosition;
            frontmostScale = xml.frontmostScale;
            frontmostAlpha = xml.frontmostAlpha;
        }

        public VideoSettingsXml ToXml()
        {
            return new VideoSettingsXml
            {
                enabled = enabled,
                displayType = displayType,
                path = path,
                position = position,
                rotation = rotation,
                scale = scale,
                startTime = startTime,
                volume = volume,
                alpha = alpha,
                guiScale = guiScale,
                guiAlpha = guiAlpha,
                backmostPosition = backmostPosition,
                backmostScale = backmostScale,
                backmostAlpha = backmostAlpha,
                frontmostPosition = frontmostPosition,
                frontmostScale = frontmostScale,
                frontmostAlpha = frontmostAlpha,
            };
        }
    }
}
