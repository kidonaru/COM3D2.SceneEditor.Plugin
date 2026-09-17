using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// チェック集合 (EditTargetStore) からタイムラインの opt-in 集合
    /// (TimelineData.maidShapeKeysMap) への片方向同期の差分計算。
    /// タイムライン側の Add/RemoveMaidShapeKey は 0F 自動キーやキー削除を伴う重い操作なので、
    /// 実際に変わった分だけ呼べるようここで差分を出す
    /// </summary>
    public static class ShapeKeySyncDiff
    {
        /// <summary>
        /// desired (チェック集合) と current (タイムラインの現状) を突き合わせる。
        /// toAdd / toRemove は呼び出しごとにクリアしてから詰める
        /// </summary>
        public static void Compute(
            IEnumerable<string> desired,
            ICollection<string> current,
            List<string> toAdd,
            List<string> toRemove)
        {
            toAdd.Clear();
            toRemove.Clear();

            var desiredSet = new HashSet<string>();
            if (desired != null)
            {
                foreach (var name in desired)
                {
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }
                    desiredSet.Add(name);
                }
            }

            foreach (var name in desiredSet)
            {
                if (!current.Contains(name))
                {
                    toAdd.Add(name);
                }
            }

            foreach (var name in current)
            {
                if (!desiredSet.Contains(name))
                {
                    toRemove.Add(name);
                }
            }
        }
    }
}
