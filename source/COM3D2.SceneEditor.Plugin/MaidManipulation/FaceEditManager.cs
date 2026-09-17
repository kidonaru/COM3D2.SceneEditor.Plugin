using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>メイドごとの EditTargetStore を管理する。消えたメイドの記録は毎フレームの掃除で捨てる</summary>
    public class FaceEditManager : ManagerBase
    {
        private static FaceEditManager _instance;
        public static FaceEditManager instance => _instance ?? (_instance = new FaceEditManager());

        private FaceEditManager()
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
            // 破棄済みメイドの記録を掃除する (BoneEditManager.UpdateStores と同じ方式)
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
            // シーン遷移で全メイドが入れ替わるため記録を丸ごと捨てる (BoneEditManager と同じ方式)
            _stores.Clear();
        }
    }
}
