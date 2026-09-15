using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// チェック付き複数選択コンボ。行の見た目は基底 (GUIView.DrawPopupRow) と共通で、
    /// 項目クリックでは閉じずに連続で切り替えられる点だけが違う。
    /// 閉じるのは外側クリックで、ComboBoxPopupWindow の既存機構が担う
    /// </summary>
    public class GUIMultiSelectComboBox<T> : GUIComboBox<T>
    {
        public Func<T, int, bool> getChecked;
        public Action<T, int> onToggle;

        /// <summary>先頭に置く一括操作行のラベル。null なら一括操作行を出さない</summary>
        public Func<string> getHeaderName;
        /// <summary>一括操作行のチェック状態</summary>
        public Func<bool> getHeaderChecked;
        /// <summary>一括操作行がクリックされたときの処理</summary>
        public Action onHeader;

        private bool hasHeader => getHeaderName != null;

        protected override int popupRowCount => items.Count + (hasHeader ? 1 : 0);

        public override bool DrawPopupContent(GUIView view)
        {
            var itemWidth = view.BeginPopupList(popupRowCount);
            {
                if (hasHeader)
                {
                    var isOn = getHeaderChecked != null && getHeaderChecked();
                    if (view.DrawPopupRow(getHeaderName(), isOn, itemWidth) && onHeader != null)
                    {
                        onHeader();
                    }
                }

                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var isOn = getChecked != null && getChecked(item, i);
                    // currentIndex の行はアクセント色。アクティブなレイヤーを示す
                    var color = i == currentIndex ? GUIView.option.accentColor : Color.white;
                    var enabled = getEnabled == null || getEnabled(item, i);

                    if (view.DrawPopupRow(getName(item, i), isOn, itemWidth, enabled, color)
                        && onToggle != null)
                    {
                        onToggle(item, i);
                    }
                }
            }
            view.EndPopupList();

            // 連続で切り替えられるようポップアップは閉じない
            return false;
        }
    }
}
