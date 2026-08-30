using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムラインのボーンメニュー項目選択に対応する Inspector 表示のプロバイダ。
    /// レイヤー型ごとに TimelineItemInspectorRegistry へ登録する
    /// (spec: docs/superpowers/specs/timeline-item-inspector-roadmap.md)
    /// </summary>
    public interface ITimelineItemInspector
    {
        /// <summary>選択中メニュー項目(セット行は子へ展開済み)の現在値編集UIを描く</summary>
        void DrawItems(GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items);

        /// <summary>
        /// SelectionManager の現在の選択に対応するメニュー項目名を返す(逆方向同期用)。
        /// 対応する項目が無ければ null
        /// </summary>
        string FindItemName(MTEP.ITimelineLayer layer);
    }
}
