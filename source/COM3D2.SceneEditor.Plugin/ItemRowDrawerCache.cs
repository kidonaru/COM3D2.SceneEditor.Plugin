using System.Collections.Generic;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// メニュー項目ごとに行ドロワーを持たせるためのキャッシュ。
    ///
    /// 回転行のドロワーが持つ EulerOffsetCache は「同時に 1 対象のみ」を前提に
    /// 直近 1 件だけを保持する設計のため、複数項目を 1 フレームで順に描くと
    /// 最後の項目に上書きされ続け、±90 度付近の角度表示が跳ねる。
    /// 項目ごとに別インスタンスを配って前提を満たす
    /// </summary>
    public class ItemRowDrawerCache<TDrawer> where TDrawer : new()
    {
        private readonly Dictionary<string, TDrawer> _drawers =
            new Dictionary<string, TDrawer>();

        private readonly List<string> _staleNames = new List<string>();

        public TDrawer Get(string itemName)
        {
            TDrawer drawer;
            if (!_drawers.TryGetValue(itemName, out drawer))
            {
                drawer = new TDrawer();
                _drawers[itemName] = drawer;
            }
            return drawer;
        }

        /// <summary>選択から外れた項目のドロワーを捨てる (描画ループの最後に呼ぶ)</summary>
        public void PruneExcept(IList<MTEP.IBoneMenuItem> items)
        {
            if (_drawers.Count == items.Count)
            {
                return;
            }

            _staleNames.Clear();
            foreach (var name in _drawers.Keys)
            {
                if (!ContainsName(items, name))
                {
                    _staleNames.Add(name);
                }
            }
            foreach (var name in _staleNames)
            {
                _drawers.Remove(name);
            }
        }

        private static bool ContainsName(IList<MTEP.IBoneMenuItem> items, string name)
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].name == name)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
