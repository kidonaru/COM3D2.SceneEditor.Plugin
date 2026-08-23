using System;
using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// シェイプキー編集ウィンドウ。
    /// メイドの任意 blendshape と配置モデルのシェイプキー重みを直接編集する。
    /// キーフレーム化は TimelineWindow のキーフレーム全登録 (Shift+Return) に委ね、
    /// ここでは現在値を書き換えるだけに留める
    /// </summary>
    public class ShapeKeyEditWindow : MaidWindowBase
    {
        public static readonly int WINDOW_ID = 8903389;

        /// <summary>対象種別タブの幅</summary>
        private static readonly int TAB_WIDTH = 70;

        /// <summary>メイドタブ内の内部タブの幅</summary>
        private static readonly int MAID_TAB_WIDTH = 50;

        private static readonly int UpdateButtonWidth = 50;

        /// <summary>編集対象の種別タブ</summary>
        private enum TargetTabType
        {
            メイド,
            モデル,
        }

        /// <summary>メイドタブ内の内部タブ</summary>
        private enum MaidTabType
        {
            操作,
            追加,
        }

        private TargetTabType _targetTab = TargetTabType.メイド;
        private MaidTabType _maidTab = MaidTabType.操作;

        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.TimelineData timeline => timelineManager.timeline;
        private static MTEP.MaidManager timelineMaidManager => MTEP.MaidManager.instance;
        private static MTEP.StudioModelManager modelManager => MTEP.StudioModelManager.instance;
        private static MTEP.StudioHackManager studioHackManager => MTEP.StudioHackManager.instance;

        private readonly GUIComboBox<string> _slotNameComboBox = new GUIComboBox<string>
        {
            getName = (slotName, _) => slotName,
        };

        private readonly GUIComboBox<MTEP.StudioModelStat> _modelComboBox
            = new GUIComboBox<MTEP.StudioModelStat>
        {
            getName = (model, _) => model.displayName,
        };

        // 追加タブの一覧。全スロットの morph 走査と GetTags() はどちらも
        // 毎フレーム回すには重いため、対象が変わったときだけ作り直す。
        // 着替えではスロット構成が変わっても検知できないので「更新」ボタンで捨てられるようにする
        private readonly List<string> _slotNames = new List<string>();
        private Maid _slotNamesMaid = null;
        private List<string> _tags = new List<string>();
        private string _tagsSlotName = null;

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

        protected override void DrawMaidContent(Maid target)
        {
            // GUIView.SetEnabled はグローバルな GUI.enabled を書き換える。
            // 無効のまま抜けると、このウィンドウの後に描かれる ComboBoxPopupWindow まで
            // 操作できなくなるため、どの経路を通っても最後に必ず戻す
            try
            {
                DrawBody(target);
            }
            finally
            {
                view.SetEnabled(true);
            }
        }

        private void DrawBody(Maid target)
        {
            _targetTab = DrawInnerTabs(_targetTab, TAB_WIDTH);

            if (_targetTab == TargetTabType.モデル)
            {
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

            var maidCache = timelineMaidManager.GetMaidCache(target);
            if (maidCache == null)
            {
                view.DrawLabel("メイド情報を取得できません", -1, ROW_HEIGHT, textColor: Color.yellow);
                return;
            }

            _maidTab = DrawInnerTabs(_maidTab, MAID_TAB_WIDTH);

            if (_maidTab == MaidTabType.追加)
            {
                DrawMaidShapeKeyAdd(target, maidCache);
            }
            else
            {
                DrawMaidShapeKeyEdit(maidCache);
            }
        }

        /// <summary>
        /// 追加タブ。スロットが持つ morph タグを列挙し、編集対象として登録する。
        /// 登録先はタイムライン (メイドシェイプキーレイヤーと共用)
        /// </summary>
        private void DrawMaidShapeKeyAdd(Maid target, MTEP.MaidCache maidCache)
        {
            var maidSlotNo = maidCache.slotNo;

            UpdateSlotNames(target);

            _slotNameComboBox.items = _slotNames;
            DrawLabeledComboBox("スロット", _slotNameComboBox, UpdateButtonWidth + view.margin, () =>
            {
                if (view.DrawButton("更新", UpdateButtonWidth, ROW_HEIGHT))
                {
                    ClearSlotCache();
                }
            });

            var slotName = _slotNameComboBox.currentItem;
            if (string.IsNullOrEmpty(slotName) || !target.body0.IsSlotNo(slotName))
            {
                view.DrawLabel("シェイプキーを持つスロットがありません", -1, ROW_HEIGHT);
                return;
            }

            UpdateTags(target, slotName);

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            // 登録先はタイムラインなので、ここから先だけは読み込み済みでないと触れない
            if (timeline == null)
            {
                view.DrawLabel("タイムラインが読み込まれていません", -1, ROW_HEIGHT, textColor: Color.yellow);
                return;
            }

            view.SetEnabled(view.focusedComboBox == null);

            view.BeginScrollView();
            {
                foreach (var tag in _tags)
                {
                    var enable = timeline.HasMaidShapeKey(maidSlotNo, tag);

                    view.DrawToggle(tag, enable, -1, ROW_HEIGHT, newValue =>
                    {
                        if (newValue)
                        {
                            timeline.AddMaidShapeKey(maidSlotNo, tag);
                        }
                        else
                        {
                            timeline.RemoveMaidShapeKey(maidSlotNo, tag);
                        }
                    });
                }
            }
            view.EndScrollView();
        }

        /// <summary>追加タブの一覧キャッシュを捨てる。「更新」ボタンから呼ぶ</summary>
        private void ClearSlotCache()
        {
            _slotNamesMaid = null;
            _tagsSlotName = null;
        }

        /// <summary>スロット一覧を作り直す。全スロットの morph を走査するため対象が変わったときだけ</summary>
        private void UpdateSlotNames(Maid target)
        {
            if (_slotNamesMaid == target)
            {
                return;
            }
            _slotNamesMaid = target;
            _slotNames.Clear();
            // 同名スロットでもメイドが違えばタグは別物
            _tagsSlotName = null;

            // COM3D2.5 の goSlot は直接列挙できないため、インデックス走査で両バージョンに対応する
            var slotCount = Mathf.Min((int) TBody.SlotID.end, target.body0.goSlot.Count);
            for (var i = 0; i < slotCount; i++)
            {
                var slot = target.body0.GetSlot(i);
                if (slot != null && slot.morph != null && slot.morph.hash.Count > 0)
                {
                    _slotNames.Add(slot.Category);
                }
            }
        }

        /// <summary>選択スロットのタグ一覧を作り直す。GetTags() は毎回リストを作るため選択が変わったときだけ</summary>
        private void UpdateTags(Maid target, string slotName)
        {
            if (_tagsSlotName == slotName)
            {
                return;
            }
            _tagsSlotName = slotName;

            var morph = target.body0.GetSlot(slotName).morph;
            _tags = morph != null ? morph.GetTags() : new List<string>();
            _tags.Sort();
        }

        /// <summary>操作タブ。追加タブで登録済みのシェイプキーの重みを編集する</summary>
        private void DrawMaidShapeKeyEdit(MTEP.MaidCache maidCache)
        {
            // 登録済みシェイプキーの一覧はタイムラインが持つ
            if (timeline == null)
            {
                view.DrawLabel("タイムラインが読み込まれていません", -1, ROW_HEIGHT, textColor: Color.yellow);
                return;
            }

            var shapeKeys = timeline.GetMaidShapeKeys(maidCache.slotNo);
            if (shapeKeys.Count == 0)
            {
                view.DrawLabel("追加タブでシェイプキーを登録してください", -1, ROW_HEIGHT);
                return;
            }

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            // 再生中は値の取り合いになるため、ポーズ編集中だけ触らせる (既存レイヤーと同条件)
            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            view.BeginScrollView();
            {
                foreach (var shapeKey in shapeKeys.OrderBy(x => x, StringComparer.Ordinal))
                {
                    var blendShape = maidCache.GetBlendShape(shapeKey);
                    if (blendShape == null)
                    {
                        continue;
                    }

                    var weight = blendShape.weight;

                    view.DrawLabel(shapeKey, -1, ROW_HEIGHT);

                    var updateTransform = view.DrawSliderValue(new GUIView.SliderOption
                    {
                        min = 0f,
                        max = 2f,
                        step = 0.01f,
                        defaultValue = 0f,
                        value = weight,
                        onChanged = x => weight = x,
                    });

                    if (updateTransform)
                    {
                        blendShape.weight = weight;
                        maidCache.FixBlendValues(new string[] { shapeKey });
                    }
                }
            }
            view.EndScrollView();
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

            _modelComboBox.items = models;
            DrawLabeledComboBox("対象", _modelComboBox);

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

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            view.BeginScrollView();
            {
                foreach (var blendShape in blendShapes)
                {
                    var weight = blendShape.weight;

                    view.DrawLabel(blendShape.shapeKeyName, -1, ROW_HEIGHT);

                    var updateTransform = view.DrawSliderValue(new GUIView.SliderOption
                    {
                        min = -1f,
                        max = 2f,
                        step = 0.01f,
                        defaultValue = 0f,
                        value = weight,
                        onChanged = x => weight = x,
                    });

                    // FixBlendValues は全頂点を走査するため、値が変わったときだけ呼ぶ
                    if (updateTransform)
                    {
                        blendShape.weight = weight;
                        model.FixBlendValues();
                    }
                }
            }
            view.EndScrollView();
        }
    }
}
