using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// テキストレイヤー (TextTimelineLayer) のメニュー項目 → フリーテキストの内容・スタイル編集UI。
    /// 項目はテキスト 1 つにつき 1 行 ("Text0" のように末尾が添字)。
    /// テキスト枠の位置・回転・拡縮はレイヤー UI 側で編集する (spec の表どおり内容とスタイルのみ)。
    /// 逆方向: テキストに対応する SelectionManager の選択概念が無いため無し
    /// </summary>
    public class TextItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;

        private static MTEP.TimelineTextManager textManager
            => MTEP.TimelineTextManager.instance;

        private readonly ItemRowDrawerCache<TextRowDrawer> _textRowDrawers =
            new ItemRowDrawerCache<TextRowDrawer>();

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            // 編集していない間はレイヤーが毎フレーム再生値を書き戻すため、
            // 編集モードでないときは触らせない (レイヤー UI と同じ制約)
            if (!MTEP.StudioHackManager.instance.isPoseEditing)
            {
                view.DrawLabel("編集モード中のみテキストを操作できます", -1, RowHeight,
                    textColor: Color.yellow);
                _textRowDrawers.PruneExcept(items);
                return;
            }

            foreach (var item in items)
            {
                var index = ParseTextIndex(item.name);
                if (index < 0 || !textManager.IsValidIndex(index))
                {
                    // 表示数を減らした直後は既存キーだけが残る (レイヤー側と同じ扱い)
                    view.DrawLabel(item.displayName + " (テキストが見つかりません)",
                        -1, RowHeight, textColor: Color.gray);
                    continue;
                }

                // 複数選択時にどのテキストの行か分かるよう見出しを出す
                view.DrawLabel(item.displayName, -1, RowHeight);

                // 色ピッカーはラベルで対象を識別するため、項目名でキーを一意にする
                _textRowDrawers.Get(item.name).Draw(
                    view, textManager.GetFreeTextSet(index), RowHeight, item.name + "/色");
            }

            _textRowDrawers.PruneExcept(items);
        }

        /// <summary>
        /// メニュー項目名 ("Text0" 等) から対象テキストの添字を取り出す。
        /// 想定外の名前なら -1
        /// </summary>
        public static int ParseTextIndex(string itemName)
        {
            var prefix = MTEP.TextTimelineLayer.TextBoneName;
            if (string.IsNullOrEmpty(itemName) || !itemName.StartsWith(prefix))
            {
                return -1;
            }

            int index;
            if (!int.TryParse(itemName.Substring(prefix.Length), out index) || index < 0)
            {
                return -1;
            }
            return index;
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            return null;
        }
    }
}
