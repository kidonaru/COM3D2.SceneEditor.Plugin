using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// レイヤー項目の現在値 UI に置く、値のコピー / 貼り付けメニューの描画ヘルパー。
    /// 見出し行を持つ項目は見出しの右端、1 行で完結する項目は行の右端に置く
    /// </summary>
    public static class TimelineItemClipboardMenu
    {
        private const float RowHeight = 20f;

        /// <summary>
        /// 選択全体のメニューのボタン識別キー。項目ごとのボタンは項目名をキーにするため、
        /// 項目名と衝突しない記号で囲む
        /// </summary>
        private const string SelectionKey = "*selection*";

        private static readonly List<string> _singleName = new List<string>(1);

        /// <summary>
        /// 1 項目のメニューを行の右端へ寄せて描く (横並びの中で呼ぶ)。
        /// pasteEnabled は行側で編集を止めている項目 (未装着スロット等) の貼り付けを抑える
        /// </summary>
        public static void DrawMenu(
            GUIView view, MTEP.ITimelineLayer layer, string itemName, bool pasteEnabled = true)
        {
            _singleName.Clear();
            _singleName.Add(itemName);
            var canPaste = pasteEnabled && TimelineItemValueTransfer.CanPaste(layer, _singleName);

            ItemClipboardMenu.instance.DrawRightAligned(view, layer, itemName,
                true, canPaste,
                () => TimelineItemValueTransfer.Copy(layer, new[] { itemName }),
                () => TimelineItemValueTransfer.Paste(layer, new[] { itemName }));
        }

        /// <summary>選択中の全項目をまとめて扱うメニューを行の右端へ寄せて描く (横並びの中で呼ぶ)</summary>
        public static void DrawSelectionMenu(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            var names = new List<string>(items.Count);
            foreach (var item in items)
            {
                names.Add(item.name);
            }

            ItemClipboardMenu.instance.DrawRightAligned(view, layer, SelectionKey,
                names.Count > 0, TimelineItemValueTransfer.CanPaste(layer, names),
                () => TimelineItemValueTransfer.Copy(layer, names),
                () => TimelineItemValueTransfer.Paste(layer, names));
        }

        /// <summary>見出しラベル + 右端のメニューの 1 行 (従来の DrawLabel(label, -1, 20) の置き換え)</summary>
        public static void DrawHeading(
            GUIView view, string label, MTEP.ITimelineLayer layer, string itemName,
            Color? textColor = null)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel(label, ItemClipboardMenu.GetRemainingWidth(view), RowHeight, textColor);
                DrawMenu(view, layer, itemName);
            }
            view.EndLayout();
        }
    }
}
