using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ライトレイヤー (LightTimelineLayer) のメニュー項目 → ライトのパラメータ編集UI。
    /// 項目はライト 1 灯につき 1 行で、セット行は無い。
    /// 逆方向: ライトの GameObject 選択 → 該当ライトのメニュー選択
    /// </summary>
    public class LightItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;
        /// <summary>LightWindow の LABEL_WIDTH と同じ値 (「追従メイド」が収まる幅)</summary>
        private const float LabelWidth = 70f;

        private static MTEP.StudioLightManager lightManager
            => MTEP.StudioLightManager.instance;

        private readonly ItemRowDrawerCache<LightRowDrawer> _lightRowDrawers =
            new ItemRowDrawerCache<LightRowDrawer>();

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            var mainLight = LightRowDrawer.MainLightComponent;

            foreach (var item in items)
            {
                var stat = lightManager.GetLight(item.name);
                var light = stat != null ? stat.light : null;
                if (light == null)
                {
                    // ライト削除の直後は既存キーだけが残る (レイヤー側と同じ扱い)
                    view.DrawLabel(item.displayName + " (ライトが見つかりません)",
                        -1, RowHeight, textColor: Color.gray);
                    continue;
                }

                // 複数選択時にどのライトの行か分かるよう見出しを出す
                view.DrawLabel(stat.displayName, -1, RowHeight);

                // 色ピッカーはラベルで対象を識別するため、ライト名でキーを一意にする
                var drawer = _lightRowDrawers.Get(item.name);
                if (light == mainLight)
                {
                    drawer.DrawMainLightParams(view, light, LabelWidth, RowHeight, stat.name);
                }
                else
                {
                    drawer.DrawAdditionalLightParams(
                        view, light, LabelWidth, RowHeight, stat.name, stat.followLight);
                }
            }

            _lightRowDrawers.PruneExcept(items);
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            var selectedObject = SelectionManager.instance.selectedObject;
            if (selectedObject == null)
            {
                return null;
            }

            foreach (var stat in lightManager.lights)
            {
                if (stat != null && stat.light != null
                    && stat.light.gameObject == selectedObject)
                {
                    return stat.name;
                }
            }
            return null;
        }
    }
}
