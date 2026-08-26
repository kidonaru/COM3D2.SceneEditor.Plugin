using System;
using System.Collections.Generic;
using System.Xml.Linq;
using COM3D2.SceneEditor.Plugin;
using UnityEngine;

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

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override void DrawWindow(GUIView view)
        {
            // マテリアルの編集・追跡チェックは SE のマテリアル編集ウィンドウに委譲する (レイヤー UI 非接続方針)
            view.DrawLabel("マテリアルの編集はマテリアルウィンドウで行ってください", -1, 20);
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.ModelMaterial;
        }
    }
}