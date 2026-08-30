using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// チェック付き複数選択コンボ。ドロップダウンの見た目・操作感は
    /// MenuBarWindow のポップアップに合わせてある (ホバー塗り + ラベル調の行 +
    /// 「✓ 」前置、項目クリックでは閉じずに連続で切り替えられる)。
    /// 閉じるのは外側クリックで、ComboBoxPopupWindow の既存機構が担う。
    /// MTEUtils は submodule のため本体を変えず SE 側の派生で拡張する
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

        private int rowCount => items.Count + (hasHeader ? 1 : 0);

        /// <summary>
        /// ポップアップのサイズ。メニューバーと同じ行高・枠幅で計算する。
        /// 基底は「幅 +20 (スクロールバー分)」だが、こちらは項目側を狭めるため加算しない
        /// </summary>
        public override Vector2 GetPopupSize()
        {
            var frame = MenuBarWindow.FRAME * 2;
            var height = Mathf.Min(
                contentSize.y,
                MenuBarWindow.ITEM_HEIGHT * rowCount + frame);
            // 空リストでも枠が潰れないよう 1 行分は確保する
            height = Mathf.Max(height, MenuBarWindow.ITEM_HEIGHT + frame);
            return new Vector2(contentSize.x, height);
        }

        public override bool DrawPopupContent(GUIView view)
        {
            // 枠の内側からスクロール領域を始める。padding だとスクロール内の
            // 項目座標にも加算されてずれるため、currentPos で位置だけ寄せる
            view.currentPos = new Vector2(MenuBarWindow.FRAME, MenuBarWindow.FRAME);

            var viewWidth = view.viewRect.width - MenuBarWindow.FRAME * 2;
            var viewHeight = view.viewRect.height - MenuBarWindow.FRAME * 2;
            var contentHeight = MenuBarWindow.ITEM_HEIGHT * rowCount;

            // 収まらないときはスクロールバーが出る分だけ項目を狭め、横スクロールを出さない
            var itemWidth = contentHeight > viewHeight
                ? viewWidth - MenuBarWindow.SCROLLBAR_WIDTH
                : viewWidth;

            view.BeginScrollView(viewWidth, viewHeight,
                new Rect(0, 0, itemWidth, contentHeight), false, false);
            {
                if (hasHeader)
                {
                    var isOn = getHeaderChecked != null && getHeaderChecked();
                    if (DrawRow(view, itemWidth, getHeaderName(), isOn, true, Color.white)
                        && onHeader != null)
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

                    if (DrawRow(view, itemWidth, getName(item, i), isOn, enabled, color)
                        && onToggle != null)
                    {
                        onToggle(item, i);
                    }
                }
            }
            view.EndScrollView();

            // 連続で切り替えられるようポップアップは閉じない
            return false;
        }

        /// <summary>チェック付きの 1 行を描く。押されたら true</summary>
        private bool DrawRow(
            GUIView view, float itemWidth, string name, bool isOn, bool enabled, Color color)
        {
            // label スタイルはホバー反応を持たないため自前で塗る。
            // GetDrawRect は currentPos を進めないので直後のボタンと同じ矩形になる
            var rect = view.GetDrawRect(itemWidth, MenuBarWindow.ITEM_HEIGHT);
            if (rect.Contains(Event.current.mousePosition))
            {
                view.BeginColor(MenuBarWindow.ITEM_HOVER_COLOR);
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                view.EndColor();
            }

            var label = (isOn ? "✓ " : "    ") + name;
            return view.DrawButton(label, itemWidth, MenuBarWindow.ITEM_HEIGHT,
                enabled, color, GUIView.gsLabel);
        }
    }
}
