using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// マテリアル編集ウィンドウ。
    /// メイドスロット / 配置モデル / 背景モデルのマテリアル色・数値プロパティを直接編集する。
    /// キーフレーム化は TimelineWindow のキーフレーム全登録 (Shift+Return) に委ね、
    /// ここでは現在値を書き換えるだけに留める
    /// </summary>
    public class MaterialEditWindow : MaidWindowBase
    {
        public static readonly int WINDOW_ID = 8903390;

        private static readonly int TAB_WIDTH = 70;

        private static readonly int UpdateButtonWidth = 50;

        /// <summary>編集対象の種別タブ</summary>
        private enum TargetTabType
        {
            メイド,
            モデル,
            背景,
        }

        private TargetTabType _targetTab = TargetTabType.メイド;

        private static MTEP.MaidManager timelineMaidManager => MTEP.MaidManager.instance;
        private static MTEP.StudioModelManager modelManager => MTEP.StudioModelManager.instance;
        private static MTEP.BGModelManager bgModelManager => MTEP.BGModelManager.instance;
        private static MTEP.StudioHackManager studioHackManager => MTEP.StudioHackManager.instance;

        private readonly GUIComboBox<MTEP.MaidSlotStat> _slotComboBox
            = new GUIComboBox<MTEP.MaidSlotStat>
        {
            getName = (slot, _) => slot.displayName,
        };

        private readonly GUIComboBox<MTEP.StudioModelStat> _modelComboBox
            = new GUIComboBox<MTEP.StudioModelStat>
        {
            getName = (model, _) => model.displayName,
        };

        private readonly GUIComboBox<MTEP.BGModelStat> _bgModelComboBox
            = new GUIComboBox<MTEP.BGModelStat>
        {
            getName = (model, _) => model.displayName,
        };

        private readonly GUIComboBox<MTEP.ModelMaterial> _materialComboBox
            = new GUIComboBox<MTEP.ModelMaterial>
        {
            getName = (material, _) => material.displayName,
        };

        private static MaterialEditWindow _instance = null;
        public static MaterialEditWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MaterialEditWindow();
                }
                return _instance;
            }
        }

        private MaterialEditWindow()
        {
        }

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "マテリアル";
        protected override int minWidth => 300;
        protected override int minHeight => 300;

        // 対象種別タブをメイド選択行より上に置くため、基底の選択行は使わず自前で描く
        protected override bool showMaidSelector => false;

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.materialEditPosX;
            y = config.materialEditPosY;
            width = config.materialEditWidth;
            height = config.materialEditHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.materialEditPosX = x;
            config.materialEditPosY = y;
            config.materialEditWidth = width;
            config.materialEditHeight = height;
        }

        public override bool savedVisible
        {
            get => config.materialEditVisible;
            set => config.materialEditVisible = value;
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

            switch (_targetTab)
            {
                case TargetTabType.メイド:
                    DrawMaidMaterial(target);
                    break;
                case TargetTabType.モデル:
                    DrawModelMaterial();
                    break;
                case TargetTabType.背景:
                    DrawBGModelMaterial();
                    break;
            }
        }

        private void DrawMaidMaterial(Maid target)
        {
            // showMaidSelector を切っているため、メイドモードではここで選択行を描く
            target = DrawMaidSelector(view);
            if (target == null)
            {
                return;
            }

            var maidCache = timelineMaidManager.GetMaidCache(target);
            if (maidCache == null)
            {
                view.DrawLabel("メイド情報を取得できません", -1, ROW_HEIGHT, textColor: Color.yellow);
                return;
            }

            // 着替え後はマテリアル一覧が自動で追従しないため、手動更新の導線を置く
            _slotComboBox.items = maidCache.slotStats;
            DrawLabeledComboBox("スロット", _slotComboBox, UpdateButtonWidth + view.margin, () =>
            {
                if (view.DrawButton("更新", UpdateButtonWidth, ROW_HEIGHT))
                {
                    maidCache.UpdateMaterials();
                }
            });

            var slot = _slotComboBox.currentItem;
            if (slot == null)
            {
                view.DrawLabel("スロットがありません", -1, ROW_HEIGHT);
                return;
            }

            DrawMaterialSelector(slot.materials);
        }

        private void DrawModelMaterial()
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

            DrawMaterialSelector(model.materials);
        }

        private void DrawBGModelMaterial()
        {
            var models = bgModelManager.models;
            if (models.Count == 0)
            {
                view.DrawLabel("背景モデルがありません", -1, ROW_HEIGHT);
                return;
            }

            _bgModelComboBox.items = models;
            DrawLabeledComboBox("対象", _bgModelComboBox);

            var model = _bgModelComboBox.currentItem;
            if (model == null || model.transform == null)
            {
                view.DrawLabel("背景モデルが見つかりません", -1, ROW_HEIGHT);
                return;
            }

            DrawMaterialSelector(model.materials);
        }

        private void DrawMaterialSelector(List<MTEP.ModelMaterial> materials)
        {
            if (materials == null || materials.Count == 0)
            {
                view.DrawLabel("マテリアルがありません", -1, ROW_HEIGHT);
                return;
            }

            _materialComboBox.items = materials;
            DrawLabeledComboBox("マテリアル", _materialComboBox);

            var material = _materialComboBox.currentItem;
            if (material == null)
            {
                view.DrawLabel("マテリアルが見つかりません", -1, ROW_HEIGHT);
                return;
            }

            DrawMaterialProperties(material);
        }

        /// <summary>
        /// マテリアル 1 件の色 / 数値プロパティを並べる。
        /// 対象種別によらず中身は同じなので 3 系統で共用する
        /// </summary>
        private void DrawMaterialProperties(MTEP.ModelMaterial material)
        {
            var defaultTrans = MTEP.TransformDataModelMaterial.defaultTrans;

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            // 再生中は値の取り合いになるため、ポーズ編集中だけ触らせる (既存レイヤーと同条件)
            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            view.BeginScrollView();
            {
                if (view.DrawButton("初期化", 80, ROW_HEIGHT))
                {
                    material.Reset();
                }

                foreach (var propertyType in MTEP.ModelMaterial.ColorPropertyTypes)
                {
                    if (!material.HasColor(propertyType))
                    {
                        continue;
                    }

                    var color = material.GetColor(propertyType);
                    var initialColor = material.GetInitialColor(propertyType);

                    // GetColorFieldCache のラベルは DrawColor 内で描画されるため、
                    // プロパティ名を渡すと直前の DrawLabel と二重に出る。空文字で取る
                    var cache = view.GetColorFieldCache("", true);

                    view.DrawLabel(propertyType.ToString(), -1, ROW_HEIGHT);

                    view.DrawColor(cache, color, initialColor,
                        newColor => material.SetColor(propertyType, newColor));
                }

                foreach (var propertyType in MTEP.ModelMaterial.ValuePropertyTypes)
                {
                    if (!material.HasValue(propertyType))
                    {
                        continue;
                    }

                    var value = material.GetValue(propertyType);
                    var initialValue = material.GetInitialValue(propertyType);
                    var info = defaultTrans.GetCustomValueInfo(propertyType);

                    // _OutlineWidth は 0.001 前後の極小値のため桁数を増やす
                    var fieldType = propertyType == MTEP.ModelMaterial.ValuePropertyType._OutlineWidth
                        ? FloatFieldType.F4
                        : FloatFieldType.Float;

                    view.DrawLabel($"{propertyType} ({info.name})", -1, ROW_HEIGHT);

                    view.DrawSliderValue(new GUIView.SliderOption
                    {
                        fieldType = fieldType,
                        min = info.min,
                        max = info.max,
                        step = info.step,
                        defaultValue = initialValue,
                        value = value,
                        onChanged = newValue => material.SetValue(propertyType, newValue),
                    });
                }
            }
            view.EndScrollView();
        }
    }
}
