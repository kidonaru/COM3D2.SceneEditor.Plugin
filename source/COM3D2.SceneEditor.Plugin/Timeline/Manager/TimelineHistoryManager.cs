using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class TimelineHistoryData
    {
        public string description;
        public TimelineXml xml;
        public long timestamp;
    }

    public class TimelineHistoryManager : ManagerBase
    {
        public List<TimelineHistoryData> historyList = new List<TimelineHistoryData>();
        public int historyIndex = -1;

        private List<TimelineHistoryData> _historyListInv = new List<TimelineHistoryData>();
        public List<TimelineHistoryData> historyListInv
        {
            get
            {
                _historyListInv.Clear();
                for (int i = historyList.Count - 1; i >= 0; i--)
                {
                    _historyListInv.Add(historyList[i]);
                }
                return _historyListInv;
            }
        }

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

        private int historyLimit => config.historyLimit;

        // 現在のタイムライン状態に対応する確定済みスナップショット。
        // SE 履歴ブリッジの before として使う。AddHistory と
        // TimelineHistoryEntry (SE 側 Undo/Redo 適用) の双方で更新する
        public TimelineXml lastCommittedXml { get; set; }

        private TimelineHistoryManager()
        {
        }

        public void AddHistory(TimelineData timeline, string description)
        {
            var beforeXml = lastCommittedXml;

            if (historyIndex < historyList.Count - 1)
            {
                historyList.RemoveRange(historyIndex + 1, historyList.Count - historyIndex - 1);
            }

            while (historyList.Count > 0 && historyList.Count >= historyLimit)
            {
                historyList.RemoveAt(0);
                historyIndex--;
            }

            if (historyLimit <= 0)
            {
                // 履歴無効時も現在状態の追跡は維持する (再有効化時に古い before を積まないため)
                lastCommittedXml = timeline.ToXml();
                return;
            }

            var now = System.DateTime.Now;

            var history = new TimelineHistoryData();
            history.xml = timeline.ToXml();
            history.timestamp = now.Ticks;
            history.description = string.Format("[{0}] {1}", now.ToString("MM/dd HH:mm:ss"), description);

            historyList.Add(history);
            historyIndex = historyList.Count - 1;

            // SE の履歴スタックへも同じ操作を積み、Ctrl+Z を一本化する。
            // 直前スナップショットが無い場合 (新規作成・読み込み直後) は
            // それ以前へ戻る意味がないため積まない
            if (beforeXml != null)
            {
                SceneEditor.Plugin.HistoryManager.instance.AddEntry(
                    new SceneEditor.Plugin.TimelineHistoryEntry(beforeXml, history.xml, description));
            }
            lastCommittedXml = history.xml;
        }

        public void Undo()
        {
            if (historyIndex <= 0 || historyList.Count == 0)
            {
                return;
            }

            RestoreHistory(historyIndex - 1);
        }

        public void Redo()
        {
            if (historyIndex >= historyList.Count - 1 || historyList.Count == 0)
            {
                return;
            }

            RestoreHistory(historyIndex + 1);
        }

        public void RestoreHistory(int historyIndex)
        {
            if (this.historyIndex == historyIndex)
            {
                return;
            }
            if (historyIndex < 0 || historyIndex >= historyList.Count)
            {
                return;
            }

            this.historyIndex = historyIndex;

            var xml = historyList[historyIndex].xml;
            TimelineManager.instance.UpdateTimeline(xml);
            // SE 履歴ブリッジの before がこの復元後状態を指すよう追従させる
            // (現状この経路の呼び出し元は無いが、復活時の整合のため揃えておく)
            lastCommittedXml = xml;
        }

        public void ClearHistory()
        {
            historyList.Clear();
            historyIndex = -1;
            // タイムライン破棄時に古い状態を SE 履歴ブリッジへ持ち越さない
            lastCommittedXml = null;
        }
    }
}