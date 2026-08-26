using System;
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

        private readonly GUIComboBox<MTEP.MaidSlotStat> _slotComboBox
            = new GUIComboBox<MTEP.MaidSlotStat>
        {
            getName = (slot, _) => slot.displayName,
        };

        private readonly GUIComboBox<ProviderModelStat> _modelComboBox
            = new GUIComboBox<ProviderModelStat>
        {
            getName = (model, _) => model.displayName,
        };

        private readonly GUIComboBox<ProviderModelStat> _bgModelComboBox
            = new GUIComboBox<ProviderModelStat>
        {
            getName = (model, _) => model.displayName,
        };

        private readonly GUIComboBox<MTEP.ModelMaterial> _materialComboBox
            = new GUIComboBox<MTEP.ModelMaterial>
        {
            getName = (material, _) => material.displayName,
        };

        /// <summary>
        /// チェック行の描画に必要な、対象ごとの引き当て。
        /// 背景タブは対象のタイムラインレイヤーと対象集合が食い違うため既定値 (追跡なし) を渡す
        /// </summary>
        private struct MaterialTrackTarget
        {
            /// <summary>表示判定用。まだ 1 つもチェックしていない対象のストアを作らないため FindStore を使う</summary>
            public Func<EditTargetStore> findStore;

            /// <summary>操作時のストア。遅延生成する</summary>
            public Func<EditTargetStore> getStore;

            /// <summary>記録する生名。メイドは ModelMaterial.name、モデルは displayName</summary>
            public Func<MTEP.ModelMaterial, string> getKey;

            public bool isEnabled => findStore != null && getStore != null && getKey != null;
        }

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

            DrawMaterialSelector(slot.materials, new MaterialTrackTarget
            {
                findStore = () => MaidMaterialEditManager.instance.FindStore(target),
                getStore = () => MaidMaterialEditManager.instance.GetStore(target),
                // タイムライン側の候補名 (MaidCache.materialNames) と同じ文字列
                getKey = material => material.name,
            });
        }

        /// <summary>
        /// モデルタブ。ModelProviderHost 経由で列挙するため、
        /// タイムラインモデルも ModItemExplorer 等の外部モデルも同じ経路で編集できる
        /// </summary>
        private void DrawModelMaterial()
        {
            ProviderModelStat.CleanupDestroyed();

            var entries = ModelProviderHost.GetModels();
            var models = new List<ProviderModelStat>(entries.Count);
            foreach (var entry in entries)
            {
                var stat = ProviderModelStat.GetOrCreate(entry.obj, entry.displayName);
                if (stat != null)
                {
                    models.Add(stat);
                }
            }

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

            var modelObject = model.transform.gameObject;
            DrawMaterialSelector(model.materials, new MaterialTrackTarget
            {
                findStore = () => ModelMaterialEditManager.instance.FindStore(modelObject),
                getStore = () => ModelMaterialEditManager.instance.GetStore(modelObject),
                // 修飾名は group 振り直しで変わるため、記録は Unity マテリアル名で持つ
                getKey = material => material.displayName,
            });
        }

        /// <summary>
        /// 背景タブ。現在の背景オブジェクト配下の Renderer 持ち GameObject を列挙する。
        /// BGModelManager はタイムラインデータと双方向同期するため使わない
        /// </summary>
        private void DrawBGModelMaterial()
        {
            ProviderModelStat.CleanupDestroyed();

            var bgObject = GameMain.Instance.BgMgr.BgObject;
            if (bgObject == null)
            {
                view.DrawLabel("背景が設定されていません", -1, ROW_HEIGHT);
                return;
            }

            var models = new List<ProviderModelStat>();
            foreach (var renderer in bgObject.GetComponentsInChildren<Renderer>(true))
            {
                var stat = ProviderModelStat.GetOrCreate(renderer.gameObject, renderer.gameObject.name);
                if (stat != null)
                {
                    models.Add(stat);
                }
            }

            if (models.Count == 0)
            {
                view.DrawLabel("背景にマテリアルがありません", -1, ROW_HEIGHT);
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

            // 背景タブは BGModelMaterialTimelineLayer と対象集合が違うため追跡チェックを出さない
            // (レイヤーは BGModelManager の配置モデル、こちらは現在の背景の Renderer)
            DrawMaterialSelector(model.materials, new MaterialTrackTarget());
        }

        private void DrawMaterialSelector(List<MTEP.ModelMaterial> materials, MaterialTrackTarget track)
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

            DrawMaterialProperties(material, track);
        }

        /// <summary>
        /// マテリアル 1 件の色 / 数値プロパティを並べる。
        /// 対象種別によらず中身は同じなので 3 系統で共用する
        /// </summary>
        private void DrawMaterialProperties(MTEP.ModelMaterial material, MaterialTrackTarget track)
        {
            var defaultTrans = MTEP.TransformDataModelMaterial.defaultTrans;

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.SetEnabled(view.focusedComboBox == null);

            var trackKey = track.isEnabled ? track.getKey(material) : null;

            // 編集されたマテリアルは自動で追跡対象にする
            Action markTracked = () =>
            {
                if (trackKey != null)
                {
                    track.getStore().Mark(trackKey);
                }
            };

            view.BeginScrollView();
            {
                if (trackKey != null)
                {
                    var store = track.findStore();
                    var isModified = store != null && store.IsModified(trackKey);

                    // 変更追跡チェック。ON=タイムラインの表示とキー書き込みの対象。
                    // 手動 OFF は「未編集へ戻す」操作なので値も初期値へ戻す
                    Action<bool> onCheckChanged = newChecked =>
                    {
                        if (newChecked)
                        {
                            track.getStore().Mark(trackKey);
                        }
                        else
                        {
                            material.Reset();
                            track.getStore().Unmark(trackKey);
                        }
                    };

                    view.DrawTrackedLabel(isModified, onCheckChanged, material.displayName, -1, ROW_HEIGHT);
                }

                if (view.DrawButton("初期化", 80, ROW_HEIGHT))
                {
                    material.Reset();
                    // 初期値へ戻したのだから追跡からも外す (チェック OFF と同じ意味)
                    if (trackKey != null)
                    {
                        track.getStore().Unmark(trackKey);
                    }
                }

                foreach (var propertyType in MTEP.ModelMaterial.ColorPropertyTypes)
                {
                    if (!material.HasColor(propertyType))
                    {
                        continue;
                    }

                    var color = material.GetColor(propertyType);
                    var initialColor = material.GetInitialColor(propertyType);

                    // ColorPickerWindow はラベル文字列で編集対象を同定するため、
                    // 行ごとに一意なプロパティ名を渡す (空文字だと全行が「編集中」扱いになり、
                    // ピッカーの反映先も最後の行へ化ける)。ラベル描画は DrawColor 内で行われる
                    var cache = view.GetColorFieldCache(propertyType.ToString(), true);

                    view.DrawColor(cache, color, initialColor,
                        newColor =>
                        {
                            material.SetColor(propertyType, newColor);
                            markTracked();
                        });
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
                        width = -1,
                        min = info.min,
                        max = info.max,
                        step = info.step,
                        defaultValue = initialValue,
                        value = value,
                        onChanged = newValue =>
                        {
                            material.SetValue(propertyType, newValue);
                            markTracked();
                        },
                    });
                }
            }
            view.EndScrollView();
        }
    }
}
