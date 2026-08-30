using System.Collections.Generic;
using UnityEngine.SceneManagement;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドの任意シェイプキーの変更追跡。
    /// チェック集合はここが唯一のソースで、タイムラインの opt-in
    /// (TimelineData.maidShapeKeysMap) へは片方向に流すだけ。
    /// 逆方向はタイムラインが差し替わったときの初期取り込みに限る。
    /// タグ名はメイド単位でフラットに持つ (スロットを跨いだ同名タグは同一視される。
    /// TimelineData.maidShapeKeysMap と MaidCache.GetBlendShape が元からこの前提)
    /// </summary>
    public class MaidShapeKeyEditManager : ManagerBase
    {
        private static MaidShapeKeyEditManager _instance;
        public static MaidShapeKeyEditManager instance
            => _instance ?? (_instance = new MaidShapeKeyEditManager());

        private MaidShapeKeyEditManager()
        {
        }

        private readonly Dictionary<Maid, EditTargetStore> _stores
            = new Dictionary<Maid, EditTargetStore>();

        private readonly List<Maid> _deadMaids = new List<Maid>();

        // 同期の作り直し判定。ストア集合の変化は門番が、タイムラインの差し替えは参照比較が見る
        private readonly TrackedDirtyGate _gate = new TrackedDirtyGate();
        private MTEP.TimelineData _lastTimeline;

        /// <summary>スロットを解決できないメイドを取りに行く間隔 (フレーム)</summary>
        private const int UnresolvedRetryInterval = 30;

        // 前回の同期でスロットを解決できなかったメイドが残っていたか
        private bool _hasUnresolvedMaid;
        private int _unresolvedFrameCount;

        // 差分計算の結果。使い回してゴミを出さない
        private readonly List<string> _toAdd = new List<string>();
        private readonly List<string> _toRemove = new List<string>();

        // SE の ManagerBase はタイムラインを知らないため完全修飾で引く
        // (timeline プロパティを持つのは別名前空間の同名クラス)
        private static MTEP.TimelineData currentTimeline
            => MTEP.TimelineManager.instance.timeline;

        /// <summary>
        /// メイドのチェック集合。無ければ作る。
        /// 新規作成時はタイムラインの既存 opt-in を取り込む
        /// (取り込まないと、直後の片方向同期が既存タグを全部消してしまう)
        /// </summary>
        public EditTargetStore GetStore(Maid maid)
        {
            EditTargetStore store;
            if (!_stores.TryGetValue(maid, out store))
            {
                store = new EditTargetStore();
                _stores[maid] = store;
                _gate.Invalidate();

                var timelineData = currentTimeline;
                var slotNo = GetMaidSlotNo(maid);
                if (timelineData != null && slotNo >= 0)
                {
                    store.SetNames(timelineData.GetMaidShapeKeys(slotNo));
                }
            }
            return store;
        }

        /// <summary>既存のチェック集合を引くだけで新規生成はしない (表示用)</summary>
        public EditTargetStore FindStore(Maid maid)
        {
            EditTargetStore store;
            return maid != null && _stores.TryGetValue(maid, out store) ? store : null;
        }

        public override void Update()
        {
            CleanupStores();
            SyncToTimeline();
        }

        /// <summary>破棄済みメイドの記録を掃除する (FaceEditManager と同じ方式)</summary>
        private void CleanupStores()
        {
            _deadMaids.Clear();
            foreach (var pair in _stores)
            {
                if (pair.Key == null)
                {
                    _deadMaids.Add(pair.Key);
                }
            }
            foreach (var maid in _deadMaids)
            {
                _stores.Remove(maid);
                _gate.Invalidate();
            }
        }

        /// <summary>
        /// チェック集合をタイムラインの opt-in へ流す。
        /// Add/RemoveMaidShapeKey は 0F 自動キーと全フレームキー削除を伴うため、
        /// 実際に変わったタグだけ呼ぶ
        /// </summary>
        private void SyncToTimeline()
        {
            var timelineData = currentTimeline;
            if (timelineData == null)
            {
                // タイムライン未ロード。次にロードされたときへ判定を持ち越す
                _lastTimeline = null;
                _gate.Reset();
                _hasUnresolvedMaid = false;
                return;
            }

            // タイムラインが差し替わったら、まずそちらの opt-in を取り込む。
            // 判定状態を更新する前に行うことで、取り込みで動いた version も次の判定で拾える
            if (timelineData != _lastTimeline)
            {
                ImportFromTimeline(timelineData);
            }

            var versionSum = 0;
            foreach (var pair in _stores)
            {
                versionSum += pair.Value.version;
            }

            // スロットを解決できなかったメイドは、解決しても version が動かないので
            // ここから定期的に取りに行く (放っておくとそのメイドのタグが二度と流れない)
            _unresolvedFrameCount++;
            if (_hasUnresolvedMaid && _unresolvedFrameCount >= UnresolvedRetryInterval)
            {
                _unresolvedFrameCount = 0;
                _gate.Invalidate();
            }

            if (timelineData == _lastTimeline && !_gate.IsChanged(versionSum))
            {
                return;
            }
            _lastTimeline = timelineData;
            _gate.MarkSynced(versionSum);

            _hasUnresolvedMaid = false;
            foreach (var pair in _stores)
            {
                var slotNo = GetMaidSlotNo(pair.Key);
                if (slotNo < 0)
                {
                    // タイムラインの管理外のメイド。記録は残し、上の再試行で拾い直す
                    _hasUnresolvedMaid = true;
                    continue;
                }

                ShapeKeySyncDiff.Compute(
                    pair.Value.GetNames(),
                    timelineData.GetMaidShapeKeys(slotNo),
                    _toAdd, _toRemove);

                foreach (var shapeKey in _toAdd)
                {
                    timelineData.AddMaidShapeKey(slotNo, shapeKey);
                }
                foreach (var shapeKey in _toRemove)
                {
                    timelineData.RemoveMaidShapeKey(slotNo, shapeKey);
                }
            }
        }

        /// <summary>
        /// タイムラインが持つ opt-in をストアへ取り込む。
        /// タグを持つスロットだけを反映し、空のスロットはストアを保持する。
        /// こうしないとタイムラインの新規作成でチェックが全部消える
        /// (新規タイムラインのマップは空)。
        /// 取り込み契機を OnLoad にしないのは、SE の ManagerRegistry.OnLoad が
        /// タイムラインのロードでは呼ばれない (UI の有効化でしか走らない) ため
        /// </summary>
        private void ImportFromTimeline(MTEP.TimelineData timelineData)
        {
            var maidManager = MTEP.MaidManager.instance;

            foreach (var pair in timelineData.maidShapeKeysMap)
            {
                if (pair.Value == null || pair.Value.Count == 0)
                {
                    continue;
                }

                var maidCache = maidManager.GetMaidCache(pair.Key);
                var maid = maidCache != null ? maidCache.maid : null;
                if (maid == null)
                {
                    continue;
                }

                // GetStore の初回生成も同じ内容を取り込むため、ここは上書きになっても等価
                GetStore(maid).SetNames(pair.Value);
            }
        }

        /// <summary>タイムライン側のメイドスロット番号。管理外なら -1</summary>
        private static int GetMaidSlotNo(Maid maid)
        {
            if (maid == null)
            {
                return -1;
            }
            var maidCache = MTEP.MaidManager.instance.GetMaidCache(maid);
            return maidCache != null ? maidCache.slotNo : -1;
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            // シーン遷移で全メイドが入れ替わるため記録を丸ごと捨てる (FaceEditManager と同じ方式)
            _stores.Clear();
            _lastTimeline = null;
            _gate.Reset();
            _hasUnresolvedMaid = false;
        }
    }
}
