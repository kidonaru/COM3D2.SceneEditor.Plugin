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
                // 差分は登録時に 1 度だけ求め、Undo/Redo の両方向で使い回す
                var diff = TimelineXmlDiff.Compute(beforeXml, afterXml);
                SceneEditor.Plugin.HistoryManager.instance.AddEntry(
                    new SceneEditor.Plugin.TimelineHistoryEntry(beforeXml, afterXml, diff, description));
            }

            lastCommittedXml = afterXml;
        }

        /// <summary>
        /// 現在のタイムラインを「変更前」の基準として据える。履歴には積まない。
        /// 新規作成・読み込みの直後に呼ぶことで、その後の最初の操作から
        /// AddHistory がエントリを積めるようになる (基準作りに 1 手目を消費しない)
        /// </summary>
        public void SetBaseline(TimelineData timeline)
        {
            lastCommittedXml = timeline?.ToXml();
        }

        /// <summary>タイムライン破棄時に古い状態を持ち越さない</summary>
        public void ClearHistory()
        {
            lastCommittedXml = null;
        }
    }
}
