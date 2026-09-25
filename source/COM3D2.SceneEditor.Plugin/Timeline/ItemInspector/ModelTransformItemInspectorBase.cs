using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// モデルの Transform をキー化するレイヤー共通のプロバイダ。
    /// モデル 1 件分の表示 (ヘッダー行・管理行・Transform・委譲先の固有行) を持ち、
    /// タイムラインのメニュー項目選択と、Inspector でのモデル本体の選択の両方で同じものを描く。
    /// モデル一覧の持ち主 (配置モデル / 背景モデル) だけが派生先で変わる。
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

        private readonly ItemRowDrawerCache<ObjectTransformRowDrawer> _transformRowDrawers =
            new ItemRowDrawerCache<ObjectTransformRowDrawer>();

        /// <summary>このフレームに描いたモデル名。キャッシュの掃除に使う</summary>
        private readonly List<string> _drawnNames = new List<string>();

        /// <summary>メニュー項目名からモデルを引く。見つからなければ null</summary>
        protected abstract TModel FindModel(string itemName);

        /// <summary>逆引きの走査対象になるモデル一覧</summary>
        protected abstract List<TModel> models { get; }

        /// <summary>
        /// 表示トグル + 名前 + フォーカスのヘッダー行。トグルが書く先 (表示の持ち方) が
        /// モデルの種類ごとに違うため派生先が描く。複数選択時はモデルごとの見出しを兼ねる
        /// </summary>
        protected abstract void DrawModelHeaderRow(GUIView view, TModel model);

        /// <summary>
        /// モデル 1 件分の管理行 (複製・削除など)。内容はモデルの種類ごとに変わる。
        /// 一覧性はヒエラルキーが持つため、ここは選択中のモデルだけを対象にする
        /// </summary>
        protected virtual void DrawModelManageRows(GUIView view, TModel model)
        {
        }

        /// <summary>描かなかったモデルのキャッシュを捨てる (派生先が持つ分)</summary>
        protected virtual void PruneCaches(IList<string> names)
        {
        }

        public void DrawItems(
            GUIView view, MTEP.ITimelineLayer layer, IList<MTEP.IBoneMenuItem> items)
        {
            _drawnNames.Clear();
            foreach (var item in items)
            {
                var model = FindModel(item.name);
                if (model == null || model.transform == null)
                {
                    // 一覧から消えた直後のメニュー項目 (削除・シーン切替) はここに来る
                    view.DrawLabel(item.displayName + " (モデルが見つかりません)",
                        -1, RowHeight, textColor: Color.gray);
                    continue;
                }

                DrawModel(view, model);
            }

            PruneAllCaches();
        }

        /// <summary>
        /// 選択中のオブジェクトがモデル本体なら、タイムラインのメニュー項目選択と
        /// 同じ表示を描いて true を返す。子オブジェクト (メッシュ・ボーン) は
        /// その子の Transform を個別に触れるよう、呼び出し側の既定表示に任せる
        /// </summary>
        public bool TryDrawSelected(GUIView view, GameObject selected)
        {
            var model = FindModelByObject(selected);
            if (model == null)
            {
                return false;
            }

            _drawnNames.Clear();
            DrawModel(view, model);
            PruneAllCaches();
            return true;
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

        private void DrawModel(GUIView view, TModel model)
        {
            _drawnNames.Add(model.name);
            var go = model.transform.gameObject;

            DrawModelHeaderRow(view, model);
            DrawModelManageRows(view, model);
            _transformRowDrawers.Get(model.name).Draw(
                view, go, LabelWidth, ScaleLabelWidth, RowHeight);

            // 委譲先 (ModItemExplorer 等) に固有の行。ホスト側の別ビューで描くため、
            // こちらのレイアウトは返ってきた高さぶん自分で送る
            var rowsHeight = InspectorHost.DrawRows(go, view.GetDrawRect(-1, 0f));
            if (rowsHeight > 0f)
            {
                view.DrawEmpty(-1, rowsHeight);
            }
        }

        private TModel FindModelByObject(GameObject go)
        {
            if (go == null)
            {
                return default(TModel);
            }
            foreach (var model in models)
            {
                if (model.transform != null && model.transform.gameObject == go)
                {
                    return model;
                }
            }
            return default(TModel);
        }

        private void PruneAllCaches()
        {
            _transformRowDrawers.PruneExcept(_drawnNames);
            PruneCaches(_drawnNames);
        }
    }
}
