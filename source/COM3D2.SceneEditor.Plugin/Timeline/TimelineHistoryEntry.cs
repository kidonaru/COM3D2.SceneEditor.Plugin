using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムライン操作 1 件の履歴エントリ。
    /// タイムラインの状態は IStateSnapshot で表現できないため、
    /// TimelineXml の前後スナップショットを対で持ち SE の履歴スタックへ参加する
    /// </summary>
    public class TimelineHistoryEntry : IHistoryEntry
    {
        public string description { get; set; }

        private readonly MTEP.TimelineXml _before;
        private readonly MTEP.TimelineXml _after;

        public TimelineHistoryEntry(MTEP.TimelineXml before, MTEP.TimelineXml after, string description)
        {
            _before = before;
            _after = after;
            this.description = description;
        }

        // タイムラインが閉じられている間は適用できない (エントリはスキップされる)
        public bool canApply => MTEP.TimelineManager.instance.timeline != null;

        public void ApplyBefore()
        {
            Restore(_before);
        }

        public void ApplyAfter()
        {
            Restore(_after);
        }

        private static void Restore(MTEP.TimelineXml xml)
        {
            MTEP.TimelineManager.instance.UpdateTimeline(xml);
            // 次の編集の before がこの復元後状態を指すよう、確定済みスナップショットを更新する
            MTEP.TimelineHistoryManager.instance.lastCommittedXml = xml;
        }
    }
}
