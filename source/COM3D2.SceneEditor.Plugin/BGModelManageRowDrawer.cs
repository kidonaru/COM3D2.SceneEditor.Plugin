using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>行内で押された操作。一覧を書き換えるため呼び出し側が描画後に実行する</summary>
    public enum BGModelRowAction
    {
        None,
        Duplicate,
        Delete,
    }

    /// <summary>
    /// 制御対象の背景モデル 1 つ分の管理ブロック (表示切替・複製・削除と Transform)。
    /// 見た目と開閉の作法は KeyFrameInspector のキーフレームブロックに合わせる。
    /// 回転行のキャッシュと開閉状態を持つため、モデルごとにインスタンスを分ける
    /// </summary>
    public class BGModelManageRowDrawer
    {
        private const float RowHeight = 20f;
        /// <summary>InspectorWindow.LabelWidth と同じ値 (Object 表示と見た目を揃える)</summary>
        private const float LabelWidth = 50f;
        /// <summary>InspectorWindow.ScaleLabelWidth と同じ値 (連動トグル分を差し引いた幅)</summary>
        private const float ScaleLabelWidth = 25f;

        /// <summary>ブロックヘッダーの開閉マーク幅 (KeyFrameInspector と同じ値)</summary>
        private const float FoldMarkWidth = 16f;
        /// <summary>複製・削除ボタンの幅 (ModelManageRowDrawer と同じ値)</summary>
        private const float ButtonWidth = 45f;
        /// <summary>ヘッダーのモデル名を切り詰める下限 (これ以下だと名前が読めない)</summary>
        private const float MinHeaderLabelWidth = 40f;

        private readonly ObjectTransformRowDrawer _transformRowDrawer =
            new ObjectTransformRowDrawer();

        /// <summary>展開状態。ドロワーはモデルごとに分かれているためここで持てる</summary>
        private bool _expanded = true;

        /// <summary>開閉の予約。反映は ApplyPendingFold で描画ループの外へ送る</summary>
        private bool _pendingToggleFold = false;

        public BGModelRowAction Draw(GUIView view, MTEP.BGModelStat model)
        {
            if (model == null)
            {
                return BGModelRowAction.None;
            }

            view.DrawHorizontalLine(Color.gray);

            var transform = model.transform;
            if (transform == null)
            {
                // 参照先を失った行。削除だけは出して一覧から取り除けるようにする
                return DrawLostRow(view, model);
            }

            var action = DrawHeaderRow(view, model);
            if (!_expanded)
            {
                return action;
            }

            _transformRowDrawer.Draw(
                view, transform.gameObject, LabelWidth, ScaleLabelWidth, RowHeight);

            return action;
        }

        /// <summary>
        /// 開閉の反映を描画ループの外へ遅延させる (KeyFrameInspector と同じ理由)。
        /// GUIView は FloatFieldCache を描画順のインデックスで採番するため、
        /// 同じ Unity フレームの途中で描画要素数が変わるとキャッシュの対応がずれる
        /// </summary>
        public void ApplyPendingFold()
        {
            if (_pendingToggleFold)
            {
                _pendingToggleFold = false;
                _expanded = !_expanded;
            }
        }

        /// <summary>
        /// 開閉マーク + 表示チェック + モデル名 + 複製 / 削除の 1 行。
        /// 折りたたんだままでも操作できるよう、Transform 以外は全てこの行に置く
        /// </summary>
        private BGModelRowAction DrawHeaderRow(GUIView view, MTEP.BGModelStat model)
        {
            var action = BGModelRowAction.None;

            var available = view.viewRect.width - view.padding.x * 2;
            // 要素は 5 個 (マーク・表示チェック・名前・複製・削除) なので margin を 5 個ぶん引く
            var labelWidth = available
                - FoldMarkWidth - GUIView.TrackedCheckWidth - ButtonWidth * 2 - view.margin * 5;
            labelWidth = Mathf.Max(labelWidth, MinHeaderLabelWidth);

            view.BeginHorizontal();
            {
                Action toggle = () => _pendingToggleFold = true;

                view.DrawLabel(_expanded ? "▼" : "▶", FoldMarkWidth, RowHeight,
                    onClickAction: toggle);

                view.DrawToggle(model.visible, GUIView.TrackedCheckWidth, RowHeight, newValue =>
                {
                    model.visible = newValue;
                });

                view.DrawLabel(model.displayName, labelWidth, RowHeight,
                    onClickAction: toggle);

                // 複製・削除はレイヤーの書き戻し対象なので、値を書く直前に編集モードへ入る
                view.BeginAutoEditMode();

                if (view.DrawButton("複製", ButtonWidth, RowHeight))
                {
                    AutoEditMode.Enter();
                    action = BGModelRowAction.Duplicate;
                }

                if (view.DrawButton("削除", ButtonWidth, RowHeight))
                {
                    AutoEditMode.Enter();
                    action = BGModelRowAction.Delete;
                }

                // 後続の Transform 行まで自動移行の対象にしない
                view.EndAutoEditMode();
            }
            view.EndLayout();

            return action;
        }

        /// <summary>モデルの実体が失われた行。表示切替と Transform は出せないため削除のみ</summary>
        private static BGModelRowAction DrawLostRow(GUIView view, MTEP.BGModelStat model)
        {
            var action = BGModelRowAction.None;

            var available = view.viewRect.width - view.padding.x * 2;
            var labelWidth = Mathf.Max(
                available - ButtonWidth - view.margin * 2, MinHeaderLabelWidth);

            view.BeginHorizontal();
            {
                view.DrawLabel(model.displayName + " (モデルが見つかりません)",
                    labelWidth, RowHeight, Color.gray);

                // 削除は一覧を書き換えるため、他の削除操作と同じく値を書く直前に編集モードへ入る
                view.BeginAutoEditMode();

                if (view.DrawButton("削除", ButtonWidth, RowHeight))
                {
                    AutoEditMode.Enter();
                    action = BGModelRowAction.Delete;
                }

                view.EndAutoEditMode();
            }
            view.EndLayout();

            return action;
        }
    }
}
