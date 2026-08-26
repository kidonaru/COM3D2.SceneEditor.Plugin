using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 配置モデルマテリアルの変更追跡。
    /// 記録はモデル (GameObject) ごとに Unity マテリアル名 (ModelMaterial.displayName) で持ち、
    /// タイムラインへ渡すモデル修飾名は集約側で毎回組み直す。
    /// 修飾名を直接記録に使わないのは、group 振り直しで名前が変わりうるため
    /// (ModelHackManager.FixGroup)
    /// </summary>
    public class ModelMaterialEditManager : ManagerBase
    {
        private static ModelMaterialEditManager _instance;
        public static ModelMaterialEditManager instance
            => _instance ?? (_instance = new ModelMaterialEditManager());

        private ModelMaterialEditManager()
        {
        }

        private readonly Dictionary<GameObject, EditTargetStore> _stores
            = new Dictionary<GameObject, EditTargetStore>();

        private readonly List<GameObject> _deadModels = new List<GameObject>();

        private readonly ModelTrackedNameStore<GameObject> _tracked
            = new ModelTrackedNameStore<GameObject>();

        // Sync へ毎フレーム渡すキー列。使い回してゴミを出さない
        private readonly List<GameObject> _keys = new List<GameObject>();

        /// <summary>モデル修飾名の集約。ModelMaterialTimelineLayer の追跡ストアとして使う</summary>
        public EditTargetStore trackedStore => _tracked.store;

        /// <summary>モデルのチェック集合 (Unity マテリアル名)。無ければ作る</summary>
        public EditTargetStore GetStore(GameObject model)
        {
            EditTargetStore store;
            if (!_stores.TryGetValue(model, out store))
            {
                store = new EditTargetStore();
                _stores[model] = store;
                _tracked.Invalidate();
            }
            return store;
        }

        /// <summary>既存のチェック集合を引くだけで新規生成はしない (表示用)</summary>
        public EditTargetStore FindStore(GameObject model)
        {
            EditTargetStore store;
            return model != null && _stores.TryGetValue(model, out store) ? store : null;
        }

        public override void Init()
        {
            // モデルの増減で group が振り直され、モデル修飾名が変わる。
            // 記録側の version は動かないため、一覧が変わった契機で集約を作り直させる。
            // OnPluginDisable は UI をトグルするたび呼ばれるため、そこで解除すると
            // UI を一度閉じただけで購読が復活しなくなる (ModelShapeKeyEditManager と同じ理由で解除しない)
            MTEP.StudioModelManager.onModelAdded += OnTimelineModelChanged;
            MTEP.StudioModelManager.onModelRemoved += OnTimelineModelChanged;
        }

        private void OnTimelineModelChanged(MTEP.StudioModelStat model)
        {
            _tracked.Invalidate();
        }

        public override void Update()
        {
            CleanupStores();
            SyncTrackedStore();
        }

        /// <summary>削除されたモデルの記録を捨てる</summary>
        private void CleanupStores()
        {
            _deadModels.Clear();
            foreach (var pair in _stores)
            {
                if (pair.Key == null)
                {
                    _deadModels.Add(pair.Key);
                }
            }
            foreach (var model in _deadModels)
            {
                _stores.Remove(model);
                _tracked.Invalidate();
            }
        }

        private void SyncTrackedStore()
        {
            _keys.Clear();
            foreach (var pair in _stores)
            {
                _keys.Add(pair.Key);
            }

            _tracked.Sync(
                _keys,
                model => _stores[model].version,
                (model, result) =>
                {
                    var modelName = GetModelName(model);
                    if (modelName == null)
                    {
                        return false;
                    }
                    foreach (var materialName in _stores[model].GetNames())
                    {
                        var name = ModelQualifiedNames.Qualify(modelName, materialName);
                        if (name != null)
                        {
                            result.Add(name);
                        }
                    }
                    return true;
                });
        }

        /// <summary>
        /// モデルルートからタイムライン側のモデル名を引く。取れなければ null。
        /// ModelMaterialController.model は GetOrCreate のたび上書きされ、
        /// MaterialEditWindow が触ると ProviderModelStat へ差し替わるため参照しない。
        /// StudioModelManager の一覧はタイムラインのロード後にしか埋まらないので、
        /// 未ロードのうちは解決できず集約側の再試行に任せる
        /// </summary>
        private static string GetModelName(GameObject model)
        {
            if (model == null)
            {
                return null;
            }

            foreach (var stat in MTEP.StudioModelManager.instance.models)
            {
                if (stat != null && stat.transform != null && stat.transform.gameObject == model)
                {
                    return stat.name;
                }
            }
            return null;
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            // シーン遷移でモデルが全て入れ替わるため記録を丸ごと捨てる
            _stores.Clear();
            _tracked.Clear();
        }
    }
}
