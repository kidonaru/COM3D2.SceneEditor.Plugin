using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムライン操作 1 件の履歴エントリ。
    /// タイムラインの状態は IStateSnapshot で表現できないため、
    /// TimelineXml の前後スナップショットを対で持ち SE の履歴スタックへ参加する。
    /// 前後の差分 (変更レイヤー) も持ち、可能なら該当レイヤーだけ再構築する
    /// </summary>
    public class TimelineHistoryEntry : IHistoryEntry
    {
        public string description { get; set; }

        private readonly MTEP.TimelineXml _before;
        private readonly MTEP.TimelineXml _after;
        private readonly MTEP.TimelineXmlDiff _diff;

        public TimelineHistoryEntry(
            MTEP.TimelineXml before,
            MTEP.TimelineXml after,
            MTEP.TimelineXmlDiff diff,
            string description)
        {
            _before = before;
            _after = after;
            _diff = diff;
            this.description = description;
        }

        // タイムラインが閉じられている間は適用できない (エントリはスキップされる)
        public bool canApply => MTEP.TimelineManager.instance.timeline != null;

        public void ApplyBefore()
        {
            Restore(_before, expectedCurrentXml: _after);
        }

        public void ApplyAfter()
        {
            Restore(_after, expectedCurrentXml: _before);
        }

        /// <param name="xml">復元する側</param>
        /// <param name="expectedCurrentXml">現在のタイムラインがこれと同一のときだけ部分適用できる</param>
        private void Restore(MTEP.TimelineXml xml, MTEP.TimelineXml expectedCurrentXml)
        {
            var timelineManager = MTEP.TimelineManager.instance;
            var historyManager = MTEP.TimelineHistoryManager.instance;

            // 部分適用は「現在のタイムラインが expectedCurrentXml と同一」が前提。AddHistory は
            // 前エントリの after をそのまま次の before に使い、ここは適用後に lastCommittedXml を
            // 置き換えるため、隣接エントリを順に辿っている限り参照が一致する。エントリのスキップ・
            // 適用失敗・RestoreTo の多段ジャンプで前提が崩れると一致しないので全再構築へ倒す
            if (_diff != null && _diff.canApplyPartially &&
                ReferenceEquals(historyManager.lastCommittedXml, expectedCurrentXml))
            {
                timelineManager.UpdateTimelineLayers(xml, _diff.changedLayerIndices);
            }
            else
            {
                timelineManager.UpdateTimeline(xml);
            }
            // 次の編集の before がこの復元後状態を指すよう、確定済みスナップショットを更新する
            historyManager.lastCommittedXml = xml;
        }
    }
}
