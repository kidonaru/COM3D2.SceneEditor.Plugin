using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// PNG 配置レイヤー (PngPlacementTimelineLayer) のメニュー項目 → 配置 PNG の編集UI。
    /// 項目は PNG 1 枚につき 1 行。レイヤーがキー化するのは Transform と
    /// 表示・色・明るさで、いずれも Inspector の Object 表示と PNG 固有欄が持つ行と同じ。
    /// 逆方向: PNG のルートオブジェクト選択 → 該当 PNG のメニュー選択
    /// </summary>
    public class PngPlacementItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;
        /// <summary>InspectorWindow.LabelWidth と同じ値 (Object 表示と見た目を揃える)</summary>
        private const float LabelWidth = 50f;
        /// <summary>InspectorWindow.ScaleLabelWidth と同じ値 (連動トグル分を差し引いた幅)</summary>
        private const float ScaleLabelWidth = 25f;

        private static MTEP.PngObjectTimelineManager pngTimelineManager
            => MTEP.PngObjectTimelineManager.instance;

        private readonly ItemRowDrawerCache<ObjectTransformRowDrawer> _transformRowDrawers =
            new ItemRowDrawerCache<ObjectTransformRowDrawer>();

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            // 編集していない間はレイヤーが毎フレーム再生値を書き戻すため、
            // 編集モードでないときは触らせない (他の演出系レイヤーと同じ制約)
            if (!MTEP.StudioHackManager.instance.isPoseEditing)
            {
                view.DrawLabel("編集モード中のみPNG配置を操作できます", -1, RowHeight,
                    textColor: Color.yellow);
                _transformRowDrawers.PruneExcept(items);
                return;
            }

            foreach (var item in items)
            {
                var pngObject = pngTimelineManager.GetPngObject(item.name);
                var rootObject = pngObject != null && pngObject.data != null
                    ? pngObject.data.rootObject
                    : null;
                if (rootObject == null)
                {
                    // PNG を削除した直後は既存キーだけが残る (レイヤー側と同じ扱い)
                    view.DrawLabel(item.displayName + " (PNGが見つかりません)",
                        -1, RowHeight, textColor: Color.gray);
                    continue;
                }

                // 複数選択時にどの PNG の行か分かるよう見出しを出す
                view.DrawLabel(item.displayName, -1, RowHeight);

                _transformRowDrawers.Get(item.name).Draw(
                    view, rootObject, LabelWidth, ScaleLabelWidth, RowHeight);

                // 表示・ビルボード・色・明るさなどの PNG 固有欄は Object 表示と同じものを使う。
                // 色ピッカーはラベルで対象を識別するため、項目名でキーを一意にする
                PngPlacementInspector.Draw(view, rootObject, item.name);
            }

            _transformRowDrawers.PruneExcept(items);
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            var selectedObject = SelectionManager.instance.selectedObject;
            if (selectedObject == null)
            {
                return null;
            }

            // ビューポートのクリックでは PNG の板 (ルートの子) がヒットするため祖先も辿る
            // (ModelTransformItemInspectorBase.FindItemName と同じ流儀)
            var selected = selectedObject.transform;
            foreach (var pngObject in pngTimelineManager.pngObjects)
            {
                var rootObject = pngObject.data != null ? pngObject.data.rootObject : null;
                if (rootObject != null && selected.IsChildOf(rootObject.transform))
                {
                    return pngObject.name;
                }
            }
            return null;
        }
    }
}
