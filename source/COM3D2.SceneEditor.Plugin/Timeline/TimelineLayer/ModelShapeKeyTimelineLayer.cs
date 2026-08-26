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

        private GUIComboBox<StudioModelStat> _modelComboBox = new GUIComboBox<StudioModelStat>
        {
            getName = (model, index) => model.displayName,
            buttonSize = new Vector2(200, 20),
            contentSize = new Vector2(200, 300),
        };

        private enum TabType
        {
            操作,
            管理,
        }

        private static TabType _tabType = TabType.操作;

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override void DrawWindow(GUIView view)
        {
            // SE ではコンボのポップアップ描画をホストウィンドウ側 (ComboBoxPopupWindow) が行うため view.DrawComboBox() は呼ばない
            _tabType = view.DrawTabs(_tabType, 50, 20);

            switch (_tabType)
            {
                case TabType.操作:
                    DrawBlendShapes(view);
                    break;
                case TabType.管理:
                    DrawModelManage(view);
                    break;
            }

        }

        public void DrawBlendShapes(GUIView view)
        {
            _modelComboBox.items = modelManager.models;

            if (modelManager.models.Count == 0)
            {
                view.DrawLabel("モデルが存在しません", 200, 20);
                return;
            }

            view.SetEnabled(view.focusedComboBox == null);

            view.DrawLabel("モデル選択", 200, 20);
            _modelComboBox.DrawButton(view);

            var model = _modelComboBox.currentItem;
            if (model == null || model.transform == null)
            {
                view.DrawLabel("モデルが見つかりません", 200, 20);
                return;
            }

            var blendShapes = model.blendShapes;
            if (blendShapes.Count == 0)
            {
                view.DrawLabel("シェイプキーが存在しません", 200, 20);
                return;
            }

            view.DrawHorizontalLine(Color.gray);

            view.AddSpace(5);

            view.BeginScrollView();
            {
                view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

                for (var i = 0; i < blendShapes.Count; i++)
                {
                    var blendShape = blendShapes[i];
                    var weight = blendShape.weight;
                    var updateTransform = false;

                    view.DrawLabel(blendShape.shapeKeyName, -1, 20);

                    updateTransform |= view.DrawSliderValue(
                        new GUIView.SliderOption
                        {
                            min = -1f,
                            max = 2f,
                            step = 0.01f,
                            defaultValue = 0f,
                            value = weight,
                            onChanged = x => weight = x,
                        });

                    if (updateTransform)
                    {
                        blendShape.weight = weight;
                        model.FixBlendValues();
                    }
                }
            }
            view.SetEnabled(view.focusedComboBox == null);
            view.EndScrollView();
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.ModelShapeKey;
        }
    }
}