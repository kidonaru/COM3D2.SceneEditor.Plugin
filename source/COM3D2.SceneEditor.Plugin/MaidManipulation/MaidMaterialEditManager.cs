using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メイドスロットマテリアルの変更追跡。メイドごとに EditTargetStore を持つ。
    /// 記録する名前は ModelMaterial.name (= "{スロット名}/{Unity マテリアル名}") で、
    /// タイムライン側の候補名 (MaidCache.materialNames) と同じ文字列になるため修飾の変換は不要。
    /// 着替えで消えたマテリアルの記録は捨てない (現物に無ければ表示されないだけ)
    /// </summary>
    public class MaidMaterialEditManager : ManagerBase
    {
        private static MaidMaterialEditManager _instance;
        public static MaidMaterialEditManager instance
            => _instance ?? (_instance = new MaidMaterialEditManager());

        private MaidMaterialEditManager()
        {
        }

        private readonly Dictionary<Maid, EditTargetStore> _stores = new Dictionary<Maid, EditTargetStore>();
        private readonly List<Maid> _deadMaids = new List<Maid>();

        public EditTargetStore GetStore(Maid maid)
        {
            EditTargetStore store;
            if (!_stores.TryGetValue(maid, out store))
            {
                store = new EditTargetStore();
                _stores[maid] = store;
            }
            return store;
        }

        public EditTargetStore FindStore(Maid maid)
        {
            EditTargetStore store;
            return maid != null && _stores.TryGetValue(maid, out store) ? store : null;
        }

        public override void Update()
        {
            // 破棄済みメイドの記録を掃除する (FaceEditManager と同じ方式)
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
            }
        }

        public override void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            // シーン遷移で全メイドが入れ替わるため記録を丸ごと捨てる
            _stores.Clear();
        }
    }
}
