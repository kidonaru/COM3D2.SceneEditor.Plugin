using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;
using static COM3D2.MotionTimelineEditor.Plugin.ModelMaterial;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("背景モデルマテリアル", 34)]
    public class BGModelMaterialTimelineLayer : BGModelTimelineLayerBase
    {
        public override Type layerType => typeof(BGModelMaterialTimelineLayer);
        public override string layerName => nameof(BGModelMaterialTimelineLayer);

        public override List<string> allBoneNames => bgModelManager.materialNames;

        private BGModelMaterialTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static BGModelMaterialTimelineLayer Create(int slotNo)
        {
            return new BGModelMaterialTimelineLayer(0);
        }

        public override void Init()
        {
            base.Init();

            BGModelManager.onSetup += OnBGModelSetup;
            BGModelManager.onModelAdded += OnBGModelAdded;
            BGModelManager.onModelRemoved += OnBGModelRemoved;
        }

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            foreach (var model in bgModelManager.models)
            {
                if (model.materials.Count == 0)
                {
                    continue;
                }

                var setMenuItem = new BoneSetMenuItem(model.name, model.displayName);
                allMenuItems.Add(setMenuItem);

                foreach (var material in model.materials)
                {
                    var menuItem = new BoneMenuItem(material.name, material.displayName);
                    setMenuItem.AddChild(menuItem);
                }
            }
        }

        public override void Dispose()
        {
            base.Dispose();

            BGModelManager.onSetup -= OnBGModelSetup;
            BGModelManager.onModelAdded -= OnBGModelAdded;
            BGModelManager.onModelRemoved -= OnBGModelRemoved;
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
            var material = bgModelManager.GetMaterial(motion.name);
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

        public void OnBGModelSetup()
        {
            InitMenuItems();

            var materialNames = bgModelManager.materialNames;
            AddFirstBones(materialNames);
            ApplyCurrentFrame(true);
        }

        public void OnBGModelAdded(BGModelStat model)
        {
            InitMenuItems();

            var materialNames = model.materials.Select(x => x.name).ToList();
            AddFirstBones(materialNames);
            ApplyCurrentFrame(true);
        }

        public void OnBGModelRemoved(BGModelStat model)
        {
            InitMenuItems();

            var materialNames = model.materials.Select(x => x.name).ToList();
            RemoveAllBones(materialNames);
            ApplyCurrentFrame(true);
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            foreach (var sourceMaterial in bgModelManager.materialMap.Values)
            {
                var materialName = sourceMaterial.name;

                var trans = frame.GetOrCreateTransformData<TransformDataModelMaterial>(materialName);
                trans.Apply(sourceMaterial);
            }
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.ModelMaterial;
        }
    }
}