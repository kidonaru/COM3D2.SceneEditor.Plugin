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
                trans.easing = GetEasing(frame.frameNo, boneName);
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

        private GUIComboBox<string> _boneNameComboBox = new GUIComboBox<string>
        {
            getName = (boneName, index) => boneName,
        };

        private GUIComboBox<string> _fontNameComboBox = new GUIComboBox<string>
        {
            getName = (fontName, index) => fontName,
        };

        private GUIComboBox<TextAnchor> _textAlignmentComboBox = new GUIComboBox<TextAnchor>
        {
            items = Enum.GetValues(typeof(TextAnchor)).Cast<TextAnchor>().ToList(),
            getName = (type, index) => type.ToString(),
        };

        private ColorFieldCache _colorFieldValue = new ColorFieldCache("Color", true);

        public override void DrawWindow(GUIView view)
        {
            // SE 版 GUIView には IsComboBoxFocused がないため focusedComboBox 判定に置き換え
            view.SetEnabled(view.focusedComboBox == null);

            view.BeginHorizontal();
            {
                view.margin = 0;

                view.DrawLabel("テキスト表示数", view.labelWidth, 20);

                view.DrawIntField(new GUIView.IntFieldOption
                {
                    value = timeline.textCount,
                    width = view.viewRect.width - (view.labelWidth + 40 + view.padding.x * 2),
                    height = 20,
                    onChanged = x =>
                    {
                        timeline.textCount = x;
                    }
                });

                if (view.DrawButton("-", 20, 20))
                {
                    timeline.textCount--;
                }
                if (view.DrawButton("+", 20, 20))
                {
                    timeline.textCount++;
                }

                timeline.textCount = Mathf.Clamp(timeline.textCount, 1, 16);

                view.margin = GUIView.defaultMargin;
            }
            view.EndLayout();

            _boneNameComboBox.items = allBoneNames;
            _boneNameComboBox.DrawButton("対象", view);

            var boneName = _boneNameComboBox.currentItem;
            var index = _boneNameComboBox.currentIndex;

            if (!textManager.IsValidIndex(index))
            {
                return;
            }

            var freeTextSet = textManager.GetFreeTextSet(index);
            var text = freeTextSet.text;
            var rect = freeTextSet.rect;

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);
            view.BeginScrollView();

            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            view.DrawLabel("テキスト", -1, 20);

            view.DrawTextField(new GUIView.TextFieldOption
            {
                value = text.text,
                onChanged = value => text.text = value,
                maxLines = 3,
            });

            _fontNameComboBox.items = TimelineTextManager.fontNames;
            _fontNameComboBox.currentIndex = TimelineTextManager.fontNames.IndexOf(
                text.font != null ? text.font.name : "");
            _fontNameComboBox.onSelected = (fontName, _) =>
            {
                text.font = textManager.GetFont(fontName);
            };

            _fontNameComboBox.DrawButton("フォント", view);

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "サイズ",
                labelWidth = 30,
                fieldType = FloatFieldType.Int,
                min = 0,
                max = 200,
                step = 1,
                defaultValue = 50,
                value = text.fontSize,
                onChanged = value => text.fontSize = (int)value,
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "行間",
                labelWidth = 30,
                fieldType = FloatFieldType.Int,
                min = 0,
                max = 200,
                step = 1,
                defaultValue = 50,
                value = text.lineSpacing,
                onChanged = value => text.lineSpacing = value,
            });

            _textAlignmentComboBox.currentIndex = (int)text.alignment;
            _textAlignmentComboBox.onSelected = (alignment, _) =>
            {
                text.alignment = alignment;
            };

            _textAlignmentComboBox.DrawButton("整列", view);

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "幅",
                labelWidth = 30,
                fieldType = FloatFieldType.Int,
                min = 0,
                max = 2000,
                step = 1,
                defaultValue = 1000,
                value = rect.sizeDelta.x,
                onChanged = value =>
                {
                    var sizeDelta = rect.sizeDelta;
                    sizeDelta.x = value;
                    rect.sizeDelta = sizeDelta;
                }
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = "高さ",
                labelWidth = 30,
                fieldType = FloatFieldType.Int,
                min = 0,
                max = 2000,
                step = 1,
                defaultValue = 1000,
                value = rect.sizeDelta.y,
                onChanged = value =>
                {
                    var sizeDelta = rect.sizeDelta;
                    sizeDelta.y = value;
                    rect.sizeDelta = sizeDelta;
                }
            });

            view.DrawColor(
                _colorFieldValue,
                text.color,
                Color.white,
                c => text.color = c);

            DrawTransformRect(
                view,
                rect,
                TransformEditType.全て,
                DrawMaskAll,
                boneName,
                Vector3.zero,
                Vector3.zero,
                Vector3.one);

            view.SetEnabled(view.focusedComboBox == null);

            view.EndScrollView();
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.Text;
        }
    }
}
