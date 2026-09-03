namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// タイムライン BGM ファイルの設定。
    /// TimelineData が持つがタイムライン未読込時は BGMManager が standalone 値として保持する
    /// </summary>
    public class BgmSettings
    {
        public string bgmPath = "";
        public float bpm = 120f;
        public bool isShowBPMLine = false;
        public float bpmLineOffsetFrame = 0f;

        public void CopyFrom(BgmSettings src)
        {
            bgmPath = src.bgmPath;
            bpm = src.bpm;
            isShowBPMLine = src.isShowBPMLine;
            bpmLineOffsetFrame = src.bpmLineOffsetFrame;
        }

        // TimelineXml の要素名は互換維持のため変えない (ReadFrom / WriteTo で写す)
        public void ReadFrom(TimelineXml xml)
        {
            bgmPath = xml.bgmPath;
            bpm = xml.bpm;
            isShowBPMLine = xml.isShowBPMLine;
            bpmLineOffsetFrame = xml.bpmLineOffsetFrame;
        }

        public void WriteTo(TimelineXml xml)
        {
            xml.bgmPath = bgmPath;
            xml.bpm = bpm;
            xml.isShowBPMLine = isShowBPMLine;
            xml.bpmLineOffsetFrame = bpmLineOffsetFrame;
        }
    }
}
