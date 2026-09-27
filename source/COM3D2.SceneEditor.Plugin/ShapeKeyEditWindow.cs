using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// シェイプキー編集ウィンドウ。
    /// メイドの全シェイプキーと配置モデルのシェイプキー重みをタイムラインとは独立に直接編集する。
    /// 行頭のチェックが変更追跡 (プリセット保存とタイムラインのキーフレーム対象) を兼ねる。
    /// メイド側はチェック集合がソース・オブ・トゥルースで、タイムラインの opt-in
    /// (TimelineData.maidShapeKeysMap) へは MaidShapeKeyEditManager が片方向に流す
    /// </summary>
    public class ShapeKeyEditWindow : MaidWindowBase
    {
        public static readonly int WINDOW_ID = 8903389;

        /// <summary>対象種別タブの幅</summary>
        private static readonly int TAB_WIDTH = 70;

        private static readonly int UpdateButtonWidth = 50;

        /// <summary>編集対象の種別タブ</summary>
        private enum TargetTabType
        {
            メイド,
            モデル,
        }

        private TargetTabType _targetTab = TargetTabType.メイド;

        private static MTEP.MaidManager timelineMaidManager => MTEP.MaidManager.instance;
        private static MTEP.StudioModelManager modelManager => MTEP.StudioModelManager.instance;

        private readonly GUIComboBox<string> _slotNameComboBox = new GUIComboBox<string>
        {
            getName = (slotName, _) => slotName,
        };

        private readonly GUIComboBox<MTEP.StudioModelStat> _modelComboBox
            = new GUIComboBox<MTEP.StudioModelStat>
        {
            getName = (model, _) => model.displayName,
        };

        /// <summary>メイドタブのスロット / タグ一覧と検索欄。検索文字列はモデルタブとも共用する</summary>
        private readonly MaidShapeKeyListView _maidShapeKeyList = new MaidShapeKeyListView();

        private static ShapeKeyEditWindow _instance = null;
        public static ShapeKeyEditWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ShapeKeyEditWindow();
                }
                return _instance;
            }
        }

        private ShapeKeyEditWindow()
        {
        }

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "シェイプキー";
        protected override int minWidth => 300;
        protected override int minHeight => 300;

        // 対象種別タブをメイド選択行より上に置くため、基底の選択行は使わず自前で描く
        protected override bool showMaidSelector => false;

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.shapeKeyEditPosX;
            y = config.shapeKeyEditPosY;
            width = config.shapeKeyEditWidth;
            height = config.shapeKeyEditHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.shapeKeyEditPosX = x;
            config.shapeKeyEditPosY = y;
            config.shapeKeyEditWidth = width;
            config.shapeKeyEditHeight = height;
        }

        public override bool savedVisible
        {
            get => config.shapeKeyEditVisible;
            set => config.shapeKeyEditVisible = value;
        }

        public override bool TryFocusTimelineLayer(Type layerType)
        {
            if (layerType == typeof(MTEP.ShapeKeyTimelineLayer))
            {
                _targetTab = TargetTabType.メイド;
                return true;
            }
            if (layerType == typeof(MTEP.ModelShapeKeyTimelineLayer))
            {
                _targetTab = TargetTabType.モデル;
                return true;
            }
            return false;
        }

        protected override void DrawMaidContent(Maid target)
        {
            // GUI.enabled の戻しは基底の DrawContent が TimelineLayerGate.End で必ず行う
            DrawBody(target);
        }

        private void DrawBody(Maid target)
        {
            _targetTab = DrawInnerTabs(_targetTab, TAB_WIDTH);

            if (_targetTab == TargetTabType.モデル)
            {
                TimelineLayerGate.Begin(view, typeof(MTEP.ModelShapeKeyTimelineLayer), ROW_HEIGHT);
                DrawModelContent();
                return;
            }

            // showMaidSelector を切っているため、メイドモードではここで選択行を描く
            target = DrawMaidSelector(view);
            if (target == null)
            {
                return;
            }

            if (target.body0 == null || !target.body0.isLoadedBody)
            {
                view.DrawLabel("ボディが読み込まれていません", -1, ROW_HEIGHT, textColor: Color.yellow);
                return;
            }

            var gateState = TimelineLayerGate.Begin(
                view, typeof(MTEP.ShapeKeyTimelineLayer), target, ROW_HEIGHT);

            var maidCache = timelineMaidManager.GetMaidCache(target);
            if (maidCache == null)
            {
                // ゲートが同じ状況を通知済みなら重ねて出さない
                if (gateState != TimelineLayerGateState.MaidNotFound)
                {
                    view.DrawLabel("メイド情報を取得できません", -1, ROW_HEIGHT, textColor: Color.yellow);
                }
                return;
            }

            DrawMaidShapeKeys(target, maidCache);
        }

        /// <summary>スロット選択 → そのスロットの全シェイプキーをスライダー表示する</summary>
        private void DrawMaidShapeKeys(Maid target, MTEP.MaidCache maidCache)
        {
            var slotNames = _maidShapeKeyList.GetSlotNames(target);
            if (slotNames.Count == 0)
            {
                view.DrawLabel("シェイプキーを持つスロットがありません", -1, ROW_HEIGHT);
                return;
            }

            _slotNameComboBox.items = slotNames;
            DrawLabeledComboBox("スロット", _slotNameComboBox, UpdateButtonWidth + view.margin, () =>
            {
                // 着替えはウィンドウ側から検知できないため明示更新
                if (view.DrawButton("更新", UpdateButtonWidth, ROW_HEIGHT))
                {
                    _maidShapeKeyList.ClearCache();
                    maidCache.ClearBlendShapeCache();
                }
            });

            var slotName = _slotNameComboBox.currentItem;
            if (string.IsNullOrEmpty(slotName) || !target.body0.IsSlotNo(slotName))
            {
                return;
            }

            var tags = _maidShapeKeyList.GetTags(target, slotName);

            _maidShapeKeyList.DrawSearchField(view, LABEL_WIDTH, ROW_HEIGHT);

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            _maidShapeKeyList.DrawRows(view, target, maidCache, tags, ROW_HEIGHT);
        }

        /// <summary>モデルタブ。配置モデルが持つシェイプキーをそのまま列挙する</summary>
        private void DrawModelContent()
        {
            var models = modelManager.models;
            if (models.Count == 0)
            {
                view.DrawLabel("配置中のモデルがありません", -1, ROW_HEIGHT);
                return;
            }

            DrawModelComboBox("対象", _modelComboBox, models, m => m.transform);

            var model = _modelComboBox.currentItem;
            if (model == null || model.transform == null)
            {
                view.DrawLabel("モデルが見つかりません", -1, ROW_HEIGHT);
                return;
            }

            var blendShapes = model.blendShapes;
            if (blendShapes.Count == 0)
            {
                view.DrawLabel("シェイプキーを持たないモデルです", -1, ROW_HEIGHT);
                return;
            }

            _maidShapeKeyList.DrawSearchField(view, LABEL_WIDTH, ROW_HEIGHT);

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.SetEnabled(view.focusedComboBox == null);

            var matchedCount = 0;

            view.BeginScrollView();
            {
                foreach (var blendShape in blendShapes)
                {
                    var shapeKeyName = blendShape.shapeKeyName;
                    if (!MaidShapeKeyListView.IsSearchMatched(shapeKeyName, _maidShapeKeyList.searchText))
                    {
                        continue;
                    }

                    matchedCount++;

                    ModelShapeKeyRowDrawer.Draw(view, model, blendShape, ROW_HEIGHT);
                }

                if (matchedCount == 0)
                {
                    view.DrawLabel("該当するシェイプキーがありません", -1, ROW_HEIGHT);
                }
            }
            view.EndScrollView();
        }
    }
}
