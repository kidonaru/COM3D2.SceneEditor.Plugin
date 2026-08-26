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
    [TimelineLayerDesc("モデルシェイプ", 23)]
    public partial class ModelShapeKeyTimelineLayer : ModelTimelineLayerBase
    {
        public override Type layerType => typeof(ModelShapeKeyTimelineLayer);
        public override string layerName => nameof(ModelShapeKeyTimelineLayer);

        public override List<string> allBoneNames => trackedBoneNames;

        protected override EditTargetStore trackedStore
            => ModelShapeKeyEditManager.instance.trackedStore;

        protected override List<string> trackedCandidateNames => modelManager.blendShapeNames;

        protected override string trackedHistoryPrefix => "モデルシェイプ";

        // モデルレイヤーは maid を持たない。モデルが 1 体でもあれば 0F 自動キーを打てる
        protected override bool isTrackedTargetReady => modelManager.models.Count > 0;

        private ModelShapeKeyTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static ModelShapeKeyTimelineLayer Create(int slotNo)
        {
            return new ModelShapeKeyTimelineLayer(0);
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

            // 追跡対象だけをメニューへ出す。全シェイプキーを出すとモデル 1 体で数百行になる
            var targetNames = new HashSet<string>(allBoneNames);

            foreach (var model in modelManager.models)
            {
                if (model.blendShapes.Count == 0)
                {
                    continue;
                }

                BoneSetMenuItem setMenuItem = null;

                foreach (var blendShape in model.blendShapes)
                {
                    if (!targetNames.Contains(blendShape.name))
                    {
                        continue;
                    }

                    // 対象が 1 件も無いモデルは見出しごと出さない
                    if (setMenuItem == null)
                    {
                        setMenuItem = new BoneSetMenuItem(model.name, model.displayName);
                        allMenuItems.Add(setMenuItem);
                    }

                    setMenuItem.AddChild(new BoneMenuItem(blendShape.name, blendShape.shapeKeyName));
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

        protected override void ApplyPlayData()
        {
            base.ApplyPlayData();

            var models = modelManager.models;
            foreach (var model in models)
            {
                model.FixBlendValues();
            }
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            var blendShape = modelManager.GetBlendShape(motion.name);
            if (blendShape == null)
            {
                return;
            }

            var start = motion.start as TransformDataModelShapeKey;
            var end = motion.end as TransformDataModelShapeKey;

            var t0 = motion.stFrame * timeline.frameDuration;
            var t1 = motion.edFrame * timeline.frameDuration;

            var weight = PluginUtils.HermiteValue(t0, t1, start.weightValue, end.weightValue, t);
            blendShape.weight = weight;
        }

        public void OnModelAdded(StudioModelStat model)
        {
            // 候補名リストが入れ替わるため、メニューを組む前にキャッシュを捨てる
            InvalidateTrackedBoneNames();

            // 追加直後のモデルは未編集なので追跡対象が無い。
            // 0F キーはシェイプキーをチェックした時点で追跡部品が打つ
            InitMenuItems();
            ApplyCurrentFrame(true);
        }

        public void OnModelRemoved(StudioModelStat model)
        {
            InvalidateTrackedBoneNames();

            InitMenuItems();

            var boneNames = model.blendShapes.Select(x => x.name).ToList();
            RemoveAllBones(boneNames);
            ApplyCurrentFrame(true);
        }

        public override void OnCopyModel(StudioModelStat sourceModel, StudioModelStat newModel)
        {
            var blendShapes = sourceModel.blendShapes;
            var newModelName = newModel.name;
            foreach (var keyFrame in keyFrames)
            {
                foreach (var blendShape in blendShapes)
                {
                    var sourceBone = keyFrame.GetBone(blendShape.name);
                    if (sourceBone == null)
                    {
                        continue;
                    }

                    var baseName = blendShape.shapeKeyName;
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
                var blendShape = modelManager.GetBlendShape(boneName);
                if (blendShape == null)
                {
                    // 既存キーにだけ残っているシェイプキー (モデル差し替え等) は書けないので飛ばす
                    continue;
                }

                var trans = CreateTransformData<TransformDataModelShapeKey>(boneName);
                trans.weight = blendShape.weight;

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
            // ブレンドシェイプの編集・追跡チェックは SE のシェイプキー編集ウィンドウに委譲する (レイヤー UI 非接続方針)
            view.DrawLabel("シェイプキーの編集はシェイプキーウィンドウで行ってください", -1, 20);
            view.DrawHorizontalLine(Color.gray);
            DrawModelManage(view);
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.ModelShapeKey;
        }
    }
}