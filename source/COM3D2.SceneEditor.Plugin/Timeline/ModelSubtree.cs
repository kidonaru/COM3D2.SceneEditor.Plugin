using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// モデル自身の部分木だけを探す GetComponentInChildren。
    /// 別のモデルを子へアタッチすると、そのモデルのレンダラー等まで親のものとして拾ってしまうため、
    /// プロバイダが列挙する別モデルの根の下は探さない。
    /// SE 側のコントローラの有無で判定すると、stat の生成順によっては子がまだ持っておらず取りこぼすため使わない
    /// </summary>
    public static class ModelSubtree
    {
        private static readonly HashSet<GameObject> _modelRoots = new HashSet<GameObject>();
        private static int _cachedFrame = -1;

        /// <summary>プロバイダの列挙は割り当てを伴うので、1 フレームに 1 回だけ作り直す</summary>
        private static bool IsModelRoot(Transform transform)
        {
            if (_cachedFrame != Time.frameCount)
            {
                _cachedFrame = Time.frameCount;
                _modelRoots.Clear();
                foreach (var entry in COM3D2.SceneEditor.Plugin.ModelProviderHost.GetModels())
                {
                    if (entry.obj != null)
                    {
                        _modelRoots.Add(entry.obj);
                    }
                }
            }
            return _modelRoots.Contains(transform.gameObject);
        }

        /// <summary>GetComponentInChildren と同じく、非アクティブな GameObject は探さない</summary>
        public static T FindComponent<T>(Transform root) where T : Component
        {
            if (root == null || !root.gameObject.activeInHierarchy)
            {
                return null;
            }

            var component = root.GetComponent<T>();
            if (component != null)
            {
                return component;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (IsModelRoot(child))
                {
                    continue;
                }
                var found = FindComponent<T>(child);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}
