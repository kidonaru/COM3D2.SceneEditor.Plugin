using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    using AttachPoint = PhotoTransTargetObject.AttachPoint;

    /// <summary>
    /// 外部プラグイン（ModelPlacerProvider 規約の実装者）へモデル配置を委譲する ModelHack。
    /// タイムラインは配置の所有者ではなく、プロバイダが列挙するモデルをキーフレーム対象として扱う
    /// </summary>
    public class ExternalModelHack : ModelHackBase
    {
        private readonly ModelPlacerProvider _provider;

        /// <summary>
        /// GameObject → StudioModelStat の対応。毎フレーム作り直すと
        /// BoneController / MaterialController が作り直されるためキャッシュする
        /// </summary>
        private readonly Dictionary<GameObject, StudioModelStat> _statMap
            = new Dictionary<GameObject, StudioModelStat>();

        private readonly List<StudioModelStat> _modelList = new List<StudioModelStat>();

        /// <summary>生存中 GameObject の集合。掃除のたびに作り直さないよう使い回す</summary>
        private readonly HashSet<GameObject> _aliveObjects = new HashSet<GameObject>();

        private Transform _unattachedParent;

        /// <summary>
        /// プロバイダがアタッチなしのモデルを置く親。プロバイダ API に取得手段が無いため、
        /// アタッチしていないと確定している時点 (生成直後・解除直後・メイド配下でない状態での発見時) の親を控える
        /// </summary>
        public override Transform unattachedParent => _unattachedParent;

        public override string pluginName => _provider.id;

        public ExternalModelHack(ModelPlacerProvider provider)
        {
            _provider = provider;
        }

        public override List<StudioModelStat> modelList
        {
            get
            {
                _modelList.Clear();

                var objects = SafeGetModels();

                // 生存判定を毎フレーム線形探索しないよう、集合にしてから突き合わせる
                _aliveObjects.Clear();
                foreach (var obj in objects)
                {
                    _aliveObjects.Add(obj);
                }
                CleanupDestroyed(_aliveObjects);

                foreach (var obj in objects)
                {
                    var stat = GetOrCreateStat(obj);
                    if (stat != null)
                    {
                        _modelList.Add(stat);
                    }
                }
                return _modelList;
            }
        }

        private List<GameObject> SafeGetModels()
        {
            try
            {
                return _provider.getModels() ?? new List<GameObject>();
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
                return new List<GameObject>();
            }
        }

        /// <summary>
        /// プロバイダの一覧から消えた GameObject のエントリを捨てる。
        /// Unity の null 判定が真でも Dictionary のキーとしては生きているため明示的に掃除する
        /// </summary>
        private void CleanupDestroyed(HashSet<GameObject> aliveObjects)
        {
            List<GameObject> deadKeys = null;
            foreach (var pair in _statMap)
            {
                if (pair.Key == null || !aliveObjects.Contains(pair.Key))
                {
                    if (deadKeys == null)
                    {
                        deadKeys = new List<GameObject>();
                    }
                    deadKeys.Add(pair.Key);
                }
            }
            if (deadKeys != null)
            {
                foreach (var key in deadKeys)
                {
                    _statMap.Remove(key);
                    _unresolvedAttachBones.Remove(key);
                }
            }
        }

        /// <summary>
        /// GameObject に対応する StudioModelStat を返す。
        /// group は ModelHackManager.FixGroup が未採番のものにだけ一度振り、以後は変えない
        /// (列挙順が変わっても name が動かないようにするため)。
        /// 作り直しの判定は fileName の変化だけで行う
        /// </summary>
        private StudioModelStat GetOrCreateStat(GameObject obj)
        {
            if (obj == null)
            {
                return null;
            }

            var fileName = SafeGetFileName(obj);

            if (_statMap.TryGetValue(obj, out var cached))
            {
                if (cached.info != null && cached.info.fileName == fileName)
                {
                    cached.visible = obj.activeSelf;
                    SyncAttachFromProvider(cached, obj);
                    SyncLayerFromProvider(cached, obj);
                    return cached;
                }
                _statMap.Remove(obj);
            }

            // プロバイダ側で直接置かれたモデルは生成を経ないのでここで控える。
            // 既にメイドへアタッチ済みならボーンを拾ってしまうため、親の上位にメイドがいないときだけ
            var parent = obj.transform.parent;
            if (_unattachedParent == null && parent != null && parent.GetComponentInParent<Maid>() == null)
            {
                _unattachedParent = parent;
            }

            var stat = modelManager.CreateModelStat(
                fileName,
                obj.transform,
                AttachPoint.Null,
                -1,
                obj,
                pluginName,
                obj.activeSelf);

            // プロバイダの列挙名にはグループ接尾辞が無く、CreateModelStat は接尾辞が無いと 0 を付ける。
            // 0 のままだと列挙順によっては新規 stat が既存の 0 番を押し出すため、明示的に未採番へ落とす
            stat.SetGroup(StudioModelStat.UnassignedGroup);
            SyncAttachFromProvider(stat, obj);
            SyncLayerFromProvider(stat, obj);

            _statMap[obj] = stat;
            return stat;
        }

        /// <summary>
        /// プロバイダ側の UI で切り替えた表示レイヤーを stat へ取り込む。
        /// プロバイダが扱えない (任意メンバが無い) 場合は未指定のまま
        /// </summary>
        private void SyncLayerFromProvider(StudioModelStat stat, GameObject obj)
        {
            if (_provider.getModelLayer == null)
            {
                return;
            }

            try
            {
                stat.layer = _provider.getModelLayer(obj);
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        /// <summary>取り込めなかったアタッチ先の控え (警告の重複を避ける)</summary>
        private readonly Dictionary<GameObject, Transform> _unresolvedAttachBones = new Dictionary<GameObject, Transform>();

        /// <summary>
        /// プロバイダ側の UI で付け替えたアタッチを stat へ取り込む。
        /// SE 自身の付け替えは stat を書いてからプロバイダを呼ぶので、ここで差分にはならない
        /// </summary>
        private void SyncAttachFromProvider(StudioModelStat stat, GameObject obj)
        {
            if (_provider.getModelAttachBone == null)
            {
                return;
            }

            Transform bone;
            try
            {
                bone = _provider.getModelAttachBone(obj);
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
                return;
            }

            AttachPoint point;
            int slotNo;
            if (!TryResolveAttach(bone, out point, out slotNo))
            {
                // 30 フレームごとに呼ばれるので、同じボーンについては 1 度だけ知らせる
                Transform warnedBone;
                if (!_unresolvedAttachBones.TryGetValue(obj, out warnedBone) || warnedBone != bone)
                {
                    _unresolvedAttachBones[obj] = bone;
                    MTEUtils.LogWarning(
                        "アタッチ先を SceneEditor の部位として表せないため、キーへ取り込みません: {0} → {1}",
                        stat.displayName, bone.name);
                }
                return;
            }

            _unresolvedAttachBones.Remove(obj);
            stat.attachPoint = point;
            stat.attachMaidSlotNo = slotNo;
        }

        /// <summary>
        /// 親ボーンを SE の部位とメイドのスロットへ戻す。未アタッチは true (Null / -1)。
        /// SE がキャッシュを持たないメイドや、部位一覧に無いボーンは false
        /// </summary>
        private static bool TryResolveAttach(Transform bone, out AttachPoint point, out int slotNo)
        {
            point = AttachPoint.Null;
            slotNo = -1;
            if (bone == null)
            {
                return true;
            }

            var maid = bone.GetComponentInParent<Maid>();
            var maidCache = maid != null ? maidManager.GetMaidCache(maid) : null;
            if (maidCache == null)
            {
                return false;
            }

            if (!ModelAttachPoints.TryFindAttachPoint(p => maidCache.GetAttachPointTransform(p), bone, out point))
            {
                return false;
            }

            slotNo = maidCache.slotNo;
            return true;
        }

        private string SafeGetFileName(GameObject obj)
        {
            try
            {
                return _provider.getModelFileName(obj) ?? obj.name;
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
                return obj.name;
            }
        }

        public override void CreateModel(StudioModelStat model)
        {
            var info = model.info;
            var obj = _provider.createModel(
                info.type.ToString(),
                info.fileName,
                info.myRoomId,
                info.bgObjectId,
                model.group,
                model.visible);
            if (obj == null)
            {
                MTEUtils.LogError("CreateModel: モデルの追加に失敗しました " + model.name);
                return;
            }

            // UpdateAttachPoint より前なので、ここでの親はプロバイダの配置ルート
            _unattachedParent = obj.transform.parent;

            model.transform = obj.transform;
            model.obj = obj;
            _statMap[obj] = model;

            UpdateAttachPoint(model);

            // 複製・読込で stat に載っているレイヤーを新しい実体へ移す
            UpdateLayer(model);
        }

        public override void DeleteModel(StudioModelStat model)
        {
            var obj = model.obj as GameObject;
            if (obj != null)
            {
                _statMap.Remove(obj);
                _provider.deleteModel(obj);
            }
            model.transform = null;
        }

        public override void DeleteAllModels()
        {
            _statMap.Clear();
            _provider.deleteAllModels();
        }

        public override void SetModelVisible(StudioModelStat model, bool visible)
        {
            var obj = model.obj as GameObject;
            if (obj != null)
            {
                _provider.setModelVisible(obj, visible);
            }
        }

        public override void UpdateLayer(StudioModelStat model)
        {
            var obj = model.obj as GameObject;
            if (obj == null || model.layer == StudioModelStat.UnspecifiedLayer
                || _provider.setModelLayer == null)
            {
                return;
            }

            // UI は StudioModelManager 側の複製 stat を渡してくるので、modelList が返す stat へも写す
            // (写さないと直後の LateUpdate で旧値との差分と誤判定される)
            if (_statMap.TryGetValue(obj, out var cached) && cached != model)
            {
                cached.layer = model.layer;
            }

            _provider.setModelLayer(obj, model.layer);
        }

        public override void UpdateAttachPoint(StudioModelStat model)
        {
            var obj = model.obj as GameObject;
            if (obj == null)
            {
                return;
            }

            // UI は StudioModelManager 側の複製 stat を編集して渡してくる。
            // modelList が返す stat へ写しておかないと、直後の LateUpdate で Null に巻き戻される
            if (_statMap.TryGetValue(obj, out var cached) && cached != model)
            {
                cached.attachPoint = model.attachPoint;
                cached.attachMaidSlotNo = model.attachMaidSlotNo;
            }

            // アタッチ先ボーンの解決は SE 側が行い、プロバイダへはボーン名だけ渡す。
            // AttachPoint enum → IKManager.BoneType の対応表をゲスト側に持たせずに済む
            var maidCache = maidManager.GetMaidCache(model.attachMaidSlotNo);
            var boneTransform = maidCache?.GetAttachPointTransform(model.attachPoint);
            var maid = boneTransform != null ? maidCache.maid : null;
            _provider.attachModel(obj, maid, boneTransform != null ? boneTransform.name : "");

            // 解除した直後の親は配置ルートと確定しているので控え直す
            if (maid == null)
            {
                _unattachedParent = obj.transform.parent;
            }
        }

        /// <summary>タイムライン読込のような一括操作をプロバイダへ伝える（任意メンバ）</summary>
        public void BeginBatch()
        {
            try
            {
                _provider.beginBatch?.Invoke();
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public void EndBatch()
        {
            try
            {
                _provider.endBatch?.Invoke();
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
        }
    }
}
