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

        private readonly GUIComboBox<Shader> _shaderComboBox = new GUIComboBox<Shader>();

        /// <summary>シェーダー候補。表示のたびに全 Shader を走査しないよう控え、「更新」で作り直す</summary>
        private static List<Shader> _shaderCatalog;

        // シェーダー行の対象。コンボの選択確定はポップアップ側 (別フレーム) で起きるため、
        // 最後に描いた対象を控えて onSelected から引く
        private MTEP.ModelMaterial _shaderTarget;
        private MaterialTrackTarget _shaderTrack;
        private Maid _shaderMaid;
        private MTEP.ModelMaterial _shaderItemsTarget;

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
            _shaderComboBox.getName = (shader, _) => GetShaderDisplayName(shader);
            _shaderComboBox.onSelected = (shader, _) => ApplyShader(shader);
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

        public override bool TryFocusTimelineLayer(Type layerType)
        {
            if (layerType == typeof(MTEP.MaidMaterialTimelineLayer))
            {
                _targetTab = TargetTabType.メイド;
                return true;
            }
            if (layerType == typeof(MTEP.ModelMaterialTimelineLayer))
            {
                _targetTab = TargetTabType.モデル;
                return true;
            }
            if (layerType == typeof(MTEP.BGModelMaterialTimelineLayer))
            {
                _targetTab = TargetTabType.背景;
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

            switch (_targetTab)
            {
                case TargetTabType.メイド:
                    DrawMaidMaterial(target);
                    break;
                case TargetTabType.モデル:
                    TimelineLayerGate.Begin(view, typeof(MTEP.ModelMaterialTimelineLayer), ROW_HEIGHT);
                    DrawModelMaterial();
                    break;
                case TargetTabType.背景:
                    TimelineLayerGate.Begin(view, typeof(MTEP.BGModelMaterialTimelineLayer), ROW_HEIGHT);
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

            TimelineLayerGate.Begin(view, typeof(MTEP.MaidMaterialTimelineLayer), target, ROW_HEIGHT);

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
            }, target);
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

        private void DrawMaterialSelector(
            List<MTEP.ModelMaterial> materials, MaterialTrackTarget track, Maid maid = null)
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

            DrawShaderRow(material, track, maid);
            DrawMaterialProperties(material, track, maid);
        }

        /// <summary>
        /// シェーダーの選択行。元シェーダーは「(元)」付きで並び、選べば元に戻る。
        /// 候補は NPRShader などを後から読んだときのために「更新」で作り直せる
        /// </summary>
        private void DrawShaderRow(MTEP.ModelMaterial material, MaterialTrackTarget track, Maid maid)
        {
            _shaderTarget = material;
            _shaderTrack = track;
            _shaderMaid = maid;

            var currentShader = material.material.shader;
            if (_shaderItemsTarget != material || !_shaderComboBox.items.Contains(currentShader))
            {
                RefreshShaderItems(material, false);
            }
            _shaderComboBox.currentItem = currentShader;

            DrawLabeledComboBox("シェーダー", _shaderComboBox, UpdateButtonWidth + view.margin, () =>
            {
                if (view.DrawButton("更新", UpdateButtonWidth, ROW_HEIGHT))
                {
                    RefreshShaderItems(material, true);
                }
            });
        }

        private void RefreshShaderItems(MTEP.ModelMaterial material, bool reloadCatalog)
        {
            if (_shaderCatalog == null || reloadCatalog)
            {
                _shaderCatalog = ShaderCatalog.GetShaders();
            }

            var items = new List<Shader>(_shaderCatalog.Count + 2);
            // 元と現在のシェーダーは候補の接頭辞に合わなくても選べるようにする
            foreach (var shader in new[] { material.originalShader, material.material.shader })
            {
                if (shader != null && !_shaderCatalog.Contains(shader) && !items.Contains(shader))
                {
                    items.Add(shader);
                }
            }
            items.AddRange(_shaderCatalog);

            _shaderComboBox.items = items;
            _shaderItemsTarget = material;
        }

        private string GetShaderDisplayName(Shader shader)
        {
            if (shader == null)
            {
                return "";
            }
            return _shaderTarget != null && shader == _shaderTarget.originalShader
                ? shader.name + " (元)"
                : shader.name;
        }

        private void ApplyShader(Shader shader)
        {
            var material = _shaderTarget;
            if (material == null || material.material == null || shader == null
                || material.material.shader == shader)
            {
                return;
            }

            // シェーダーはキーではないので追跡チェックは付けない (タイムラインへは MaterialShaderManager が保存する)
            MaterialPropertyRowsDrawer.RecordEdit(material, _shaderTrack, _shaderMaid, "シェーダー");
            material.ChangeShader(shader);
        }

        /// <summary>マテリアル 1 件の色 / 数値プロパティを並べる</summary>
        private void DrawMaterialProperties(
            MTEP.ModelMaterial material, MaterialTrackTarget track, Maid maid)
        {
            // 区切り線とコンボ操作中の入力抑止はマテリアル選択行とセットのこのウィンドウ固有の
            // 都合なので、共有ドロワー (MaterialPropertyRowsDrawer) には含めない
            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.SetEnabled(view.focusedComboBox == null);

            view.BeginScrollView();
            {
                // 1 マテリアルしか出さないので接頭辞は不要
                MaterialPropertyRowsDrawer.Draw(view, material, track, ROW_HEIGHT, null, maid);
            }
            view.EndScrollView();
        }
    }
}
