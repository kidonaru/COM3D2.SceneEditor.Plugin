using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    using AttachPoint = PhotoTransTargetObject.AttachPoint;

    /// <summary>
    /// 配置モデル 1 つ分の管理行 (プラグイン・複製・削除・アタッチ先。表示切替はヘッダー行の DrawHeaderRow)。
    /// 一覧性はヒエラルキーが受け持ち、ここは選択中のモデルへの操作だけを担う。
    /// コンボボックスの開閉状態を持つため、モデルごとにインスタンスを分ける
    /// </summary>
    public class ModelManageRowDrawer
    {
        private const float RowHeight = 20f;

        /// <summary>親メイド・親モデル行のラベル幅 (ModItemExplorer の Inspector 行とそろえる)</summary>
        private const float LabelWidth = 60f;

        /// <summary>部位のボタン幅。部位名は 2 文字程度なので固定にし、残りはメイド側へ回す</summary>
        private const float AttachPointButtonWidth = 60f;

        /// <summary>狭いウィンドウでもボタンが潰れないための下限</summary>
        private const float MinButtonWidth = 40f;

        private static MTEP.StudioModelManager modelManager => MTEP.StudioModelManager.instance;
        private static MTEP.ModelHackManager modelHackManager => MTEP.ModelHackManager.instance;
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;

        private readonly GUIComboBox<string> _pluginComboBox = new GUIComboBox<string>
        {
            getName = (name, _) => string.IsNullOrEmpty(name) ? "Default" : name,
        };

        private readonly GUIComboBox<AttachTargetChoice> _parentMaidComboBox = new GUIComboBox<AttachTargetChoice>
        {
            getName = (choice, _) => choice.label,
            contentSize = new Vector2(150, 300),
        };

        private readonly GUIComboBox<string> _attachPointComboBox = new GUIComboBox<string>
        {
            getName = (name, _) => name,
            items = MTEP.ModelAttachPoints.Names,
            buttonSize = new Vector2(AttachPointButtonWidth, 20),
        };

        private readonly GUIComboBox<AttachTargetChoice> _parentModelComboBox = new GUIComboBox<AttachTargetChoice>
        {
            getName = (choice, _) => choice.label,
            contentSize = new Vector2(150, 300),
        };

        /// <summary>親メイドの選択肢 (先頭は「未選択」)</summary>
        private readonly List<AttachTargetChoice> _parentMaidChoices = new List<AttachTargetChoice>();

        /// <summary>親モデルの選択肢 (先頭は「なし」)</summary>
        private readonly List<AttachTargetChoice> _parentModelChoices = new List<AttachTargetChoice>();

        /// <summary>
        /// 表示トグル + 表示名 + フォーカスのヘッダー行。
        /// 表示切替はレイヤーの書き戻し対象なので、値を書く直前に編集モードへ入る
        /// </summary>
        /// <param name="drawTrailing">行の右端に置く要素 (コピー / 貼り付けメニュー)。null なら置かない</param>
        public static void DrawHeaderRow(
            GUIView view, MTEP.StudioModelStat model, Action<GUIView> drawTrailing = null)
        {
            view.BeginAutoEditMode();
            InspectorHeaderRowDrawer.Draw(view, model.visible, model.displayName, RowHeight,
                newValue =>
                {
                    modelManager.SetModelVisible(model, newValue);
                    model.visible = newValue;
                },
                model.transform.gameObject,
                drawTrailing);
            view.EndAutoEditMode();
        }

        public void Draw(GUIView view, MTEP.StudioModelStat model)
        {
            if (model == null)
            {
                return;
            }

            // 複製・削除・アタッチはレイヤーの書き戻し対象なので、値を書く直前に編集モードへ入る
            view.BeginAutoEditMode();

            view.BeginHorizontal();
            {
                var pluginNames = modelHackManager.pluginNames;
                _pluginComboBox.items = pluginNames;
                _pluginComboBox.currentIndex = GetPluginIndex(pluginNames, model.pluginName);
                _pluginComboBox.onSelected = (pluginName, _) =>
                {
                    modelManager.ChangePluginName(model, pluginName);
                };
                _pluginComboBox.DrawButton(view);

                if (view.DrawButton("複製", 45, RowHeight))
                {
                    AutoEditMode.Enter();
                    timelineManager.CopyModel(model);
                }

                if (view.DrawButton("削除", 45, RowHeight))
                {
                    AutoEditMode.Enter();
                    modelManager.DeleteModel(model);
                }
            }
            view.EndLayout();

            DrawParentMaidRow(view, model);
            DrawParentModelRow(view, model);

            // 後続の Transform 行まで自動移行の対象にしない
            view.EndAutoEditMode();
        }

        /// <summary>親メイドの行。左でメイド、右で部位を選ぶ</summary>
        private void DrawParentMaidRow(GUIView view, MTEP.StudioModelStat model)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel("親メイド", LabelWidth, RowHeight);

                // 右端までの残り幅から、両コンボの前後送りボタン 4 個と部位のボタン、両コンボ間の余白 1 個を除く
                var maidWidth = GetWidthToRowEnd(view)
                    - AttachPointButtonWidth - GUIComboBoxBase.ARROW_SIZE * 4 - view.margin;
                _parentMaidComboBox.buttonSize = new Vector2(Mathf.Max(MinButtonWidth, maidWidth), RowHeight);

                AttachTargetChoices.FillMaids(_parentMaidChoices);
                _parentMaidComboBox.items = _parentMaidChoices;
                _parentMaidComboBox.currentIndex = AttachTargetChoices.IndexOf(
                    _parentMaidChoices, model.attachMaidSlotNo, model.attachModelName);
                _parentMaidComboBox.onSelected = (choice, _) =>
                {
                    // この行が受け持つメイド側の今の値と同じなら何もしない。
                    // モデルへのアタッチ中の「未選択」もここで弾き、下の行の状態を変えない
                    var currentSlotNo = IsAttachedToMaid(model) ? model.attachMaidSlotNo : -1;
                    if (choice.slotNo == currentSlotNo)
                    {
                        return;
                    }
                    ChangeAttach(model, () => AttachTargetChoices.ApplyTo(model, choice));
                };
                _parentMaidComboBox.DrawButton(view);

                // 部位はメイドに付いている間だけ選べる (モデルへのアタッチは原点に付ける)。
                // BeginEnabled はコンボ内部のサブビューへ引き継がれないため、guiEnabled を切り替える
                var prevEnabled = view.guiEnabled;
                view.SetEnabled(prevEnabled && IsAttachedToMaid(model));
                _attachPointComboBox.currentIndex = (int)model.attachPoint;
                _attachPointComboBox.onSelected = (_, index) =>
                {
                    // 選択はポップアップ側で後から届くため、その時点の状態で確かめ直す
                    if (!IsAttachedToMaid(model))
                    {
                        return;
                    }
                    ChangeAttach(model, () => model.attachPoint = (AttachPoint)index);
                };
                _attachPointComboBox.DrawButton(view);
                view.SetEnabled(prevEnabled);
            }
            view.EndLayout();
        }

        private static bool IsAttachedToMaid(MTEP.StudioModelStat model)
        {
            return !model.isAttachedToModel && model.attachMaidSlotNo >= 0;
        }

        /// <summary>親モデルの行。残り幅いっぱいのコンボで付け先のモデルを選ぶ</summary>
        private void DrawParentModelRow(GUIView view, MTEP.StudioModelStat model)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel("親モデル", LabelWidth, RowHeight);

                var buttonWidth = GetWidthToRowEnd(view) - GUIComboBoxBase.ARROW_SIZE * 2;
                _parentModelComboBox.buttonSize = new Vector2(Mathf.Max(MinButtonWidth, buttonWidth), RowHeight);

                AttachTargetChoices.FillModels(_parentModelChoices, model);
                _parentModelComboBox.items = _parentModelChoices;
                _parentModelComboBox.currentIndex = AttachTargetChoices.IndexOf(
                    _parentModelChoices, model.attachMaidSlotNo, model.attachModelName);
                _parentModelComboBox.onSelected = (choice, _) =>
                {
                    // この行が受け持つモデル側の今の値と同じなら何もしない。
                    // メイドへのアタッチ中の「なし」もここで弾き、上の行の状態を変えない
                    var currentModelName = model.isAttachedToModel ? model.attachModelName : "";
                    if ((choice.modelName ?? "") == currentModelName)
                    {
                        return;
                    }
                    ChangeAttach(model, () => AttachTargetChoices.ApplyTo(model, choice));
                };
                _parentModelComboBox.DrawButton(view);
            }
            view.EndLayout();
        }

        /// <summary>横並びの行で、今の描画位置から右端までの幅</summary>
        private static float GetWidthToRowEnd(GUIView view)
        {
            return view.viewRect.width - view.padding.x * 2 - view.currentPos.x;
        }

        /// <summary>
        /// アタッチ先を変え、自動登録が有効なら現在フレームのモデルキーへ記録する。
        /// 編集モードへは、BeginAutoEditMode の区間で描いたコンボが選択の直前に入れている
        /// </summary>
        private static void ChangeAttach(MTEP.StudioModelStat model, Action change)
        {
            change();
            modelManager.UpdateAttachPoint(model);
            TimelineWindow.AutoKeyFrameAfterEdit(typeof(MTEP.ModelTimelineLayer), slotNo: 0);
        }

        /// <summary>プラグイン名から選択肢の添字を引く。未設定・未知の名前は先頭 (Default) 扱い</summary>
        private static int GetPluginIndex(List<string> pluginNames, string pluginName)
        {
            if (string.IsNullOrEmpty(pluginName))
            {
                return 0;
            }

            var index = pluginNames.IndexOf(pluginName);
            return index >= 0 ? index : 0;
        }
    }
}
