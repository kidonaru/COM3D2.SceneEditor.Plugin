using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UnityEngine;
using COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("モデルボーン", 22)]
    public partial class ModelBoneTimelineLayer : ModelTimelineLayerBase
    {
        public override Type layerType => typeof(ModelBoneTimelineLayer);
        public override string layerName => nameof(ModelBoneTimelineLayer);

        public override List<string> allBoneNames => trackedBoneNames;

        protected override EditTargetStore trackedStore
            => BoneEditManager.instance.modelBoneTrackedStore;

        protected override List<string> trackedCandidateNames => modelManager.boneNames;

        protected override string trackedHistoryPrefix => "モデルボーン";

        // モデルレイヤーは maid を持たない。モデルが 1 体でもあれば 0F 自動キーを打てる
        protected override bool isTrackedTargetReady => modelManager.models.Count > 0;

        private ModelBoneTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static ModelBoneTimelineLayer Create(int slotNo)
        {
            return new ModelBoneTimelineLayer(0);
        }

        public override void Init()
        {
            base.Init();

            StudioModelManager.onModelAdded += OnModelAdded;
            StudioModelManager.onModelRemoved += OnModelRemoved;
        }

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            // 追跡対象だけをメニューへ出す。全ボーンを出すとモデル 1 体で数百行になる
            var targetNames = new HashSet<string>(allBoneNames);

            foreach (var model in modelManager.models)
            {
                if (model.bones.Count == 0)
                {
                    continue;
                }

                BoneSetMenuItem setMenuItem = null;

                foreach (var bone in model.bones)
                {
                    if (!targetNames.Contains(bone.name))
                    {
                        continue;
                    }

                    // 対象ボーンが 1 本も無いモデルは見出しごと出さない
                    if (setMenuItem == null)
                    {
                        setMenuItem = new BoneSetMenuItem(model.name, model.displayName);
                        allMenuItems.Add(setMenuItem);
                    }

                    setMenuItem.AddChild(new ModelBoneMenuItem(bone.name, bone.transform.name));
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
            var bone = modelManager.GetBone(motion.name);
            if (bone == null)
            {
                return;
            }

            var transform = bone.transform;
            if (transform == null)
            {
                return;
            }

            var start = motion.start;
            var end = motion.end;

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;

            transform.localPosition = PluginUtils.HermiteVector3(
                t0,
                t1,
                start.positionValues,
                end.positionValues,
                t);

            transform.localRotation = PluginUtils.HermiteQuaternion(
                t0,
                t1,
                start.rotationValues,
                end.rotationValues,
                t);

            transform.localScale = PluginUtils.HermiteVector3(
                t0,
                t1,
                start.scaleValues,
                end.scaleValues,
                t);
        }

        public void OnModelAdded(StudioModelStat model)
        {
            // 候補名リストが入れ替わるため、メニューを組む前にキャッシュを捨てる
            InvalidateTrackedBoneNames();

            // 追加直後のモデルは未編集なので追跡対象が無い。
            // 0F キーはボーンをチェックした時点で追跡部品が打つ
            InitMenuItems();
            ApplyCurrentFrame(true);
        }

        public void OnModelRemoved(StudioModelStat model)
        {
            InvalidateTrackedBoneNames();

            InitMenuItems();

            var boneNames = model.bones.Select(x => x.name).ToList();
            RemoveAllBones(boneNames);
            ApplyCurrentFrame(true);
        }

        public override void OnCopyModel(StudioModelStat sourceModel, StudioModelStat newModel)
        {
            var sourceModelBones = sourceModel.bones;
            var newModelName = newModel.name;
            foreach (var keyFrame in keyFrames)
            {
                foreach (var sourceModelBone in sourceModelBones)
                {
                    var sourceBone = keyFrame.GetBone(sourceModelBone.name);
                    if (sourceBone == null)
                    {
                        continue;
                    }

                    var baseName = sourceModelBone.transform.name;
                    var newBoneName = string.Format("{0}/{1}", newModelName, baseName);

                    var newBone = keyFrame.GetOrCreateBone(sourceBone.transform.type, newBoneName);
                    newBone.transform.FromTransformData(sourceBone.transform);
                }
            }

            // 複製先のキーを追跡集合へ即座に反映する (待つと間引きぶん遅れる)
            InvalidateTrackedBoneNames();
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            // 追跡対象だけキーを書く。allBoneNames を回すのがキー書き込み絞り込みの実体
            foreach (var boneName in allBoneNames)
            {
                var sourceBone = modelManager.GetBone(boneName);
                if (sourceBone == null || sourceBone.transform == null)
                {
                    // 既存キーにだけ残っているボーン (モデル差し替え等) は書けないので飛ばす
                    continue;
                }

                var trans = CreateTransformData<TransformDataModelBone>(boneName);
                trans.position = sourceBone.transform.localPosition;
                trans.rotation = sourceBone.transform.localRotation;
                trans.scale = sourceBone.transform.localScale;

                var bone = frame.CreateBone(trans);
                frame.UpdateBone(bone);
            }
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override void DrawWindow(GUIView view)
        {
            // SE ではコンボのポップアップ描画をホストウィンドウ側 (ComboBoxPopupWindow) が行うため view.DrawComboBox() は呼ばない
            // ボーンの編集・追跡チェックは SE のボーン編集ウィンドウに委譲する (レイヤー UI 非接続方針)
            view.DrawLabel("ボーンの編集はボーンウィンドウで行ってください", -1, 20);
            view.DrawHorizontalLine(Color.gray);
            DrawModelManage(view);
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.ModelBone;
        }
    }
}