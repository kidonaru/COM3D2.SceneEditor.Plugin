using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// IK 固定ウィンドウ。MTE の「IK固定」相当で、四肢の空間固定と足の接地を操作する。
    /// 「IKアニメーション」で有効にした箇所は、モーション再生中も固定が効く
    /// </summary>
    public class MaidIKWindow : MaidWindowBase
    {
        public static readonly int WINDOW_ID = 8903360;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "IK";

        public override bool TryFocusTimelineLayer(Type layerType)
        {
            return MotionLayerFocusUtils.ShouldFocus(layerType, MTEP.MotionLayerFocusWindow.IK);
        }

        /// <summary>行単位でまとめて操作する左右ペア（MTE と同じ並び）</summary>
        private static readonly MaidIKHoldType[][] HoldTypePairs =
        {
            new[] { MaidIKHoldType.Arm_L_Joint, MaidIKHoldType.Arm_R_Joint },
            new[] { MaidIKHoldType.Arm_L_Tip, MaidIKHoldType.Arm_R_Tip },
            new[] { MaidIKHoldType.Foot_L_Joint, MaidIKHoldType.Foot_R_Joint },
            new[] { MaidIKHoldType.Foot_L_Tip, MaidIKHoldType.Foot_R_Tip },
        };

        private static readonly int ToggleWidth = 90;
        private static readonly int PairButtonWidth = 50;

        private static MaidIKWindow _instance = null;
        public static MaidIKWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MaidIKWindow();
                }
                return _instance;
            }
        }

        private MaidIKWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.maidIKPosX;
            y = config.maidIKPosY;
            width = config.maidIKWidth;
            height = config.maidIKHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.maidIKPosX = x;
            config.maidIKPosY = y;
            config.maidIKWidth = width;
            config.maidIKHeight = height;
        }

        public override bool savedVisible
        {
            get => config.maidIKVisible;
            set => config.maidIKVisible = value;
        }

        private static MaidIKHoldController holdController => maidManager.ikHoldController;

        protected override void DrawMaidContent(Maid target)
        {
            if (target == null)
            {
                return;
            }

            TimelineLayerGate.Begin(view, typeof(MTEP.MotionTimelineLayer), target, ROW_HEIGHT);
            DrawBlendLayerGate(target);

            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            DrawHoldToggles(target);
            view.DrawHorizontalLine();
            DrawAnimeToggles(target);
            view.DrawHorizontalLine();
            DrawGrounding(target);

            view.EndScrollView();
        }

        private void DrawHoldToggles(Maid target)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel("IK固定", 60, ROW_HEIGHT);
            }
            view.EndLayout();

            foreach (var pair in HoldTypePairs)
            {
                view.BeginHorizontal();
                {
                    foreach (var type in pair)
                    {
                        var holdType = type;
                        view.DrawToggle(MaidIKHoldController.GetHoldTypeName(holdType),
                            holdController.GetHold(target, holdType), ToggleWidth, ROW_HEIGHT,
                            newValue =>
                            {
                                HistoryManager.instance.BeforeEdit(target, HistoryScope.IK,
                                    "IK固定: " + MaidIKHoldController.GetHoldTypeName(holdType));
                                holdController.SetHold(target, holdType, newValue);
                                MaidDragBoneTracker.NotifyBoneEdited(target);
                            });
                    }

                    var allHold = IsPairHeld(target, pair);
                    if (view.DrawButton(allHold ? "解除" : "固定", PairButtonWidth, ROW_HEIGHT))
                    {
                        HistoryManager.instance.BeforeEdit(target, HistoryScope.IK,
                            allHold ? "IK固定解除" : "IK固定");
                        foreach (var type in pair)
                        {
                            holdController.SetHold(target, type, !allHold);
                        }
                        MaidDragBoneTracker.NotifyBoneEdited(target);
                    }
                }
                view.EndLayout();
            }
        }

        /// <summary>再生中も固定を効かせる指定。固定 OFF の箇所は効かないので押させない</summary>
        private void DrawAnimeToggles(Maid target)
        {
            view.DrawLabel("IKアニメーション", -1, ROW_HEIGHT);

            foreach (var pair in HoldTypePairs)
            {
                view.BeginHorizontal();
                {
                    foreach (var type in pair)
                    {
                        var holdType = type;
                        view.DrawToggle(MaidIKHoldController.GetHoldTypeName(holdType),
                            holdController.GetAnime(target, holdType), ToggleWidth, ROW_HEIGHT,
                            holdController.GetHold(target, holdType),
                            newValue =>
                            {
                                HistoryManager.instance.BeforeEdit(target, HistoryScope.IK,
                                    "IKアニメ: " + MaidIKHoldController.GetHoldTypeName(holdType));
                                holdController.SetAnime(target, holdType, newValue);
                            });
                    }

                    var allHold = IsPairHeld(target, pair);
                    var allAnime = holdController.GetAnime(target, pair[0])
                        && holdController.GetAnime(target, pair[1]);
                    if (view.DrawButton(allAnime ? "無効" : "有効", PairButtonWidth, ROW_HEIGHT,
                        allHold))
                    {
                        HistoryManager.instance.BeforeEdit(target, HistoryScope.IK,
                            allAnime ? "IKアニメ無効" : "IKアニメ有効");
                        foreach (var type in pair)
                        {
                            holdController.SetAnime(target, type, !allAnime);
                        }
                    }
                }
                view.EndLayout();
            }
        }

        /// <summary>左右ペアの両方が固定されているか</summary>
        private bool IsPairHeld(Maid target, MaidIKHoldType[] pair)
        {
            return holdController.GetHold(target, pair[0])
                && holdController.GetHold(target, pair[1]);
        }

        private void DrawGrounding(Maid target)
        {
            var holdParams = holdController.GetParams(target);
            var paramUpdated = false;

            view.BeginHorizontal();
            {
                view.DrawToggle("左足の接地", holdParams.isGroundingFootL, ToggleWidth, ROW_HEIGHT,
                    newValue =>
                    {
                        HistoryManager.instance.BeforeEdit(target, HistoryScope.IK, "左足の接地");
                        holdParams.isGroundingFootL = newValue;
                        paramUpdated = true;
                    });

                view.DrawToggle("右足の接地", holdParams.isGroundingFootR, ToggleWidth, ROW_HEIGHT,
                    newValue =>
                    {
                        HistoryManager.instance.BeforeEdit(target, HistoryScope.IK, "右足の接地");
                        holdParams.isGroundingFootR = newValue;
                        paramUpdated = true;
                    });
            }
            view.EndLayout();

            // 接地は足首の固定が前提。忘れていたら気づけるように警告する
            if ((holdParams.isGroundingFootL
                    && !holdController.GetHold(target, MaidIKHoldType.Foot_L_Tip))
                || (holdParams.isGroundingFootR
                    && !holdController.GetHold(target, MaidIKHoldType.Foot_R_Tip)))
            {
                view.DrawLabel("足首の固定化が必要", -1, ROW_HEIGHT, Color.yellow);
            }

            // 既定値は MaidIKHoldParams のフィールド初期化子に一本化し、ここでは参照だけする
            var defaults = MaidIKHoldParams.Default;

            paramUpdated |= DrawParamSlider(target, "床の高さ", -10f, 10f, 0.01f,
                defaults.floorHeight, holdParams.floorHeight,
                newValue => holdParams.floorHeight = newValue);

            if (view.DrawButton("メイドの位置から推定", 150, ROW_HEIGHT))
            {
                HistoryManager.instance.BeforeEdit(target, HistoryScope.IK, "床の高さを推定");
                holdController.EstimateFloorHeight(target);
            }

            paramUpdated |= DrawParamSlider(target, "足首の高さ", 0f, 1f, 0.01f,
                defaults.footBaseOffset, holdParams.footBaseOffset,
                newValue => holdParams.footBaseOffset = newValue);

            paramUpdated |= DrawParamSlider(target, "伸ばす高さ", 0f, 1f, 0.01f,
                defaults.footStretchHeight, holdParams.footStretchHeight,
                newValue => holdParams.footStretchHeight = newValue);

            paramUpdated |= DrawParamSlider(target, "伸ばす角度", -180f, 180f, 1f,
                defaults.footStretchAngle, holdParams.footStretchAngle,
                newValue => holdParams.footStretchAngle = newValue);

            paramUpdated |= DrawParamSlider(target, "接地時角度", -180f, 180f, 1f,
                defaults.footGroundAngle, holdParams.footGroundAngle,
                newValue => holdParams.footGroundAngle = newValue);

            if (paramUpdated)
            {
                holdController.ResetTargetPosition(target, MaidIKHoldType.Foot_L_Tip);
                holdController.ResetTargetPosition(target, MaidIKHoldType.Foot_R_Tip);
            }
        }

        /// <summary>接地パラメータ用スライダー 1 本。変更されたら true</summary>
        private bool DrawParamSlider(Maid target, string label, float min, float max, float step,
            float defaultValue, float value, System.Action<float> onChanged)
        {
            return view.DrawSliderValue(new GUIView.SliderOption
            {
                label = label,
                labelWidth = LABEL_WIDTH,
                width = -1,
                min = min,
                max = max,
                step = step,
                defaultValue = defaultValue,
                value = value,
                onChanged = newValue =>
                {
                    HistoryManager.instance.BeforeEdit(target, HistoryScope.IK, label);
                    onChanged(newValue);
                },
            });
        }
    }
}
