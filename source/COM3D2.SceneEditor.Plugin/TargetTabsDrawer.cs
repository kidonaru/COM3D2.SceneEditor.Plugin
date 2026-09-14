using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ラベル + 番号タブ + 「追加」「削除」ボタンの 1 行。
    /// ライブ演出のコントローラー・ライト、テキスト、動画など
    /// 「同種の対象が複数あって 1 つを選んで編集する」行で共有する。
    /// タブの見出しは 1 始まり、選択添字は 0 始まり
    /// </summary>
    public static class TargetTabsDrawer
    {
        /// <summary>操作対象タブ 1 つぶんの幅 (2 桁の番号が収まる幅)</summary>
        private const float TabWidth = 30f;

        /// <summary>番号が 3 桁以上になったときに 1 桁あたり広げる幅</summary>
        private const float TabDigitWidth = 10f;

        /// <summary>操作対象の増減ボタンの幅 (「追加」「削除」が収まる幅)</summary>
        private const float EditButtonWidth = 50f;

        /// <summary>
        /// 末尾の番号タブと「追加」ボタンの間隔。
        /// 番号を押すつもりで「追加」を踏むのを防ぐため、通常の margin より広く空ける
        /// </summary>
        private const float EditButtonGap = 20f;

        /// <summary>
        /// 既定のラベル幅 (ライブ演出の「コントローラー」が収まり、
        /// 同じタブ内の「コントローラー数」等の行と左端が揃う幅)。
        /// 他のウィンドウはラベル文字列に合わせて上書きする
        /// </summary>
        public const float DefaultLabelWidth = 100f;

        /// <summary>
        /// 一覧から操作対象を選ぶ番号タブ列を描く。label には何を選んでいるか
        /// (コントローラー・ライト等) が分かる名前を渡す。
        /// 対象の増減で添字がはみ出しても選択が外れないよう範囲内へ丸める。
        /// onAdd / onRemove を渡すと番号の右へ「追加」「削除」ボタンを置き、
        /// canAdd / canRemove が false のボタンは描くが非活性にする (上限・下限での無効化用)。
        /// 一覧が空なら番号の代わりに「&lt;label&gt;が存在しません」を描く。
        /// 増減ボタンは空でも描くので、対象が 0 個になっても追加できる
        /// </summary>
        /// <returns>選択中の対象。一覧が空なら null</returns>
        public static T Draw<T>(
            GUIView view, string label, IList<T> items, ref int index, float rowHeight,
            Action onAdd = null, Action onRemove = null,
            bool canAdd = true, bool canRemove = true,
            float labelWidth = DefaultLabelWidth) where T : class
        {
            Draw(view, label, items.Count, ref index, rowHeight,
                onAdd, onRemove, canAdd, canRemove, labelWidth);

            return items.Count > 0 ? items[index] : null;
        }

        /// <summary>
        /// 対象数だけを渡す版。テキスト・動画のように実体が配列で持たれていて
        /// IList として取り出せない対象で使う。選択添字は index へ丸めて返す
        /// </summary>
        public static void Draw(
            GUIView view, string label, int count, ref int index, float rowHeight,
            Action onAdd = null, Action onRemove = null,
            bool canAdd = true, bool canRemove = true,
            float labelWidth = DefaultLabelWidth)
        {
            // 前フレームから対象が減っていることがあるため、描く前に丸めておく
            index = Mathf.Clamp(index, 0, Mathf.Max(0, count - 1));

            view.BeginHorizontal();
            {
                view.DrawLabel(label, labelWidth, rowHeight);

                // 番号タブの右へ回すぶんの幅。タブとボタンの間の margin と間隔も含める
                var buttonCount = (onAdd != null ? 1 : 0) + (onRemove != null ? 1 : 0);
                var buttonsWidth = buttonCount > 0
                    ? buttonCount * (EditButtonWidth + view.margin) + view.margin
                        + EditButtonGap + view.margin
                    : 0f;

                if (count > 0)
                {
                    DrawNumberTabs(view, count, ref index, rowHeight, buttonsWidth);

                    if (buttonCount > 0)
                    {
                        view.AddSpace(EditButtonGap, rowHeight);
                    }
                }

                if (onAdd != null && view.DrawButton("追加", EditButtonWidth, rowHeight, canAdd))
                {
                    onAdd();
                }
                if (onRemove != null &&
                    view.DrawButton("削除", EditButtonWidth, rowHeight, canRemove && count > 0))
                {
                    onRemove();
                }

                if (count == 0)
                {
                    view.DrawLabel(label + "が存在しません", 200, rowHeight);
                }
            }
            view.EndLayout();

            // 増減ボタンは対象を即座に変える。末尾を選んだまま減らすと
            // 添字が範囲外に残るため、参照する前に丸め直す
            index = Mathf.Clamp(index, 0, Mathf.Max(0, count - 1));
        }

        /// <summary>
        /// 番号見出しの配列。OnGUI は 1 フレームに複数回走るため、
        /// 要素数ごとに 1 度だけ作って使い回す
        /// </summary>
        private static readonly Dictionary<int, string[]> _numberLabelsCache
            = new Dictionary<int, string[]>();

        private static string[] GetNumberLabels(int count)
        {
            string[] labels;
            if (!_numberLabelsCache.TryGetValue(count, out labels))
            {
                labels = new string[count];
                for (int i = 0; i < count; i++)
                {
                    labels[i] = (i + 1).ToString();
                }
                _numberLabelsCache[count] = labels;
            }
            return labels;
        }

        /// <summary>
        /// 番号タブを、行末のボタンぶん (reservedWidth) を空けた幅に収めて描く。
        /// DrawTabs は与えられたビューの幅いっぱいまで折り返すため、
        /// 幅を詰めたサブビューに閉じ込めないとボタンが行から押し出される。
        /// 幅と行数は GUIView.DrawTabs 内部の折り返し計算を先読みしているので、
        /// あちらの式を変えたらここも合わせること
        /// </summary>
        private static void DrawNumberTabs(
            GUIView view, int count, ref int index, float rowHeight, float reservedWidth)
        {
            var tabWidth = GetTabWidth(count);
            var maxWidth = view.viewRect.width - view.currentPos.x - view.padding.x - reservedWidth;
            // 幅が足りなくても 1 タブぶんは確保する (0 幅で行数計算が壊れないように)
            var subViewWidth = Mathf.Clamp(tabWidth * count, tabWidth, Mathf.Max(maxWidth, tabWidth));
            var rows = Mathf.CeilToInt(tabWidth * count / subViewWidth);

            var subView = view.BeginSubView(
                view.GetDrawRect(subViewWidth, rowHeight * rows), GUIView.LayoutDirection.Vertical);
            {
                // string[] のまま渡すと enum 版 DrawTabs<T> に解決されるため、
                // 見出し列は IList<string> として渡す
                IList<string> labels = GetNumberLabels(count);
                index = subView.DrawTabs(labels, index, tabWidth, rowHeight);
            }
            view.EndSubView();
        }

        /// <summary>対象数の桁数に合わせたタブ幅。3 桁以上でも番号が欠けないようにする</summary>
        private static float GetTabWidth(int count)
        {
            var digits = count.ToString().Length;
            return TabWidth + Mathf.Max(0, digits - 2) * TabDigitWidth;
        }
    }
}
