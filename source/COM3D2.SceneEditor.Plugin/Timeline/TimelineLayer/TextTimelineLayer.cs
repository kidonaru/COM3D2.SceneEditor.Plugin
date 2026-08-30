using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("テキスト", 53)]
    public class TextTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(TextTimelineLayer);
        public override string layerName => nameof(TextTimelineLayer);

        public static string TextBoneName = "Text";
        public static string TextDisplayName = "テキスト";

        private List<string> _allBoneNames = new List<string>();
        public override List<string> allBoneNames
        {
            get
            {
                if (_allBoneNames.Count != timeline.textCount)
                {
                    _allBoneNames.Clear();
                    for (var i = 0; i < timeline.textCount; i++)
                    {
                        _allBoneNames.Add(TextBoneName + i);
                    }
                }
                return _allBoneNames;
            }
        }

        private static TimelineTextManager textManager => TimelineTextManager.instance;

        private TextTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static TextTimelineLayer Create(int slotNo)
        {
            // テキストはメイド単位ではないため常にスロット 0 の単一レイヤー
            return new TextTimelineLayer(0);
        }

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            foreach (var boneName in allBoneNames)
            {
                allMenuItems.Add(new BoneMenuItem(boneName, boneName));
            }
        }

        public override bool IsValidData()
        {
            errorMessage = "";
            return true;
        }

        public override void LateUpdate()
        {
            base.LateUpdate();

            // テキスト表示数の変更に追随する
            if (textManager.TextData.Length != timeline.textCount)
            {
                textManager.InitTexts();
                InitMenuItems();
                AddFirstBones(allBoneNames);
            }

            if (!studioHackManager.isPoseEditing)
            {
                ApplyPlayData();
            }
        }

        protected override void ApplyMotion(MotionData motion, float t, bool indexUpdated, MotionPlayData playData)
        {
            if (indexUpdated)
            {
                ApplyMotionInit(motion);
            }

            ApplyMotionUpdate(motion, t);
        }

        private void ApplyMotionInit(MotionData motion)
        {
            var start = motion.start as TransformDataText;

            if (!textManager.IsValidIndex(start.index))
            {
                return;
            }

            var freeTextSet = textManager.GetFreeTextSet(start.index);
            var text = freeTextSet.text;
            var rect = freeTextSet.rect;

            var textChanged = false;
            if (text.text != start.text)
            {
                text.text = start.text;
                textChanged = true;
            }
            if (!string.IsNullOrEmpty(start.text) &&
                (textChanged || (text.font != null && text.font.name != start.font)))
            {
                text.font = textManager.GetFont(start.font);
            }
            text.fontSize = start.fontSize;
            rect.localPosition = start.position;
            rect.eulerAngles = start.eulerAngles;
            rect.localScale = start.scale;
            text.color = start.color;
            text.lineSpacing = start.lineSpacing;
            text.alignment = start.alignment;
            rect.sizeDelta = start.sizeDelta;

            textManager.UpdateFreeTextSet(start.index, freeTextSet);
        }

        private void ApplyMotionUpdate(MotionData motion, float t)
        {
            var start = motion.start as TransformDataText;
            var end = motion.end as TransformDataText;

            if (!textManager.IsValidIndex(start.index))
            {
                return;
            }

            var freeTextSet = textManager.GetFreeTextSet(start.index);

            if (start.position != end.position)
            {
                freeTextSet.rect.localPosition = Vector3.Lerp(start.position, end.position, t);
            }
            if (start.eulerAngles != end.eulerAngles)
            {
                freeTextSet.rect.eulerAngles = Vector3.Lerp(start.eulerAngles, end.eulerAngles, t);
            }
            if (start.scale != end.scale)
            {
                freeTextSet.rect.localScale = Vector3.Lerp(start.scale, end.scale, t);
            }
            if (start.color != end.color)
            {
                freeTextSet.text.color = Color.Lerp(start.color, end.color, t);
            }

            textManager.UpdateFreeTextSet(start.index, freeTextSet);
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            for (var index = 0; index < timeline.textCount; index++)
            {
                if (!textManager.IsValidIndex(index))
                {
                    continue;
                }

                var boneName = TextBoneName + index;
                var freeTextSet = textManager.GetFreeTextSet(index);
                var text = freeTextSet.text;
                var rect = freeTextSet.rect;

                var trans = frame.GetOrCreateTransformData<TransformDataText>(boneName);
                trans.text = text.text;
                trans.font = text.font != null ? text.font.name : "";
                trans.position = rect.localPosition;
                trans.eulerAngles = rect.localEulerAngles;
                trans.scale = rect.localScale;
                trans.color = text.color;
                trans.index = index;
                trans.fontSize = text.fontSize;
                trans.lineSpacing = (int)text.lineSpacing;
                trans.alignment = text.alignment;
                trans.sizeDelta = rect.sizeDelta;
            }
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.Text;
        }
    }
}
