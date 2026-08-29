using System;
using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    [TimelineLayerDesc("効果音", 51)]
    public class SeTimelineLayer : TimelineLayerBase
    {
        public override Type layerType => typeof(SeTimelineLayer);
        public override string layerName => nameof(SeTimelineLayer);

        public static string SeBoneName = "SE";
        public static string SeDisplayName = "効果音";

        private List<string> _allBoneNames = new List<string> { SeBoneName };
        public override List<string> allBoneNames => _allBoneNames;

        private static TimelineSeManager seManager => TimelineSeManager.instance;

        // タイムライン固有の追加 SE と公式 SE を連結したコンボボックス用の一覧
        private List<string> _seNames = new List<string>();

        private SeTimelineLayer(int slotNo) : base(slotNo)
        {
        }

        public static SeTimelineLayer Create(int slotNo)
        {
            // 効果音はメイド単位ではないため常にスロット 0 の単一レイヤー
            return new SeTimelineLayer(0);
        }

        public override void Init()
        {
            base.Init();

            UpdateSeNames();
        }

        private void UpdateSeNames()
        {
            _seNames.Clear();
            _seNames.AddRange(timeline.additionalSeNames);
            _seNames.AddRange(seManager.seNames);
        }

        protected override void InitMenuItems()
        {
            allMenuItems.Clear();

            var menuItem = new BoneMenuItem(SeBoneName, SeDisplayName);
            allMenuItems.Add(menuItem);
        }

        public override bool IsValidData()
        {
            errorMessage = "";
            return true;
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
            if (indexUpdated)
            {
                var start = motion.start as TransformDataSe;
                seManager.PlaySe(start.fileName, start.interval, start.isLoop);
            }
        }

        public override void UpdateFrame(FrameData frame, bool initialEdit, bool force)
        {
            var trans = frame.GetOrCreateTransformData<TransformDataSe>(SeBoneName);
            trans.fileName = seManager.currentSeName;
            trans.interval = seManager.currentInterval;
            trans.isLoop = seManager.currentIsLoop;
        }

        // DCM 連携は未移植のため出力しない
        public override void OutputDCM(XElement songElement)
        {
        }

        private GUIComboBox<string> _seNameComboBox = new GUIComboBox<string>
        {
            getName = (seName, index) => seName,
        };

        private enum TabType
        {
            操作,
            管理,
        }

        private static TabType _tabType = TabType.操作;

        public override void DrawWindow(GUIView view)
        {
            // SE 版 GUIView には IsComboBoxFocused がないため focusedComboBox 判定に置き換え
            view.SetEnabled(view.focusedComboBox == null);

            _tabType = view.DrawTabs(_tabType, 50, 20);

            switch (_tabType)
            {
                case TabType.操作:
                    DrawSeControl(view);
                    break;
                case TabType.管理:
                    DrawSeManage(view);
                    break;
            }
        }

        public void DrawSeControl(GUIView view)
        {
            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            var updated = false;

            _seNameComboBox.items = _seNames;
            if (_seNameComboBox.currentItem != seManager.currentSeName)
            {
                _seNameComboBox.currentIndex = _seNameComboBox.items.IndexOf(seManager.currentSeName);
            }
            _seNameComboBox.onSelected = (seName, _) =>
            {
                seManager.currentSeName = seName;
                updated = true;
            };

            _seNameComboBox.DrawButton("SE名", view);

            updated |= view.DrawSliderValue(
                new GUIView.SliderOption
                {
                    label = "再生間隔",
                    labelWidth = 60,
                    min = 0f,
                    max = config.voiceMaxLength,
                    step = 0.01f,
                    defaultValue = 0f,
                    value = seManager.currentInterval,
                    onChanged = value => seManager.currentInterval = value,
                });

            view.DrawToggle("ループ", seManager.currentIsLoop, 80, 20, newValue =>
            {
                seManager.currentIsLoop = newValue;
                updated = true;
            });

            view.BeginHorizontal();
            {
                if (view.DrawButton("再生", 100, 20))
                {
                    updated = true;
                }

                if (view.DrawButton("初期化", 100, 20))
                {
                    seManager.PlaySe("", 0f, false);
                }
            }
            view.EndLayout();

            if (updated)
            {
                seManager.StopSe();
                seManager.PlaySe(seManager.currentSeName, seManager.currentInterval, seManager.currentIsLoop);
            }
        }

        private string _additionalSeName = "";

        public void DrawSeManage(GUIView view)
        {
            view.SetEnabled(view.focusedComboBox == null);

            view.DrawTextField(new GUIView.TextFieldOption
            {
                label = "SE名",
                labelWidth = 50,
                value = _additionalSeName,
                onChanged = value => _additionalSeName = value,
            });

            if (view.DrawButton("追加", 100, 20))
            {
                AddSe();
            }

            view.DrawHorizontalLine(Color.gray);

            view.BeginScrollView();
            {
                for (var i = 0; i < timeline.additionalSeNames.Count; i++)
                {
                    var seName = timeline.additionalSeNames[i];

                    view.BeginHorizontal();
                    {
                        view.DrawLabel(seName, view.viewRect.width - 50 - 10, 20);

                        if (view.DrawButton("削除", 50, 20))
                        {
                            timeline.additionalSeNames.Remove(seName);
                            UpdateSeNames();
                            break;
                        }
                    }
                    view.EndLayout();
                }
            }
            view.EndScrollView();
        }

        private void AddSe()
        {
            if (_additionalSeName == "")
            {
                return;
            }

            if (!_additionalSeName.EndsWith(".ogg"))
            {
                _additionalSeName += ".ogg";
            }

            if (!GameUty.FileSystem.IsExistentFile(_additionalSeName))
            {
                MTEUtils.ShowDialog($"ファイルが存在しません\n{_additionalSeName}");
                return;
            }

            if (!timeline.additionalSeNames.Contains(_additionalSeName))
            {
                timeline.additionalSeNames.Add(_additionalSeName);
                UpdateSeNames();
            }

            _additionalSeName = "";
        }

        public override SingleFrameType GetSingleFrameType(TransformType transformType)
        {
            return SingleFrameType.None;
        }

        public override TransformType GetTransformType(string name)
        {
            return TransformType.Se;
        }
    }
}
