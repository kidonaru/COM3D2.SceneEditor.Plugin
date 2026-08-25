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
                CleanupDestroyed(objects);

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
        private void CleanupDestroyed(List<GameObject> aliveObjects)
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
                }
            }
        }

        /// <summary>
        /// GameObject に対応する StudioModelStat を返す。
        /// group は ModelHackManager.modelList の FixGroup が列挙順で振り直すため、
        /// ここでは触らない（プロバイダ側の採番と突き合わせると毎回作り直しになる）。
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
                    return cached;
                }
                _statMap.Remove(obj);
            }

            var stat = modelManager.CreateModelStat(
                fileName,
                obj.transform,
                AttachPoint.Null,
                -1,
                obj,
                pluginName,
                obj.activeSelf);

            _statMap[obj] = stat;
            return stat;
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
            try
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

                model.transform = obj.transform;
                model.obj = obj;
                _statMap[obj] = model;

                UpdateAttachPoint(model);
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public override void DeleteModel(StudioModelStat model)
        {
            try
            {
                var obj = model.obj as GameObject;
                if (obj != null)
                {
                    _statMap.Remove(obj);
                    _provider.deleteModel(obj);
                }
                model.transform = null;
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public override void DeleteAllModels()
        {
            try
            {
                _statMap.Clear();
                _provider.deleteAllModels();
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public override void SetModelVisible(StudioModelStat model, bool visible)
        {
            try
            {
                var obj = model.obj as GameObject;
                if (obj != null)
                {
                    _provider.setModelVisible(obj, visible);
                }
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public override void UpdateAttachPoint(StudioModelStat model)
        {
            try
            {
                var obj = model.obj as GameObject;
                if (obj == null)
                {
                    return;
                }

                // アタッチ先ボーンの解決は SE 側が行い、プロバイダへはボーン名だけ渡す。
                // AttachPoint enum → IKManager.BoneType の対応表をゲスト側に持たせずに済む
                var maidCache = maidManager.GetMaidCache(model.attachMaidSlotNo);
                var boneTransform = maidCache?.GetAttachPointTransform(model.attachPoint);
                var maid = boneTransform != null ? maidCache.maid : null;
                _provider.attachModel(obj, maid, boneTransform != null ? boneTransform.name : "");
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
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
