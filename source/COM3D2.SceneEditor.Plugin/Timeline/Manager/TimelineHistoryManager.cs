namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// タイムラインの変更を SE の履歴スタックへ橋渡しする。
    ///
    /// Undo/Redo の本体・件数制限・UI は SE の HistoryManager が持つ。
    /// ここが持つのは「直前に確定したタイムラインの姿」だけで、
    /// 変更のたびにその前後を 1 エントリとして SE へ積む
    /// </summary>
    public class TimelineHistoryManager : ManagerBase
    {
        private static TimelineHistoryManager _instance = null;

        public static TimelineHistoryManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TimelineHistoryManager();
                }
                return _instance;
            }
        }

        /// <summary>
        /// 現在のタイムライン状態に対応する確定済みスナップショット。
        /// 積むエントリの before として使う。AddHistory と
        /// TimelineHistoryEntry (SE 側 Undo/Redo 適用) の双方で更新する
        /// </summary>
        public TimelineXml lastCommittedXml { get; set; }

        private TimelineHistoryManager()
        {
        }

        /// <summary>
        /// 変更を 1 エントリとして SE の履歴へ積む。
        /// 件数制限と履歴無効 (SE 側 config.historyLimit &lt;= 0) の判定は SE 側が行う
        /// </summary>
        public void AddHistory(TimelineData timeline, string description)
        {
            var beforeXml = lastCommittedXml;
            var afterXml = timeline.ToXml();

            // 直前スナップショットが無い場合 (新規作成・読み込み直後) は
            // それ以前へ戻る意味がないため積まない
            if (beforeXml != null)
            {
                SceneEditor.Plugin.HistoryManager.instance.AddEntry(
                    new SceneEditor.Plugin.TimelineHistoryEntry(beforeXml, afterXml, description));
            }

            lastCommittedXml = afterXml;
        }

        /// <summary>タイムライン破棄時に古い状態を持ち越さない</summary>
        public void ClearHistory()
        {
            lastCommittedXml = null;
        }
    }
}
