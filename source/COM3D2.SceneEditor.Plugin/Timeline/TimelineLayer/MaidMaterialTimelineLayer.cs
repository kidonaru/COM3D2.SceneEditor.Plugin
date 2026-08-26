using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;
using static COM3D2.MotionTimelineEditor.Plugin.ModelMaterial;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("メイドマテリアル", 17)]
    public class MaidMaterialTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(MaidMaterialTimelineLayer);
        public override string layerName => nameof(MaidMaterialTimelineLayer);

        // SE では変更追跡チェック済みのマテリアルだけへ絞り込む
        public override List<string> allBoneNames => trackedBoneNames;

        protected override EditTargetStore trackedStore
            => MaidMaterialEditManager.instance.FindStore(maid);

        // MTE 原本は三項演算子の条件が反転しており (null 時に参照 / 非 null 時に空リスト)、
        // マテリアル一覧が常に空になるため SE 側で修正している
        protected override List<string> trackedCandidateNames
            => maidCache == null ? new List<string>() : maidCache.materialNames;

        protected override string trackedHistoryPrefix => "メイドマテリアル";

        private MaidMaterialTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static MaidMaterialTimelineLayer Create(int slotNo)
        {
            return new MaidMaterialTimelineLayer(slotNo);
        }

        public override void Init()
        {
            base.Init();
            UpdateMaterials();
        }

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            if (maidCache == null)
            {
                return;
            }

            var targetNames = new HashSet<string>(allBoneNames);

            foreach (var stat in maidCache.slotStats)
            {
                if (stat == null || stat.materials.Count == 0)
                {
                    continue;
                }

                // 子が 1 つも残らないスロットは見出しごと出さない
                BoneSetMenuItem setMenuItem = null;

                foreach (var material in stat.materials)
                {
                    if (!targetNames.Contains(material.name))
                    {
                        continue;
                    }

                    if (setMenuItem == null)
                    {
                        setMenuItem = new BoneSetMenuItem(stat.name, stat.displayName);
                        allMenuItems.Add(setMenuItem);
                    }

                    var menuItem = new BoneMenuItem(material.name, material.displayName);
                    setMenuItem.AddChild(menuItem);
                }
            }
        }

        public override bool IsValidData()
        {
            errorMessage = "";
            return true;
        }

        public override void Update()
        {
            base.Update();
        }

        public override void LateUpdate()
        {
            base.LateUpdate();

            if (!studioHackManager.isPoseEditing)
            {
                ApplyPlayData();
            }
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            var material = maidCache.GetMaterial(motion.name);
            if (material == null)
            {
                return;
            }

            var start = motion.start as TransformDataModelMaterial;
            var end = motion.end as TransformDataModelMaterial;

            if (indexUpdated)
            {
                material.Apply(start);
            }

            // 集約型のためフィールド個別補間はできない。区間の代表 Tangent で形状を作る
            float lerpTime = CalcTangentValue(motion, t);
            material.Lerp(start, end, lerpTime);
        }

        public override void OnCurrentLayer()
        {
            UpdateMaterials();
        }

        public override void OnMaidChanged(Maid maid)
        {
            UpdateMaterials();
        }

        private void UpdateMaterials()
        {
            if (maidCache == null)
            {
                return;
            }

            maidCache.UpdateMaterials();

            // 候補一覧が入れ替わったので追跡集合を作り直させる
            InvalidateTrackedBoneNames();

            InitMenuItems();

            // 0F 目の自動キーは絞り込み後の対象だけへ打つ。
            // 全マテリアルへ打つと「既存キーフレーム記載」経由で全部がメニューへ復活して絞り込みが効かなくなる
            AddFirstBones(allBoneNames);
            ApplyCurrentFrame(true);
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            // materialMap を直接回すと絞り込みを素通りするため、対象集合を回して引き当てる
            foreach (var materialName in allBoneNames)
            {
                var sourceMaterial = maidCache.GetMaterial(materialName);
                if (sourceMaterial == null)
                {
                    continue;
                }

                var trans = frame.GetOrCreateTransformData<TransformDataModelMaterial>(materialName);
                trans.Apply(sourceMaterial);
            }
        }

        private GUIComboBox<MaidSlotStat> _slotComboBox = new GUIComboBox<MaidSlotStat>
        {
            getName = (model, index) => model.displayName,
            buttonSize = new Vector2(200, 20),
            contentSize = new Vector2(200, 300),
        };

        private GUIComboBox<ModelMaterial> _materialComboBox = new GUIComboBox<ModelMaterial>
        {
            getName = (material, index) => material.displayName,
            buttonSize = new Vector2(200, 20),
            contentSize = new Vector2(200, 300),
        };

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override void DrawWindow(GUIView view)
        {
            // SE ではコンボのポップアップ描画をホストウィンドウ側 (ComboBoxPopupWindow) が行うため view.DrawComboBox() は呼ばない
            DrawMaterial(view);
        }

        public void DrawMaterial(GUIView view)
        {
            _slotComboBox.items = maidCache.slotStats;

            if (_slotComboBox.items.Count == 0)
            {
                view.DrawLabel("スロットが存在しません", 200, 20);
                return;
            }

            view.SetEnabled(view.focusedComboBox == null);

            view.BeginHorizontal();
            {
                view.DrawLabel("スロット選択", 200, 20);

                if (view.DrawButton("更新", 50, 20))
                {
                    UpdateMaterials();
                }
            }
            view.EndLayout();

            _slotComboBox.DrawButton(view);

            var slot = _slotComboBox.currentItem;
            if (slot == null)
            {
                view.DrawLabel("スロットが見つかりません", 200, 20);
                return;
            }

            _materialComboBox.items = slot.materials;

            if (_materialComboBox.items.Count == 0)
            {
                view.DrawLabel("マテリアルが存在しません", 200, 20);
                return;
            }

            _materialComboBox.DrawButton(view);

            var material = _materialComboBox.currentItem;
            if (material == null)
            {
                view.DrawLabel("マテリアルが見つかりません", 200, 20);
                return;
            }

            var defaultTrans = TransformDataModelMaterial.defaultTrans;

            view.DrawHorizontalLine(Color.gray);

            view.AddSpace(5);

            view.BeginScrollView();
            {
                view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

                view.BeginHorizontal();
                {
                    view.DrawLabel(material.displayName, 180, 20);

                    if (view.DrawButton("初期化", 60, 20))
                    {
                       material.Reset();
                    }
                }
                view.EndLayout();

                foreach (var propertyType in ModelMaterial.ColorPropertyTypes)
                {
                    if (!material.HasColor(propertyType)) continue;

                    var color = material.GetColor(propertyType);
                    var initialColor = material.GetInitialColor(propertyType);
                    var cache = view.GetColorFieldCache("", true);

                    view.DrawLabel(propertyType.ToString(), 200, 20);

                    view.DrawColor(cache, color, initialColor, newColor =>
                    {
                        material.SetColor(propertyType, newColor);
                    });
                }

                foreach (var propertyType in ModelMaterial.ValuePropertyTypes)
                {
                    if (!material.HasValue(propertyType)) continue;

                    var value = material.GetValue(propertyType);
                    var initialValue = material.GetInitialValue(propertyType);
                    var info = defaultTrans.GetCustomValueInfo(propertyType);
                    var fieldType = propertyType == ValuePropertyType._OutlineWidth ?
                        FloatFieldType.F4 : FloatFieldType.Float;

                    view.DrawLabel($"{propertyType} ({info.name})", -1, 20);

                    view.DrawSliderValue(new GUIView.SliderOption
                    {
                        fieldType = fieldType,
                        min = info.min,
                        max = info.max,
                        step = info.step,
                        defaultValue = initialValue,
                        value = value,
                        onChanged = newValue =>
                        {
                            material.SetValue(propertyType, newValue);
                        },
                    });
                }

                view.DrawHorizontalLine(Color.gray);
            }
            view.SetEnabled(view.focusedComboBox == null);
            view.EndScrollView();
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.ModelMaterial;
        }
    }
}