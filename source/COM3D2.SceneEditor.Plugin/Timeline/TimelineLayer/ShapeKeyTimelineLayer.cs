using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("メイドシェイプ", 13)]
    public partial class ShapeKeyTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(ShapeKeyTimelineLayer);
        public override string layerName => nameof(ShapeKeyTimelineLayer);

        public override bool hasSlotNo => true;

        private List<string> _allBoneNames = null;
        public override List<string> allBoneNames
        {
            get
            {
                if (_allBoneNames == null)
                {
                    var shapeKeys = timeline.GetMaidShapeKeys(slotNo);
                    _allBoneNames = new List<string>(shapeKeys);
                }

                return _allBoneNames;
            }
        }

        private ShapeKeyTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static ShapeKeyTimelineLayer Create(int slotNo)
        {
            return new ShapeKeyTimelineLayer(slotNo);
        }

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            _allBoneNames = null;
            foreach (var boneName in allBoneNames)
            {
                var menuItem = new BoneMenuItem(boneName, boneName);
                allMenuItems.Add(menuItem);
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

        protected override void ApplyPlayData()
        {
            if (maidCache == null)
            {
                return;
            }

            base.ApplyPlayData();

            maidCache.FixBlendValues(_playDataMap.Keys);
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            var start = motion.start as TransformDataShapeKey;
            var end = motion.end as TransformDataShapeKey;

            var weight = PluginUtils.HermiteValue(
                motion.stFrame * timeline.frameDuration,
                motion.edFrame * timeline.frameDuration,
                start.weightValue,
                end.weightValue,
                t);
            maidCache.SetBlendShapeValue(motion.name, weight);
        }

        public override void OnShapeKeyAdded(string shapeKey)
        {
            InitMenuItems();

            var boneNames = new List<string> { shapeKey };
            AddFirstBones(boneNames);
            ApplyCurrentFrame(true);
        }

        public override void OnShapeKeyRemoved(string shapeKey)
        {
            InitMenuItems();

            var boneNames = new List<string> { shapeKey };
            RemoveAllBones(boneNames);
            ApplyCurrentFrame(true);
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            foreach (var boneName in allBoneNames)
            {
                var trans = CreateTransformData<TransformDataShapeKey>(boneName);
                trans.easing = GetEasing(frame.frameNo, boneName);
                trans.weight = maidCache.GetBlendShapeValue(boneName);

                var bone = frame.CreateBone(trans);
                frame.UpdateBone(bone);
            }
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        private GUIComboBox<string> _slotNameComboBox = new GUIComboBox<string>
        {
            getName = (slotName, index) => slotName,
        };
        private Maid _maid = null;
        private List<string> _slotNames = new List<string>();

        private enum TabType
        {
            追加,
            操作,
        }

        private static TabType _tabType = TabType.追加;

        public override void DrawWindow(GUIView view)
        {
            var maid = maidManager.maid;
            var maidSlotNo = maidManager.maidSlotNo;

            if (maid == null || maidSlotNo == -1 || !maidManager.IsValid())
            {
                view.DrawLabel("メイドを配置してください", -1, 20);
                return;
            }

            view.SetEnabled(view.focusedComboBox == null);

            _tabType = view.DrawTabs(_tabType, 50, 20);

            switch (_tabType)
            {
                case TabType.追加:
                    DrawBlendShapesAdd(view);
                    break;
                case TabType.操作:
                    DrawBlendShapesEdit(view);
                    break;
            }

            // SE ではコンボのポップアップ描画をホストウィンドウ側 (ComboBoxPopupWindow) が行うため
            // view.DrawComboBox() は呼ばない
        }

        public void DrawBlendShapesAdd(GUIView view)
        {
            var maid = maidManager.maid;
            var maidSlotNo = maidManager.maidSlotNo;

            if (maid != _maid)
            {
                // COM3D2.5 の goSlot は直接列挙できないため、インデックス走査で両バージョンに対応する
                _slotNames = new List<string>();
                var slotCount = Mathf.Min((int) TBody.SlotID.end, maid.body0.goSlot.Count);
                for (var i = 0; i < slotCount; i++)
                {
                    var slot = maid.body0.GetSlot(i);
                    if (slot != null && slot.morph != null && slot.morph.hash.Count > 0)
                    {
                        _slotNames.Add(slot.Category);
                    }
                }
            }

            _slotNameComboBox.items = _slotNames;
            _slotNameComboBox.DrawButton("対象スロット", view);

            var slotName = _slotNameComboBox.currentItem;

            if (string.IsNullOrEmpty(slotName) || !maid.body0.IsSlotNo(slotName))
            {
                view.DrawLabel("対象スロットがありません", -1, 20);
                return;
            }

            var morph = maid.body0.GetSlot(slotName).morph;
            var tags = morph.GetTags();
            tags.Sort();

            view.DrawHorizontalLine(Color.gray);

            view.AddSpace(5);

            view.BeginScrollView();

            foreach (string tag in tags)
            {
                var enable = timeline.HasMaidShapeKey(maidSlotNo, tag);

                view.DrawToggle(tag, enable, -1, 20, newValue =>
                {
                    if (newValue)
                    {
                        timeline.AddMaidShapeKey(maidSlotNo, tag);
                    }
                    else
                    {
                        timeline.RemoveMaidShapeKey(maidSlotNo, tag);
                    }
                });
            }

            view.SetEnabled(view.focusedComboBox == null);
            view.EndScrollView();
        }

        public void DrawBlendShapesEdit(GUIView view)
        {
            var maid = maidManager.maid;
            var maidSlotNo = maidManager.maidSlotNo;

            view.DrawHorizontalLine(Color.gray);

            view.AddSpace(5);

            view.BeginScrollView();

            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            foreach (var menuItem in allMenuItems)
            {
                var shapeKey = menuItem.name;

                var blendShape = maidCache.GetBlendShape(shapeKey);
                if (blendShape == null)
                {
                    continue;
                }

                var weight = blendShape.weight;
                var updateTransform = false;

                view.DrawLabel(menuItem.displayName, -1, 20);

                updateTransform |= view.DrawSliderValue(
                    new GUIView.SliderOption
                    {
                        min = 0f,
                        max = 2f,
                        step = 0.01f,
                        defaultValue = 0f,
                        value = weight,
                        onChanged = x => weight = x,
                    });

                if (updateTransform)
                {
                    blendShape.weight = weight;
                    maidCache.FixBlendValues(new string[] { shapeKey });
                }
            }

            view.SetEnabled(view.focusedComboBox == null);
            view.EndScrollView();
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.ShapeKey;
        }
    }
}