using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// モデルの Transform をキー化するレイヤー共通のプロバイダ。
    /// メニュー項目名からモデルの Transform を引ければ、Inspector の Object 表示と
    /// 同じ行を出せる。モデル一覧の持ち主 (配置モデル / 背景モデル) だけが派生先で変わる。
    ///
    /// 型引数を取るのは COM3D2 構成 (.NET 3.5) に IEnumerable&lt;T&gt; の共変性が無く、
    /// List&lt;StudioModelStat&gt; を List&lt;IModelStat&gt; として受け取れないため
    /// </summary>
    /// <typeparam name="TModel">レイヤーが扱うモデルの型</typeparam>
    public abstract class ModelTransformItemInspectorBase<TModel> : ITimelineItemInspector
        where TModel : MTEP.IModelStat
    {
        private const float RowHeight = 20f;
        /// <summary>InspectorWindow.LabelWidth と同じ値 (Object 表示と見た目を揃える)</summary>
        private const float LabelWidth = 50f;
        /// <summary>InspectorWindow.ScaleLabelWidth と同じ値 (連動トグル分を差し引いた幅)</summary>
        private const float ScaleLabelWidth = 25f;

        private readonly ObjectTransformRowDrawer _transformRowDrawer =
            new ObjectTransformRowDrawer();

        /// <summary>メニュー項目名からモデルを引く。見つからなければ null</summary>
        protected abstract TModel FindModel(string itemName);

        /// <summary>逆引きの走査対象になるモデル一覧</summary>
        protected abstract List<TModel> models { get; }

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            foreach (var item in items)
            {
                var model = FindModel(item.name);
                var transform = model != null ? model.transform : null;
                if (transform == null)
                {
                    // 一覧から消えた直後のメニュー項目 (削除・シーン切替) はここに来る
                    view.DrawLabel(item.displayName + " (モデルが見つかりません)",
                        -1, RowHeight, textColor: Color.gray);
                    continue;
                }

                // 複数選択時にどのモデルの行か分かるよう見出しを出す
                view.DrawLabel(item.displayName, -1, RowHeight);
                _transformRowDrawer.Draw(
                    view, transform.gameObject, LabelWidth, ScaleLabelWidth, RowHeight);
            }
        }

        public string FindItemName(MTEP.ITimelineLayer layer)
        {
            var selectedObject = SelectionManager.instance.selectedObject;
            if (selectedObject == null)
            {
                return null;
            }
            // SceneView クリックではモデルの子メッシュがヒットしうるため祖先も辿る
            // (MaidWindowBase.SyncTargetModelFromSelection と同じ流儀)
            var selected = selectedObject.transform;
            foreach (var model in models)
            {
                if (model.transform != null && selected.IsChildOf(model.transform))
                {
                    return model.name;
                }
            }
            return null;
        }
    }
}
