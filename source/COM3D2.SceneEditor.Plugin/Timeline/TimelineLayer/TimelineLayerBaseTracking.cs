using System.Collections.Generic;
using System.Linq;
using COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// 変更追跡ストア (EditTargetStore) 連動のボーン絞り込み。
    /// trackedCandidateNames を override したレイヤーだけが opt-in する。
    /// チェック済み ∪ 既存キーフレーム記載を対象集合とし、チェック追加で 0F 自動キー、
    /// 解除でキー自動削除を行う (表情レイヤーで確立した仕組みの汎用化)
    /// </summary>
    public abstract partial class TimelineLayerBase
    {
        /// <summary>変更追跡ストア。対象未選択などで null を返してよい</summary>
        protected virtual EditTargetStore trackedStore => null;

        /// <summary>絞り込み候補の全ボーン名 (正準順)。null なら絞り込み無効</summary>
        protected virtual List<string> trackedCandidateNames => null;

        /// <summary>自動キー登録/削除の履歴表示に使う接頭辞 (例: "表情")</summary>
        protected virtual string trackedHistoryPrefix => "";

        /// <summary>
        /// 0F 自動キーを打てる状態か。既定はメイド対象レイヤー向け。
        /// モデル対象レイヤーは maid を持たないため override して差し替える
        /// </summary>
        protected virtual bool isTrackedTargetReady => maid != null;

        protected bool hasTrackedBoneFilter => trackedCandidateNames != null;

        /// <summary>store 以外由来の集合変化を拾うための再計算間隔 (フレーム)</summary>
        private const int TrackedRebuildInterval = 30;

        private List<string> _trackedBoneNames;
        private int _lastTrackedStoreVersion = -1;
        private EditTargetStore _lastTrackedStore;
        private int _trackedRebuildFrameCount;
        private HashSet<string> _lastTrackedCheckedNames = new HashSet<string>();

        /// <summary>絞り込み後の対象集合。opt-in レイヤーは allBoneNames の実装に使う</summary>
        protected List<string> trackedBoneNames
            => _trackedBoneNames ?? (_trackedBoneNames = BuildTrackedBoneNames());

        /// <summary>
        /// チェック済み ∪ 既存キーフレーム記載。表示順は trackedCandidateNames に揃える。
        /// 既存キーフレーム分を含めるのは、読み込んだアニメの項目を未チェックでも編集できるようにするため。
        /// チェックを外した項目は RemoveTrackedKeys でキーごと消えるため、ここには残らない
        /// </summary>
        private List<string> BuildTrackedBoneNames()
        {
            var store = trackedStore;

            var keyFrameNames = new HashSet<string>();
            foreach (var frame in keyFrames)
            {
                foreach (var name in frame.boneNames)
                {
                    keyFrameNames.Add(name);
                }
            }

            var result = new List<string>();
            foreach (var name in trackedCandidateNames)
            {
                if ((store != null && store.IsModified(name)) || keyFrameNames.Contains(name))
                {
                    result.Add(name);
                }
            }
            return result;
        }

        /// <summary>
        /// 候補テーブルに存在するチェック済み項目。表示対象の算出に使う。
        /// チェック解除の判定には使わない (候補テーブルが縮んだだけの項目を解除と誤認するため)
        /// </summary>
        private HashSet<string> BuildTrackedCheckedNames(EditTargetStore store)
        {
            var result = new HashSet<string>();
            if (store == null)
            {
                return result;
            }

            foreach (var name in trackedCandidateNames)
            {
                if (store.IsModified(name))
                {
                    result.Add(name);
                }
            }
            return result;
        }

        /// <summary>
        /// 追跡集合のキャッシュを捨てる。
        /// 候補名リスト (trackedCandidateNames) がレイヤー都合で入れ替わったとき
        /// (モデルの追加・削除など) に呼ぶ。次の参照で作り直され、
        /// 間引きも解除するため UpdateTrackedBoneFilter が次フレームで必ず走る
        /// </summary>
        protected void InvalidateTrackedBoneNames()
        {
            _trackedBoneNames = null;
            _trackedRebuildFrameCount = TrackedRebuildInterval;
        }

        /// <summary>Update から毎フレーム呼ぶ。opt-in レイヤー以外では呼ばれない</summary>
        protected void UpdateTrackedBoneFilter()
        {
            // チェック変更 (store.version) は毎フレームの整数比較だけで検知する。
            // キー削除など store 以外由来の集合変化は 30 フレームごとの間引き再計算で拾う
            // (BuildTrackedBoneNames は keyFrames のフルコピーを伴うため毎フレームは回さない)
            var store = trackedStore;
            var version = store != null ? store.version : -1;
            var storeChanged = store != _lastTrackedStore || version != _lastTrackedStoreVersion;

            _trackedRebuildFrameCount++;
            if (!storeChanged && _trackedRebuildFrameCount < TrackedRebuildInterval)
            {
                return;
            }
            _trackedRebuildFrameCount = 0;

            // 対象が入れ替わったときは前回のチェック集合を引き継がない
            // (別対象のチェック解除とみなしてキーを消さないようにするため)
            var targetChanged = store != _lastTrackedStore;
            _lastTrackedStore = store;
            _lastTrackedStoreVersion = version;

            var checkedNames = BuildTrackedCheckedNames(store);
            if (!targetChanged)
            {
                // チェックを外した項目はキーごと消す。0F 目の自動登録と対称にしないと、
                // 自動登録されたキーが残り続けてボーンメニューから消えなくなる。
                //
                // 判定は候補テーブル由来の checkedNames ではなくストアへ直接問い合わせる。
                // 候補テーブルはメイドの着替えなどで縮むことがあり、そこから落ちただけの項目まで
                // 「チェック解除」とみなすと、ユーザーが作ったキーフレームが黙って消える
                RemoveTrackedKeys(
                    _lastTrackedCheckedNames.Where(name => store == null || !store.IsModified(name)).ToList());
            }
            _lastTrackedCheckedNames = checkedNames;

            var newNames = BuildTrackedBoneNames();
            if (_trackedBoneNames == null || !newNames.SequenceEqual(_trackedBoneNames))
            {
                // 初回構築 (_trackedBoneNames == null) は既存の対象を並べ直すだけなので追加扱いにしない
                var addedNames = _trackedBoneNames != null
                    ? newNames.Except(_trackedBoneNames).ToList()
                    : new List<string>();

                // UpdateFrame は allBoneNames を回すため、キー登録より先にキャッシュを差し替える
                _trackedBoneNames = newNames;
                InitMenuItems();

                AddTrackedFirstFrameKeys(addedNames);
            }
        }

        /// <summary>
        /// 新たに対象へ入った項目に 0F 目のキーを打つ。
        /// レイヤーの Update はタイムライン読み込み中しか回らないため、
        /// 読み込み済みのときにチェックを入れた場合だけ発火する。
        /// 0F 目にキーが無いとその項目はアニメの起点を持てないので、
        /// チェックした時点の現在値を基準値として登録する
        /// </summary>
        private void AddTrackedFirstFrameKeys(List<string> boneNames)
        {
            if (boneNames.Count == 0 || !isTrackedTargetReady)
            {
                return;
            }

            var tmpFrame = CreateFrame(0);
            UpdateFrame(tmpFrame, initialEdit: false, force: true);

            var bones = tmpFrame.GetFilterBones(boneNames);
            if (bones.Count == 0)
            {
                return;
            }

            UpdateBones(0, bones);
            ApplyCurrentFrame(true);

            timelineManager.RequestHistory(trackedHistoryPrefix + "キーフレーム自動登録");
        }

        /// <summary>チェックを外した項目のキーを全フレームから消す</summary>
        private void RemoveTrackedKeys(List<string> boneNames)
        {
            if (boneNames.Count == 0)
            {
                return;
            }

            var removed = false;
            foreach (var frame in _keyFrames)
            {
                var bones = frame.GetFilterBones(boneNames);
                if (bones.Count > 0)
                {
                    frame.RemoveBones(bones);
                    removed = true;
                }
            }

            if (!removed)
            {
                return;
            }

            CleanFrames();
            ApplyCurrentFrame(true);

            timelineManager.RequestHistory(trackedHistoryPrefix + "キーフレーム自動削除");
        }
    }
}
