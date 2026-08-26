using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("モデルマテリアル", 24)]
    public partial class ModelMaterialTimelineLayer : ModelTimelineLayerBase
    {
        public override Type layerType => typeof(ModelMaterialTimelineLayer);
        public override string layerName => nameof(ModelMaterialTimelineLayer);

        // 変更追跡チェック済みのマテリアルだけへ絞り込む
        public override List<string> allBoneNames => trackedBoneNames;

        protected override EditTargetStore trackedStore
            => ModelMaterialEditManager.instance.trackedStore;

        protected override List<string> trackedCandidateNames => modelManager.materialNames;

        protected override string trackedHistoryPrefix => "モデルマテリアル";

        // モデルレイヤーは maid を持たないため、モデルが 1 体でもあれば 0F 自動キーを打てる
        protected override bool isTrackedTargetReady => modelManager.models.Count > 0;

        private ModelMaterialTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static ModelMaterialTimelineLayer Create(int slotNo)
        {
            return new ModelMaterialTimelineLayer(0);
        }

        public override void Init()
        {
            base.Init();
            // 0F 目の自動キーは絞り込み後の対象だけへ打つ
            AddFirstBones(allBoneNames);

            StudioModelManager.onModelAdded += OnModelAdded;
            StudioModelManager.onModelRemoved += OnModelRemoved;
        }

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            var targetNames = new HashSet<string>(allBoneNames);

            foreach (var model in modelManager.models)
            {
                if (model.materials.Count == 0)
                {
                    continue;
                }

                // 子が 1 つも残らないモデルは見出しごと出さない
                BoneSetMenuItem setMenuItem = null;

                foreach (var material in model.materials)
                {
                    if (!targetNames.Contains(material.name))
                    {
                        continue;
                    }

                    if (setMenuItem == null)
                    {
                        setMenuItem = new BoneSetMenuItem(model.name, model.displayName);
                        allMenuItems.Add(setMenuItem);
                    }

                    var menuItem = new BoneMenuItem(material.name, material.displayName);
                    setMenuItem.AddChild(menuItem);
                }
            }
        }

        public override void Dispose()
        {
            base.Dispose();

            StudioModelManager.onModelAdded -= OnModelAdded;
            StudioModelManager.onModelRemoved -= OnModelRemoved;
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
            var material = modelManager.GetMaterial(motion.name);
            if (material == null)
            {
                MTEUtils.LogDebug($"Material not found: {motion.name}");
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

        public void OnModelAdded(StudioModelStat model)
        {
            // 候補一覧が入れ替わったので追跡集合を作り直させる
            InvalidateTrackedBoneNames();

            InitMenuItems();

            AddFirstBones(allBoneNames);
            ApplyCurrentFrame(true);
        }

        public void OnModelRemoved(StudioModelStat model)
        {
            InvalidateTrackedBoneNames();

            InitMenuItems();

            var materialNames = model.materials.Select(x => x.name).ToList();
            RemoveAllBones(materialNames);
            ApplyCurrentFrame(true);
        }

        public override void OnCopyModel(StudioModelStat sourceModel, StudioModelStat newModel)
        {
            var sourceModelMaterials = sourceModel.materials;
            var newModelName = newModel.name;
            foreach (var keyFrame in keyFrames)
            {
                foreach (var sourceModelMaterial in sourceModelMaterials)
                {
                    var sourceMaterial = keyFrame.GetBone(sourceModelMaterial.name);
                    if (sourceMaterial == null)
                    {
                        continue;
                    }

                    var baseName = sourceModelMaterial.name;
                    var newMaterialName = string.Format("{0}/{1}", newModelName, baseName);

                    var newMaterial = keyFrame.GetOrCreateBone(sourceMaterial.transform.type, newMaterialName);
                    newMaterial.transform.FromTransformData(sourceMaterial.transform);
                }
            }
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            // materialMap を直接回すと絞り込みを素通りするため、対象集合を回して引き当てる
            foreach (var materialName in allBoneNames)
            {
                var sourceMaterial = modelManager.GetMaterial(materialName);
                if (sourceMaterial == null)
                {
                    continue;
                }

                var trans = frame.GetOrCreateTransformData<TransformDataModelMaterial>(materialName);
                trans.Apply(sourceMaterial);
            }
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override void DrawWindow(GUIView view)
        {
            // SE ではコンボのポップアップ描画をホストウィンドウ側 (ComboBoxPopupWindow) が行うため view.DrawComboBox() は呼ばない
            // マテリアルの編集・追跡チェックは SE のマテリアル編集ウィンドウに委譲する (レイヤー UI 非接続方針)
            view.DrawLabel("マテリアルの編集はマテリアルウィンドウで行ってください", -1, 20);
            view.DrawHorizontalLine(Color.gray);
            DrawModelManage(view);
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.ModelMaterial;
        }
    }
}