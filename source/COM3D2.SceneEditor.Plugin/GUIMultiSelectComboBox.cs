using System;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// チェック付き複数選択コンボ。メニューバーのポップアップと同じ流儀で、
    /// 項目クリックでは閉じずにチェックをトグルする (閉じるのは外側クリック)。
    /// MTEUtils は submodule のため本体を変えず SE 側の派生で拡張する
    /// </summary>
    public class GUIMultiSelectComboBox<T> : GUIComboBox<T>
    {
        public Func<T, int, bool> getChecked;
        public Action<T, int> onToggle;

        public override bool DrawPopupContent(GUIView view)
        {
            var selectedIndex = view.DrawListView(
                items,
                GetCheckedName,
                getEnabled,
                view.viewRect.width,
                view.viewRect.height,
                currentIndex,
                buttonSize.y);

            if (selectedIndex >= 0 && selectedIndex < items.Count)
            {
                onToggle?.Invoke(items[selectedIndex], selectedIndex);
            }

            // 連続で切り替えられるようポップアップは閉じない
            return false;
        }

        private string GetCheckedName(T item, int index)
        {
            var isOn = getChecked != null && getChecked(item, index);
            return (isOn ? "✓ " : "　 ") + getName(item, index);
        }
    }
}
