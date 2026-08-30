using System.Collections.Generic;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 任意の GameObject を IModelStat として扱う軽量ラッパー。
    /// ModelProviderHost 提供モデルや背景オブジェクトのマテリアル編集に使う。
    /// ModelMaterialController は GameObject 単位で冪等なため、
    /// タイムラインの StudioModelStat と同じ GameObject を指しても同一コントローラを共有する
    /// </summary>
    public class ProviderModelStat : MTEP.IModelStat
    {
        public GameObject obj { get; private set; }
        public string name => obj != null ? obj.name : "";
        public string displayName { get; private set; }
        public Transform transform => obj != null ? obj.transform : null;

        private MTEP.ModelMaterialController _materialController;

        public List<MTEP.ModelMaterial> materials
        {
            get
            {
                if (obj == null)
                {
                    return new List<MTEP.ModelMaterial>();
                }
                if (_materialController == null)
                {
                    _materialController = MTEP.ModelMaterialController.GetOrCreate(this);
                }
                return _materialController != null
                    ? _materialController.materials
                    : new List<MTEP.ModelMaterial>();
            }
        }

        private ProviderModelStat(GameObject obj, string displayName)
        {
            this.obj = obj;
            this.displayName = displayName;
        }

        private static readonly Dictionary<GameObject, ProviderModelStat> _cache
            = new Dictionary<GameObject, ProviderModelStat>();

        public static ProviderModelStat GetOrCreate(GameObject obj, string displayName)
        {
            if (obj == null)
            {
                return null;
            }

            ProviderModelStat stat;
            if (!_cache.TryGetValue(obj, out stat))
            {
                stat = new ProviderModelStat(obj, displayName);
                _cache.Add(obj, stat);
            }
            stat.displayName = displayName;
            return stat;
        }

        /// <summary>
        /// Destroy 済み GameObject のエントリを掃除する。
        /// Unity の null 判定が真でも Dictionary のキーとしては生きているため、列挙前に呼ぶ
        /// </summary>
        public static void CleanupDestroyed()
        {
            List<GameObject> deadKeys = null;
            foreach (var pair in _cache)
            {
                if (pair.Key == null)
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
                    _cache.Remove(key);
                }
            }
        }
    }
}
