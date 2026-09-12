using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ポストエフェクトレイヤー (PostEffectTimelineLayer) のメニュー項目 → エフェクトの編集UI。
    /// 項目は被写界深度・GTToneMap・ブルームが 1 つずつ、パラフィン・距離フォグ・リムライトが
    /// タイムラインの設定数だけ並ぶ。
    /// 逆方向: ポストエフェクトに対応する SelectionManager の選択概念が無いため無し
    /// </summary>
    public class PostEffectItemInspector : ITimelineItemInspector
    {
        private const float RowHeight = 20f;

        private static MTEP.TimelineData timeline => MTEP.TimelineManager.instance.timeline;

        private readonly ItemRowDrawerCache<PostEffectRowDrawer> _rowDrawers =
            new ItemRowDrawerCache<PostEffectRowDrawer>();

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            // 編集モード外はレイヤーが毎フレーム再生値を書き戻すため、
            // 値を書く直前に編集モードへ入る (BeginAutoEditMode)
            view.BeginAutoEditMode();

            foreach (var item in items)
            {
                // 複数選択時にどのエフェクトの行か分かるよう見出しを出す
                view.DrawLabel(item.displayName, -1, RowHeight);
                DrawItem(view, item);
            }

            _rowDrawers.PruneExcept(items);

            view.EndAutoEditMode();
        }

        private void DrawItem(GUIView view, MTEP.IBoneMenuItem item)
        {
            var drawer = _rowDrawers.Get(item.name);
            // 色ピッカーはラベルで対象を識別するため、項目名でキーを一意にする
            drawer.SetColorLabels(item.name);

            switch (MTEP.PostEffectUtils.GetEffectType(item.name))
            {
                case MTEP.PostEffectType.DepthOfField:
                    drawer.DrawDepthOfFieldRows(view);
                    return;
                case MTEP.PostEffectType.GTToneMap:
                    drawer.DrawGTToneMapRows(view);
                    return;
                case MTEP.PostEffectType.Paraffin:
                    DrawIndexedItem(view, item, timeline.paraffinCount,
                        MTEP.PostEffectTimelineLayer.GetParaffinName,
                        index => drawer.DrawParaffinRows(view, index));
                    return;
                case MTEP.PostEffectType.DistanceFog:
                    DrawIndexedItem(view, item, timeline.distanceFogCount,
                        MTEP.PostEffectTimelineLayer.GetDistanceFogName,
                        index => drawer.DrawDistanceFogRows(view, index));
                    return;
                case MTEP.PostEffectType.Rimlight:
                    DrawIndexedItem(view, item, timeline.rimlightCount,
                        MTEP.PostEffectTimelineLayer.GetRimlightName,
                        index => drawer.DrawRimlightRows(view, index, item.name));
                    return;
                case MTEP.PostEffectType.Bloom:
                    drawer.DrawBloomRows(view);
                    return;
                default:
                    view.DrawLabel("(未対応のエフェクトです)", -1, RowHeight,
                        textColor: Color.gray);
                    return;
            }
        }

        /// <summary>設定数ぶん並ぶエフェクトの行。項目名から添字を引いてから描く</summary>
        private static void DrawIndexedItem(
            GUIView view, MTEP.IBoneMenuItem item, int count,
            Func<int, string> getName, Action<int> drawRows)
        {
            var index = ResolveIndex(item.name, count, getName);
            if (index < 0)
            {
                // エフェクト数を減らした直後は既存キーだけが残る (レイヤー側と同じ扱い)
                view.DrawLabel("(エフェクトが見つかりません)", -1, RowHeight,
                    textColor: Color.gray);
                return;
            }
            drawRows(index);
        }

        /// <summary>
        /// メニュー項目名から対象エフェクトの添字を引く。
        /// 名前は "Paraffin"・"Paraffin (1)" のように添字が接尾辞になるため、
        /// 文字列を解析せずレイヤー側の命名関数と突き合わせる。見つからなければ -1
        /// </summary>
        public static int ResolveIndex(string itemName, int count, Func<int, string> getName)
        {
            for (var i = 0; i < count; i++)
            {
                if (getName(i) == itemName)
                {
                    return i;
                }
            }
            return -1;
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            return null;
        }
    }
}
