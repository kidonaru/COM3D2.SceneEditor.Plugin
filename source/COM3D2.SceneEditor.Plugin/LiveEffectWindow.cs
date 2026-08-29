using COM3D2.MotionTimelineEditor;
using COM3D2.MotionTimelineEditor.Plugin;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ステージライト・ステージレーザー・サイリウムの編集ウィンドウ。
    /// タイムラインの各ライブ演出レイヤーから編集 UI を委譲された受け皿。
    /// 値はライブ状態 (各マネージャの実体) を直接編集するため、
    /// キーフレーム登録はタイムライン操作ウィンドウ側で行えばそのままキー化される
    /// </summary>
    public class LiveEffectWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903393;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "ライブ演出";

        private static readonly int ROW_HEIGHT = 20;

        private static MTEP.StudioHackManager studioHackManager => MTEP.StudioHackManager.instance;
        private static MTEP.StageLightManager stageLightManager => MTEP.StageLightManager.instance;
        private static MTEP.StageLaserManager stageLaserManager => MTEP.StageLaserManager.instance;
        private static MTEP.PsylliumManager psylliumManager => MTEP.PsylliumManager.instance;
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.Config timelineConfig => MTEP.ConfigManager.instance.config;

        /// <summary>
        /// ウィンドウ全体を覆うビュー。コンボのフォーカス集約専用。
        /// buttonPos をウィンドウ原点基準で扱うため、内容ビューを子にしてここへ集める
        /// </summary>
        private readonly GUIView _rootView = new GUIView();

        private readonly GUIView _view = new GUIView();

        private static LiveEffectWindow _instance = null;
        public static LiveEffectWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new LiveEffectWindow();
                }
                return _instance;
            }
        }

        private LiveEffectWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.liveEffectPosX;
            y = config.liveEffectPosY;
            width = config.liveEffectWidth;
            height = config.liveEffectHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.liveEffectPosX = x;
            config.liveEffectPosY = y;
            config.liveEffectWidth = width;
            config.liveEffectHeight = height;
        }

        public override bool savedVisible
        {
            get => config.liveEffectVisible;
            set => config.liveEffectVisible = value;
        }

        private enum TopTab
        {
            ライト,
            レーザー,
            サイリウム,
        }

        private TopTab _topTab = TopTab.ライト;

        protected override void DrawContent()
        {
            _rootView.Init(new Rect(0f, 0f, windowRect.width, windowRect.height));
            // 内容ビューを子にして、どちらに描いたコンボもフォーカス状態を共有させる
            _view.parent = _rootView;
            _view.Init(ToLocalRect(contentRect));

            DrawTopTabs();

            // ボタン押下で _rootView に登録されたフォーカスをポップアップへ引き渡す
            ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this);
        }

        private void DrawTopTabs()
        {
            if (!MTEP.TimelineBundleManager.instance.IsValid())
            {
                // ライブ演出はタイムライン同梱のアセットバンドルが前提 (レイヤーの ValidateLayer と同等)
                _view.DrawLabel("アセットバンドル未読込のため使用できません", -1, ROW_HEIGHT);
                return;
            }

            if (timelineManager.timeline == null)
            {
                // ライブ演出オブジェクトはタイムライン文脈でのみ生成・更新される
                // (各マネージャの Update は TimelineIntegration.UpdateGuards 通過時のみ回る)
                _view.DrawLabel("タイムライン読込後に使用できます", -1, ROW_HEIGHT);
                return;
            }

            _topTab = _view.DrawTabs(_topTab, 70, ROW_HEIGHT);

            switch (_topTab)
            {
                case TopTab.ライト:
                    DrawStageLight(_view);
                    break;
                case TopTab.レーザー:
                    DrawStageLaser(_view);
                    break;
                case TopTab.サイリウム:
                    DrawPsyllium(_view);
                    break;
            }
        }

        /// <summary>
        /// ウィンドウ内部のタブを描く。
        /// DrawTabs 末尾の AddSpace(5) が縦レイアウトでは「スペース5px + margin」に
        /// なるため、通常の行間に合わせて詰める (MaidWindowBase.DrawInnerTabs と同趣旨)
        /// </summary>
        private T DrawInnerTabs<T>(T currentTab, float width)
        {
            var result = _view.DrawTabs(currentTab, width, ROW_HEIGHT);
            _view.currentPos.y -= 5 + GUIView.defaultMargin;
            return result;
        }

        /// <summary>
        /// サイリウムの手動更新へ渡す再生時刻。レイヤー未追加時は 0 (静止) とする
        /// </summary>
        private static float psylliumPlayingTime
        {
            get
            {
                var layer = timelineManager.GetLayer<PsylliumTimelineLayer>();
                return layer != null ? layer.playingTime : 0f;
            }
        }

        /// <summary>
        /// 対象レイヤーの直前キーを引く。レイヤー未追加なら null
        /// </summary>
        private static BoneData GetPrevBone<T>(string boneName) where T : TimelineLayerBase
        {
            var layer = timelineManager.GetLayer<T>();
            return layer == null ? null : layer.GetPrevBone(timelineManager.currentFrameNo, boneName);
        }

        /// <summary>
        /// 回転欄はキーフレーム間の角度連続性を保つため直前キーの角度を基準にする。
        /// レイヤー未追加時は初期値へフォールバックする (レイヤー側 GetPrevBone と同じ挙動)
        /// </summary>
        private static Vector3 GetPrevEulerAngles<T>(string boneName, Vector3 initialEulerAngles, bool sub = false)
            where T : TimelineLayerBase
        {
            var prevBone = GetPrevBone<T>(boneName);
            if (prevBone == null)
            {
                return initialEulerAngles;
            }

            return sub ? prevBone.transform.subEulerAngles : prevBone.transform.eulerAngles;
        }

        private readonly GUIComboBox<StageLightController> _lightControllerComboBox = new GUIComboBox<StageLightController>
        {
            getName = (light, index) => light.displayName,
            labelWidth = 70,
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        private readonly GUIComboBox<StageLight> _lightComboBox = new GUIComboBox<StageLight>
        {
            getName = (light, index) => light.displayName,
            labelWidth = 70,
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        private readonly GUIComboBox<StageLight> _copyToLightComboBox = new GUIComboBox<StageLight>
        {
            getName = (light, index) => light.displayName,
            labelWidth = 70,
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        private readonly ColorFieldCache _lightColor1FieldValue = new ColorFieldCache("Color1", true);
        private readonly ColorFieldCache _lightColor2FieldValue = new ColorFieldCache("Color2", true);

        private enum StageLightTabType
        {
            一括,
            個別,
        }

        private StageLightTabType _stageLightTabType = StageLightTabType.一括;

        /// <summary>ライトタブ。一括 (コントローラー) と個別 (ライト) を切り替える</summary>
        private void DrawStageLight(GUIView view)
        {
            _stageLightTabType = DrawInnerTabs(_stageLightTabType, 50);

            switch (_stageLightTabType)
            {
                case StageLightTabType.一括:
                    DrawStageLightControllEdit(view);
                    break;
                case StageLightTabType.個別:
                    DrawStageLightEdit(view);
                    break;
            }
        }

        private void DrawStageLightControllEdit(GUIView view)
        {
            view.SetEnabled(view.focusedComboBox == null);

            view.BeginHorizontal();
            {
                view.margin = 0;

                view.DrawLabel("コントローラー数", view.labelWidth, 20);

                view.DrawIntField(new GUIView.IntFieldOption
                {
                    value = stageLightManager.controllers.Count,
                    width = view.viewRect.width - (view.labelWidth + 40 + view.padding.x * 2),
                    height = 20,
                });

                if (view.DrawButton("-", 20, 20))
                {
                    stageLightManager.RemoveController(true);
                }
                if (view.DrawButton("+", 20, 20))
                {
                    stageLightManager.AddController(true);
                }

                view.margin = GUIView.defaultMargin;
            }
            view.EndLayout();

            var controllers = stageLightManager.controllers;
            if (controllers.Count == 0)
            {
                view.DrawLabel("コントローラーが存在しません", 200, 20);
                return;
            }

            _lightControllerComboBox.items = controllers;
            _lightControllerComboBox.DrawButton("操作対象", view);

            var controller = _lightControllerComboBox.currentItem;
            if (controller == null)
            {
                view.DrawLabel("コントローラーを選択してください", 200, 20);
                return;
            }
            
            view.BeginHorizontal();
            {
                view.margin = 0;

                view.DrawLabel("ライト数", view.labelWidth, 20);

                view.DrawIntField(new GUIView.IntFieldOption
                {
                    value = controller.lights.Count,
                    width = view.viewRect.width - (view.labelWidth + 40 + view.padding.x * 2),
                    height = 20,
                });

                if (view.DrawButton("-", 20, 20))
                {
                    stageLightManager.RemoveLight(controller.groupIndex, true);
                }
                if (view.DrawButton("+", 20, 20))
                {
                    stageLightManager.AddLight(controller.groupIndex, true);
                }

                view.margin = GUIView.defaultMargin;
            }
            view.EndLayout();

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.BeginScrollView();

            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            var updateTransform = false;
            var defaultTrans = TransformDataStageLightController.defaultTrans;

            updateTransform |= view.DrawToggle("一括表示設定", controller.autoVisible, 200, 20, newValue =>
            {
                controller.autoVisible = newValue;
            });

            if (controller.autoVisible)
            {
                updateTransform |= view.DrawToggle("表示", controller.visible, 120, 20, newValue =>
                {
                    controller.visible = newValue;
                });
            }

            updateTransform |= view.DrawToggle("一括位置設定", controller.autoPosition, 200, 20, newValue =>
            {
                controller.autoPosition = newValue;
            });

            if (controller.autoPosition)
            {
                var initialPosition = defaultTrans.initialPosition;
                var transformCache = view.GetTransformCache(null);
                transformCache.position = controller.positionMin;

                view.DrawLabel("最小位置", 200, 20);

                updateTransform |= TimelineLayerBase.DrawPosition(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    initialPosition);

                if (updateTransform)
                {
                    controller.positionMin = transformCache.position;
                }

                initialPosition = defaultTrans.initialSubPosition;
                transformCache = view.GetTransformCache(null);
                transformCache.position = controller.positionMax;

                view.DrawLabel("最大位置", 200, 20);

                updateTransform |= TimelineLayerBase.DrawPosition(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    initialPosition);

                if (updateTransform)
                {
                    controller.positionMax = transformCache.position;
                }
            }

            updateTransform |= view.DrawToggle("一括角度設定", controller.autoRotation, 200, 20, newValue =>
            {
                controller.autoRotation = newValue;
            });

            if (controller.autoRotation)
            {
                var initialEulerAngles = defaultTrans.initialEulerAngles;
                var transformCache = view.GetTransformCache(null);
                transformCache.eulerAngles = controller.rotationMin;

                view.DrawLabel("最小角度", 200, 20);

                updateTransform |= TimelineLayerBase.DrawEulerAngles(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    GetPrevEulerAngles<StageLightTimelineLayer>(controller.name, initialEulerAngles),
                    initialEulerAngles);

                if (updateTransform)
                {
                    controller.rotationMin = transformCache.eulerAngles;
                }

                transformCache = view.GetTransformCache(null);
                transformCache.eulerAngles = controller.rotationMax;

                view.DrawLabel("最大角度", 200, 20);

                updateTransform |= TimelineLayerBase.DrawEulerAngles(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    GetPrevEulerAngles<StageLightTimelineLayer>(controller.name, initialEulerAngles, true),
                    initialEulerAngles);

                if (updateTransform)
                {
                    controller.rotationMax = transformCache.eulerAngles;
                }
            }

            updateTransform |= view.DrawToggle("一括色設定", controller.autoColor, 200, 20, newValue =>
            {
                controller.autoColor = newValue;
            });

            if (controller.autoColor)
            {
                view.DrawLabel("最小色", 200, 20);

                updateTransform |= view.DrawColor(
                    _lightColor1FieldValue,
                    controller.colorMin,
                    defaultTrans.initialColor,
                    c => controller.colorMin = c);

                view.DrawLabel("最大色", 200, 20);

                updateTransform |= view.DrawColor(
                    _lightColor2FieldValue,
                    controller.colorMax,
                    defaultTrans.initialSubColor,
                    c => controller.colorMax = c);
            }

            updateTransform |= view.DrawToggle("一括ライト情報設定", controller.autoLightInfo, 200, 20, newValue =>
            {
                controller.autoLightInfo = newValue;
            });

            if (controller.autoLightInfo)
            {
                var lightInfo = controller.lightInfo;

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.spotAngleInfo,
                    lightInfo.spotAngle,
                    x => lightInfo.spotAngle = x);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.spotRangeInfo,
                    lightInfo.spotRange,
                    x => lightInfo.spotRange = x);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.rangeMultiplierInfo,
                    lightInfo.rangeMultiplier,
                    x => lightInfo.rangeMultiplier = x);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.falloffExpInfo,
                    lightInfo.falloffExp,
                    x => lightInfo.falloffExp = x);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.noiseStrengthInfo,
                    lightInfo.noiseStrength,
                    x => lightInfo.noiseStrength = x);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.noiseScaleInfo,
                    lightInfo.noiseScale,
                    x => lightInfo.noiseScale = x);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.coreRadiusInfo,
                    lightInfo.coreRadius,
                    x => lightInfo.coreRadius = x);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.offsetRangeInfo,
                    lightInfo.offsetRange,
                    x => lightInfo.offsetRange = x);
                
                updateTransform |= view.DrawCustomValueInt(
                    defaultTrans.segmentAngleInfo,
                    lightInfo.segmentAngle,
                    x => lightInfo.segmentAngle = x);
                
                updateTransform |= view.DrawCustomValueInt(
                    defaultTrans.segmentRangeInfo,
                    lightInfo.segmentRange,
                    x => lightInfo.segmentRange = x);

                updateTransform |= view.DrawCustomValueBool(
                    defaultTrans.zTestInfo,
                    lightInfo.zTest,
                    x => lightInfo.zTest = x);
            }

            if (updateTransform)
            {
                controller.UpdateLights();
            }

            view.SetEnabled(view.focusedComboBox == null);
            view.EndScrollView();
        }

        private void DrawStageLightEdit(GUIView view)
        {
            view.SetEnabled(view.focusedComboBox == null);

            var controllers = stageLightManager.controllers;
            if (controllers.Count == 0)
            {
                view.DrawLabel("コントローラーが存在しません", 200, 20);
                return;
            }

            _lightControllerComboBox.items = controllers;
            _lightControllerComboBox.DrawButton("操作対象", view);

            var controller = _lightControllerComboBox.currentItem;
            if (controller == null)
            {
                view.DrawLabel("コントローラーを選択してください", 200, 20);
                return;
            }

            var lights = controller.lights;
            if (lights.Count == 0)
            {
                view.DrawLabel("ライトが存在しません", 200, 20);
                return;
            }

            _lightComboBox.items = lights;
            _lightComboBox.DrawButton("操作対象", view);

            var light = _lightComboBox.currentItem;

            if (light == null || light.transform == null)
            {
                view.DrawLabel("ライトを選択してください", 200, 20);
                return;
            }

            view.DrawHorizontalLine(Color.gray);

            view.AddSpace(5);

            view.BeginScrollView();

            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            view.DrawLabel(light.displayName, 200, 20);

            if (!controller.autoVisible)
            {
                view.DrawToggle("表示", light.visible, 120, 20, newValue =>
                {
                    light.visible = newValue;
                });
            }

            var transformCache = view.GetTransformCache();
            var defaultTrans = TransformDataStageLight.defaultTrans;
            transformCache.position = light.position;
            transformCache.eulerAngles = light.eulerAngles;
            var initialPosition = defaultTrans.initialPosition;
            var initialEulerAngles = defaultTrans.initialEulerAngles;
            var initialScale = Vector3.one;
            var updateTransform = false;
            var editType = TimelineLayerBase.TransformEditType.全て;

            if (!controller.autoPosition)
            {
                updateTransform |= TimelineLayerBase.DrawPosition(view, transformCache, editType, initialPosition);
            }

            if (!controller.autoRotation)
            {
                updateTransform |= TimelineLayerBase.DrawEulerAngles(view, transformCache, editType,
                    GetPrevEulerAngles<StageLightTimelineLayer>(light.name, initialEulerAngles), initialEulerAngles);
            }

            if (updateTransform)
            {
                light.position = transformCache.position;
                light.eulerAngles = transformCache.eulerAngles;
            }

            if (!controller.autoColor)
            {
                updateTransform |= view.DrawColor(
                    _lightColor1FieldValue,
                    light.color,
                    Color.white,
                    c => light.color = c);
            }

            if (!controller.autoLightInfo)
            {
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.spotAngleInfo,
                    light.spotAngle,
                    x => light.spotAngle = x);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.spotRangeInfo,
                    light.spotRange,
                    x => light.spotRange = x);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.rangeMultiplierInfo,
                    light.rangeMultiplier,
                    x => light.rangeMultiplier = x);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.falloffExpInfo,
                    light.falloffExp,
                    x => light.falloffExp = x);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.noiseStrengthInfo,
                    light.noiseStrength,
                    x => light.noiseStrength = x);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.noiseScaleInfo,
                    light.noiseScale,
                    x => light.noiseScale = x);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.coreRadiusInfo,
                    light.coreRadius,
                    x => light.coreRadius = x);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.offsetRangeInfo,
                    light.offsetRange,
                    x => light.offsetRange = x);
                
                updateTransform |= view.DrawCustomValueInt(
                    defaultTrans.segmentAngleInfo,
                    light.segmentAngle,
                    x => light.segmentAngle = x);
                
                updateTransform |= view.DrawCustomValueInt(
                    defaultTrans.segmentRangeInfo,
                    light.segmentRange,
                    x => light.segmentRange = x);

                updateTransform |= view.DrawCustomValueBool(
                    defaultTrans.zTestInfo,
                    light.zTest,
                    x => light.zTest = x);
            }

            view.DrawHorizontalLine(Color.gray);

            {
                _copyToLightComboBox.items = lights;
                _copyToLightComboBox.DrawButton("コピー先", view);

                var copyToLight = _copyToLightComboBox.currentItem;

                if (view.DrawButton("コピー", 60, 20))
                {
                    if (copyToLight != null && copyToLight != light)
                    {
                        copyToLight.CopyFrom(light);
                    }
                }

                view.DrawHorizontalLine(Color.gray);
                view.AddSpace(5);
            }

            view.SetEnabled(view.focusedComboBox == null);
            view.EndScrollView();
        }

        private readonly GUIComboBox<StageLaserController> _laserControllerComboBox = new GUIComboBox<StageLaserController>
        {
            getName = (laser, index) => laser.displayName,
            labelWidth = 70,
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        private readonly GUIComboBox<StageLaser> _laserComboBox = new GUIComboBox<StageLaser>
        {
            getName = (laser, index) => laser.displayName,
            labelWidth = 70,
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        private readonly GUIComboBox<StageLaserController> _copyToLaserControllerComboBox = new GUIComboBox<StageLaserController>
        {
            getName = (laser, index) => laser.displayName,
            labelWidth = 70,
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        private readonly GUIComboBox<StageLaser> _copyToLaserComboBox = new GUIComboBox<StageLaser>
        {
            getName = (laser, index) => laser.displayName,
            labelWidth = 70,
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        private readonly ColorFieldCache _laserColor1FieldValue = new ColorFieldCache("", true);
        private readonly ColorFieldCache _laserColor2FieldValue = new ColorFieldCache("", true);

        private enum StageLaserTabType
        {
            一括,
            個別,
        }

        private StageLaserTabType _stageLaserTabType = StageLaserTabType.一括;

        /// <summary>レーザータブ。一括 (コントローラー) と個別 (レーザー) を切り替える</summary>
        private void DrawStageLaser(GUIView view)
        {
            _stageLaserTabType = DrawInnerTabs(_stageLaserTabType, 50);

            switch (_stageLaserTabType)
            {
                case StageLaserTabType.一括:
                    DrawStageLaserControllEdit(view);
                    break;
                case StageLaserTabType.個別:
                    DrawStageLaserEdit(view);
                    break;
            }
        }

        private void DrawStageLaserControllEdit(GUIView view)
        {
            view.SetEnabled(view.focusedComboBox == null);

            view.BeginHorizontal();
            {
                view.margin = 0;

                view.DrawLabel("コントローラー数", view.labelWidth, 20);

                view.DrawIntField(new GUIView.IntFieldOption
                {
                    value = stageLaserManager.controllers.Count,
                    width = view.viewRect.width - (view.labelWidth + 40 + view.padding.x * 2),
                    height = 20,
                });

                if (view.DrawButton("-", 20, 20))
                {
                    stageLaserManager.RemoveController(true);
                }
                if (view.DrawButton("+", 20, 20))
                {
                    stageLaserManager.AddController(true);
                }

                view.margin = GUIView.defaultMargin;
            }
            view.EndLayout();

            var controllers = stageLaserManager.controllers;
            if (controllers.Count == 0)
            {
                view.DrawLabel("コントローラーが存在しません", 200, 20);
                return;
            }

            _laserControllerComboBox.items = controllers;
            _laserControllerComboBox.DrawButton("操作対象", view);

            var controller = _laserControllerComboBox.currentItem;
            if (controller == null)
            {
                view.DrawLabel("コントローラーを選択してください", 200, 20);
                return;
            }
            
            view.BeginHorizontal();
            {
                view.margin = 0;

                view.DrawLabel("レーザー数", view.labelWidth, 20);

                view.DrawIntField(new GUIView.IntFieldOption
                {
                    value = controller.lasers.Count,
                    width = view.viewRect.width - (view.labelWidth + 40 + view.padding.x * 2),
                    height = 20,
                });

                if (view.DrawButton("-", 20, 20))
                {
                    stageLaserManager.RemoveLaser(controller.groupIndex, true);
                }
                if (view.DrawButton("+", 20, 20))
                {
                    stageLaserManager.AddLaser(controller.groupIndex, true);
                }

                view.margin = GUIView.defaultMargin;
            }
            view.EndLayout();

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.BeginScrollView();

            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            var updateTransform = false;
            var defaultTrans = TransformDataStageLaserController.defaultTrans;

            {
                var initialPosition = defaultTrans.initialPosition;
                var transformCache = view.GetTransformCache(null);
                transformCache.position = controller.position;

                view.DrawLabel("位置", 200, 20);

                updateTransform |= TimelineLayerBase.DrawPosition(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    initialPosition);

                if (updateTransform)
                {
                    controller.position = transformCache.position;
                }
            }

            {
                var initialEulerAngles = defaultTrans.initialEulerAngles;
                var transformCache = view.GetTransformCache(null);
                transformCache.eulerAngles = controller.eulerAngles;

                view.DrawLabel("角度", 200, 20);

                updateTransform |= TimelineLayerBase.DrawEulerAngles(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    GetPrevEulerAngles<StageLaserTimelineLayer>(controller.name, initialEulerAngles),
                    initialEulerAngles);

                if (updateTransform)
                {
                    controller.eulerAngles = transformCache.eulerAngles;
                }
            }

            updateTransform |= view.DrawToggle("一括表示設定", controller.autoVisible, 200, 20, newValue =>
            {
                controller.autoVisible = newValue;
            });

            if (controller.autoVisible)
            {
                updateTransform |= view.DrawToggle("表示", controller.visible, 120, 20, newValue =>
                {
                    controller.visible = newValue;
                });
            }

            updateTransform |= view.DrawToggle("一括角度設定", controller.autoRotation, 200, 20, newValue =>
            {
                controller.autoRotation = newValue;
            });

            if (controller.autoRotation)
            {
                var initialEulerAngles = defaultTrans.initialRotationMin;
                var transformCache = view.GetTransformCache(null);
                transformCache.eulerAngles = controller.rotationMin;

                var prevBone = GetPrevBone<StageLaserTimelineLayer>(controller.name);
                var prevTransform = prevBone != null ? prevBone.transform as TransformDataStageLaserController : null;
                var prevAngles = prevTransform != null ? prevTransform.rotationMin : initialEulerAngles;

                view.DrawLabel("最小角度", 200, 20);

                updateTransform |= TimelineLayerBase.DrawEulerAngles(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    prevAngles,
                    initialEulerAngles);

                if (updateTransform)
                {
                    controller.rotationMin = transformCache.eulerAngles;
                }

                initialEulerAngles = defaultTrans.initialRotationMax;
                transformCache = view.GetTransformCache(null);
                transformCache.eulerAngles = controller.rotationMax;

                prevAngles = prevTransform != null ? prevTransform.rotationMax : initialEulerAngles;

                view.DrawLabel("最大角度", 200, 20);

                updateTransform |= TimelineLayerBase.DrawEulerAngles(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    prevAngles,
                    initialEulerAngles);

                if (updateTransform)
                {
                    controller.rotationMax = transformCache.eulerAngles;
                }
            }

            updateTransform |= view.DrawToggle("一括色設定", controller.autoColor, 200, 20, newValue =>
            {
                controller.autoColor = newValue;
            });

            if (controller.autoColor)
            {
                view.DrawLabel("中心色", 200, 20);

                updateTransform |= view.DrawColor(
                    _laserColor1FieldValue,
                    controller.color1,
                    defaultTrans.initialColor,
                    c => controller.color1 = c);

                view.DrawLabel("錯乱色", 200, 20);

                updateTransform |= view.DrawColor(
                    _laserColor2FieldValue,
                    controller.color2,
                    defaultTrans.initialSubColor,
                    c => controller.color2 = c);
            }

            updateTransform |= view.DrawToggle("一括レーザー情報設定", controller.autoLaserInfo, 200, 20, newValue =>
            {
                controller.autoLaserInfo = newValue;
            });

            if (controller.autoLaserInfo)
            {
                var laserInfo = controller.laserInfo;

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.intensityInfo,
                    laserInfo.intensity,
                    x => laserInfo.intensity = x);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.laserRangeInfo,
                    laserInfo.laserRange,
                    x => laserInfo.laserRange = x);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.laserWidthInfo,
                    laserInfo.laserWidth,
                    x => laserInfo.laserWidth = x);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.falloffExpInfo,
                    laserInfo.falloffExp,
                    x => laserInfo.falloffExp = x);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.noiseStrengthInfo,
                    laserInfo.noiseStrength,
                    x => laserInfo.noiseStrength = x);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.noiseScaleInfo,
                    laserInfo.noiseScale,
                    x => laserInfo.noiseScale = x);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.coreRadiusInfo,
                    laserInfo.coreRadius,
                    x => laserInfo.coreRadius = x);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.offsetRangeInfo,
                    laserInfo.offsetRange,
                    x => laserInfo.offsetRange = x);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.glowWidthInfo,
                    laserInfo.glowWidth,
                    x => laserInfo.glowWidth = x);
                
                updateTransform |= view.DrawCustomValueInt(
                    defaultTrans.segmentRangeInfo,
                    laserInfo.segmentRange,
                    x => laserInfo.segmentRange = x);

                updateTransform |= view.DrawCustomValueBool(
                    defaultTrans.zTestInfo,
                    laserInfo.zTest,
                    x => laserInfo.zTest = x);
            }

            if (updateTransform)
            {
                controller.UpdateLasers();
            }

            view.DrawHorizontalLine(Color.gray);

            {
                _copyToLaserControllerComboBox.items = controllers;
                _copyToLaserControllerComboBox.DrawButton("コピー先", view);

                var copyToController = _copyToLaserControllerComboBox.currentItem;

                if (view.DrawButton("コピー", 60, 20))
                {
                    if (copyToController != null && copyToController != controller)
                    {
                        copyToController.CopyFrom(controller);
                        copyToController.UpdateLasers();
                    }
                }

                view.DrawHorizontalLine(Color.gray);
                view.AddSpace(5);
            }

            view.SetEnabled(view.focusedComboBox == null);
            view.EndScrollView();
        }

        private void DrawStageLaserEdit(GUIView view)
        {
            view.SetEnabled(view.focusedComboBox == null);

            var controllers = stageLaserManager.controllers;
            if (controllers.Count == 0)
            {
                view.DrawLabel("コントローラーが存在しません", 200, 20);
                return;
            }

            _laserControllerComboBox.items = controllers;
            _laserControllerComboBox.DrawButton("操作対象", view);

            var controller = _laserControllerComboBox.currentItem;
            if (controller == null)
            {
                view.DrawLabel("コントローラーを選択してください", 200, 20);
                return;
            }

            var lasers = controller.lasers;
            if (lasers.Count == 0)
            {
                view.DrawLabel("レーザーが存在しません", 200, 20);
                return;
            }

            _laserComboBox.items = lasers;
            _laserComboBox.DrawButton("操作対象", view);

            var laser = _laserComboBox.currentItem;

            if (laser == null || laser.transform == null)
            {
                view.DrawLabel("レーザーを選択してください", 200, 20);
                return;
            }

            view.DrawHorizontalLine(Color.gray);

            view.AddSpace(5);

            view.BeginScrollView();

            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            view.DrawLabel(laser.displayName, 200, 20);

            if (!controller.autoVisible)
            {
                view.DrawToggle("表示", laser.visible, 120, 20, newValue =>
                {
                    laser.visible = newValue;
                });
            }

            var transformCache = view.GetTransformCache();
            var defaultTrans = TransformDataStageLaser.defaultTrans;
            transformCache.eulerAngles = laser.eulerAngles;
            var initialPosition = defaultTrans.initialPosition;
            var initialEulerAngles = defaultTrans.initialEulerAngles;
            var initialScale = Vector3.one;
            var updateTransform = false;
            var editType = TimelineLayerBase.TransformEditType.全て;

            if (!controller.autoRotation)
            {
                updateTransform |= TimelineLayerBase.DrawEulerAngles(view, transformCache, editType,
                    GetPrevEulerAngles<StageLaserTimelineLayer>(laser.name, initialEulerAngles), initialEulerAngles);
            }

            if (updateTransform)
            {
                laser.eulerAngles = transformCache.eulerAngles;
            }

            if (!controller.autoColor)
            {
                view.DrawLabel("中心色", 200, 20);

                updateTransform |= view.DrawColor(
                    _laserColor1FieldValue,
                    laser.color1,
                    Color.white,
                    c => laser.color1 = c);

                view.DrawLabel("錯乱色", 200, 20);

                updateTransform |= view.DrawColor(
                    _laserColor2FieldValue,
                    laser.color2,
                    Color.white,
                    c => laser.color2 = c);
            }

            if (!controller.autoLaserInfo)
            {
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.intensityInfo,
                    laser.intensity,
                    x => laser.intensity = x);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.laserRangeInfo,
                    laser.laserRange,
                    x => laser.laserRange = x);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.laserWidthInfo,
                    laser.laserWidth,
                    x => laser.laserWidth = x);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.falloffExpInfo,
                    laser.falloffExp,
                    x => laser.falloffExp = x);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.noiseStrengthInfo,
                    laser.noiseStrength,
                    x => laser.noiseStrength = x);

                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.noiseScaleInfo,
                    laser.noiseScale,
                    x => laser.noiseScale = x);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.coreRadiusInfo,
                    laser.coreRadius,
                    x => laser.coreRadius = x);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.offsetRangeInfo,
                    laser.offsetRange,
                    x => laser.offsetRange = x);
                
                updateTransform |= view.DrawCustomValueFloat(
                    defaultTrans.glowWidthInfo,
                    laser.glowWidth,
                    x => laser.glowWidth = x);
                
                updateTransform |= view.DrawCustomValueInt(
                    defaultTrans.segmentRangeInfo,
                    laser.segmentRange,
                    x => laser.segmentRange = x);

                updateTransform |= view.DrawCustomValueBool(
                    defaultTrans.zTestInfo,
                    laser.zTest,
                    x => laser.zTest = x);
            }

            view.DrawHorizontalLine(Color.gray);

            {
                _copyToLaserComboBox.items = lasers;
                _copyToLaserComboBox.DrawButton("コピー先", view);

                var copyToLaser = _copyToLaserComboBox.currentItem;

                if (view.DrawButton("コピー", 60, 20))
                {
                    if (copyToLaser != null && copyToLaser != laser)
                    {
                        copyToLaser.CopyFrom(laser);
                    }
                }

                view.DrawHorizontalLine(Color.gray);
                view.AddSpace(5);
            }

            view.SetEnabled(view.focusedComboBox == null);
            view.EndScrollView();
        }

        private readonly GUIComboBox<PsylliumController> _psylliumControllerComboBox = new GUIComboBox<PsylliumController>
        {
            getName = (psyllium, index) => psyllium.displayName,
            labelWidth = 70,
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        private readonly GUIComboBox<PsylliumArea> _areaComboBox = new GUIComboBox<PsylliumArea>
        {
            getName = (area, index) => area.displayName,
            labelWidth = 70,
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        private readonly GUIComboBox<PsylliumPattern> _patternComboBox = new GUIComboBox<PsylliumPattern>
        {
            getName = (pattern, index) => pattern.patternConfig.displayName,
            labelWidth = 70,
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        private readonly GUIComboBox<PsylliumController> _copyToPsylliumControllerComboBox = new GUIComboBox<PsylliumController>
        {
            getName = (psyllium, index) => psyllium.displayName,
            labelWidth = 70,
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        private readonly GUIComboBox<PsylliumPattern> _copyToPatternComboBox = new GUIComboBox<PsylliumPattern>
        {
            getName = (pattern, index) => pattern.patternConfig.displayName,
            labelWidth = 70,
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        private readonly GUIComboBox<PsylliumPattern> _copyToTransformComboBox = new GUIComboBox<PsylliumPattern>
        {
            getName = (pattern, index) => pattern.transformConfig.displayName,
            labelWidth = 70,
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        private readonly GUIComboBox<PsylliumArea> _copyToAreaComboBox = new GUIComboBox<PsylliumArea>
        {
            getName = (area, index) => area.displayName,
            labelWidth = 70,
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        private readonly ColorFieldCache _color1aFieldValue = new ColorFieldCache("", true);
        private readonly ColorFieldCache _color1bFieldValue = new ColorFieldCache("", true);
        private readonly ColorFieldCache _color1cFieldValue = new ColorFieldCache("", true);
        private readonly ColorFieldCache _color2aFieldValue = new ColorFieldCache("", true);
        private readonly ColorFieldCache _color2bFieldValue = new ColorFieldCache("", true);
        private readonly ColorFieldCache _color2cFieldValue = new ColorFieldCache("", true);

        private enum PsylliumTabType
        {
            基本,
            バー,
            持ち手,
            アニメ,
            エリア,
        }

        private enum HandTabType
        {
            両手,
            右手,
            左手,
        }

        private PsylliumTabType _psylliumTabType = PsylliumTabType.基本;
        private HandTabType _handTabType = HandTabType.両手;

        /// <summary>サイリウムタブ。基本 / バー / 持ち手 / アニメ / エリアを切り替える</summary>
        private void DrawPsyllium(GUIView view)
        {
            _psylliumTabType = DrawInnerTabs(_psylliumTabType, 50);

            switch (_psylliumTabType)
            {
                case PsylliumTabType.基本:
                    DrawPsylliumControllEdit(view);
                    break;
                case PsylliumTabType.バー:
                    DrawPsylliumBarConfigEdit(view);
                    break;
                case PsylliumTabType.持ち手:
                    DrawPsylliumHandConfigEdit(view);
                    break;
                case PsylliumTabType.アニメ:
                    DrawPsylliumPatternConfigEdit(view);
                    break;
                case PsylliumTabType.エリア:
                    DrawPsylliumAreaEdit(view);
                    break;
            }
        }

        private void DrawPsylliumControllEdit(GUIView view)
        {
            view.SetEnabled(view.focusedComboBox == null);

            view.BeginHorizontal();
            {
                view.margin = 0;

                view.DrawLabel("コントローラー数", view.labelWidth, 20);

                view.DrawIntField(new GUIView.IntFieldOption
                {
                    value = psylliumManager.controllers.Count,
                    width = view.viewRect.width - (view.labelWidth + 40 + view.padding.x * 2),
                    height = 20,
                });

                if (view.DrawButton("-", 20, 20))
                {
                    psylliumManager.RemoveController(true);
                }
                if (view.DrawButton("+", 20, 20))
                {
                    psylliumManager.AddController(true);
                }

                view.margin = GUIView.defaultMargin;
            }
            view.EndLayout();

            var controllers = psylliumManager.controllers;
            if (controllers.Count == 0)
            {
                view.DrawLabel("コントローラーが存在しません", 200, 20);
                return;
            }

            _psylliumControllerComboBox.items = controllers;
            _psylliumControllerComboBox.DrawButton("操作対象", view);

            var controller = _psylliumControllerComboBox.currentItem;
            if (controller == null)
            {
                view.DrawLabel("コントローラーを選択してください", 200, 20);
                return;
            }

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.BeginScrollView();

            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            var updateTransform = false;
            var defaultTrans = TransformDataPsylliumController.defaultTrans;
            var transformCache = view.GetTransformCache(null);

            updateTransform |= view.DrawToggle(controller.displayName, controller.visible, 200, 20, newValue =>
            {
                controller.visible = newValue;
            });

            {
                var initialPosition = defaultTrans.initialPosition;
                transformCache.position = controller.position;

                updateTransform |= TimelineLayerBase.DrawPosition(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    initialPosition);

                if (updateTransform)
                {
                    controller.position = transformCache.position;
                }
            }

            {
                var initialEulerAngles = defaultTrans.initialEulerAngles;
                transformCache.eulerAngles = controller.eulerAngles;

                updateTransform |= TimelineLayerBase.DrawEulerAngles(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    GetPrevEulerAngles<PsylliumTimelineLayer>(controller.name, initialEulerAngles),
                    initialEulerAngles);

                if (updateTransform)
                {
                    controller.eulerAngles = transformCache.eulerAngles;
                }
            }

            if (updateTransform)
            {
                controller.Refresh();
            }

            view.DrawHorizontalLine(Color.gray);

            {
                _copyToPsylliumControllerComboBox.items = controllers;
                _copyToPsylliumControllerComboBox.DrawButton("コピー先", view);

                var copyToController = _copyToPsylliumControllerComboBox.currentItem;

                if (view.DrawButton("コピー", 60, 20))
                {
                    if (copyToController != null && copyToController != controller)
                    {
                        copyToController.CopyFrom(controller);
                        copyToController.Refresh();
                    }
                }
            }

            view.SetEnabled(view.focusedComboBox == null);
            view.EndScrollView();
        }

        private void DrawPsylliumBarConfigEdit(GUIView view)
        {
            view.SetEnabled(view.focusedComboBox == null);

            var controllers = psylliumManager.controllers;
            if (controllers.Count == 0)
            {
                view.DrawLabel("コントローラーが存在しません", 200, 20);
                return;
            }

            _psylliumControllerComboBox.items = controllers;
            _psylliumControllerComboBox.DrawButton("操作対象", view);

            var controller = _psylliumControllerComboBox.currentItem;
            if (controller == null)
            {
                view.DrawLabel("コントローラーを選択してください", 200, 20);
                return;
            }

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.BeginScrollView();

            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            var barConfig = controller.barConfig;
            var updateTransform = false;
            var defaultTrans = TransformDataPsylliumBar.defaultTrans;

            view.DrawLabel("中心色1", 200, 20);

            updateTransform |= view.DrawColor(
                _color1aFieldValue,
                barConfig.color1a,
                Color.white,
                c => barConfig.color1a = c);

            view.DrawLabel("縁色1", 200, 20);

            updateTransform |= view.DrawColor(
                _color1bFieldValue,
                barConfig.color1b,
                Color.white,
                c => barConfig.color1b = c);

            view.DrawLabel("散乱色1", 200, 20);

            updateTransform |= view.DrawColor(
                _color1cFieldValue,
                barConfig.color1c,
                Color.white,
                c => barConfig.color1c = c);
            
            view.DrawLabel("中心色2", 200, 20);

            updateTransform |= view.DrawColor(
                _color2aFieldValue,
                barConfig.color2a,
                Color.white,
                c => barConfig.color2a = c);

            view.DrawLabel("縁色2", 200, 20);
            
            updateTransform |= view.DrawColor(
                _color2bFieldValue,
                barConfig.color2b,
                Color.white,
                c => barConfig.color2b = c);

            view.DrawLabel("散乱色2", 200, 20);

            updateTransform |= view.DrawColor(
                _color2cFieldValue,
                barConfig.color2c,
                Color.white,
                c => barConfig.color2c = c);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.baseScaleInfo,
                barConfig.baseScale,
                y => barConfig.baseScale = y);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.widthInfo,
                barConfig.width,
                y => barConfig.width = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.heightInfo,
                barConfig.height,
                y => barConfig.height = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.positionYInfo,
                barConfig.positionY,
                y => barConfig.positionY = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.radiusInfo,
                barConfig.radius,
                y => barConfig.radius = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.topThresholdInfo,
                barConfig.topThreshold,
                y => barConfig.topThreshold = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.cutoffAlphaInfo,
                barConfig.cutoffAlpha,
                y => barConfig.cutoffAlpha = y);

            if (updateTransform)
            {
                controller.Refresh();
            }

            view.SetEnabled(view.focusedComboBox == null);
            view.EndScrollView();
        }

        private void DrawPsylliumHandConfigEdit(GUIView view)
        {
            view.SetEnabled(view.focusedComboBox == null);

            var controllers = psylliumManager.controllers;
            if (controllers.Count == 0)
            {
                view.DrawLabel("コントローラーが存在しません", 200, 20);
                return;
            }

            _psylliumControllerComboBox.items = controllers;
            _psylliumControllerComboBox.DrawButton("操作対象", view);

            var controller = _psylliumControllerComboBox.currentItem;
            if (controller == null)
            {
                view.DrawLabel("コントローラーを選択してください", 200, 20);
                return;
            }

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.BeginScrollView();

            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            var handConfig = controller.handConfig;
            var updateTransform = false;
            var defaultTrans = TransformDataPsylliumHand.defaultTrans;
            var defaultConfig = TransformDataPsylliumHand.defaultConfig;

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.handSpacingInfo,
                handConfig.handSpacing,
                y => handConfig.handSpacing = y);
            
            var transformCache = view.GetTransformCache(null);

            view.DrawLabel("サイリウム間の位置", 200, 20);

            {
                var initialPosition = defaultConfig.barOffsetPosition;
                transformCache.position = handConfig.barOffsetPosition;

                updateTransform |= TimelineLayerBase.DrawPosition(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    initialPosition);

                if (updateTransform)
                {
                    handConfig.barOffsetPosition = transformCache.position;
                }
            }

            view.DrawLabel("サイリウム間の角度", 200, 20);

            {
                var initialEulerAngles = defaultConfig.barOffsetRotation;
                var prevEulerAngles = Vector3.zero;
                transformCache.eulerAngles = handConfig.barOffsetRotation;

                updateTransform |= TimelineLayerBase.DrawEulerAngles(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    prevEulerAngles,
                    initialEulerAngles);

                if (updateTransform)
                {
                    handConfig.barOffsetRotation = transformCache.eulerAngles;
                }
            }

            if (updateTransform)
            {
                controller.Refresh();
            }

            view.SetEnabled(view.focusedComboBox == null);
            view.EndScrollView();
        }

        private void DrawPsylliumPatternConfigEdit(GUIView view)
        {
            view.SetEnabled(view.focusedComboBox == null);

            var controllers = psylliumManager.controllers;
            if (controllers.Count == 0)
            {
                view.DrawLabel("コントローラーが存在しません", 200, 20);
                return;
            }

            _psylliumControllerComboBox.items = controllers;
            _psylliumControllerComboBox.DrawButton("操作対象", view);

            var controller = _psylliumControllerComboBox.currentItem;
            if (controller == null)
            {
                view.DrawLabel("コントローラーを選択してください", 200, 20);
                return;
            }

            view.BeginHorizontal();
            {
                view.margin = 0;

                view.DrawLabel("パターン数", view.labelWidth, 20);

                view.DrawIntField(new GUIView.IntFieldOption
                {
                    value = controller.patterns.Count,
                    width = view.viewRect.width - (view.labelWidth + 40 + view.padding.x * 2),
                    height = 20,
                });

                if (view.DrawButton("-", 20, 20))
                {
                    psylliumManager.RemovePattern(controller.groupIndex, true);
                }
                if (view.DrawButton("+", 20, 20))
                {
                    psylliumManager.AddPattern(controller.groupIndex, true);
                }

                view.margin = GUIView.defaultMargin;
            }
            view.EndLayout();

            var patterns = controller.patterns;
            if (patterns.Count == 0)
            {
                view.DrawLabel("パターンが存在しません", 200, 20);
                return;
            }

            _patternComboBox.items = patterns;
            _patternComboBox.DrawButton("操作対象", view);

            var pattern = _patternComboBox.currentItem;

            if (pattern == null)
            {
                view.DrawLabel("パターンを選択してください", 200, 20);
                return;
            }

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.BeginScrollView();

            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            var patternConfig = pattern.patternConfig;
            var updateTransform = false;
            var defaultTrans = TransformDataPsylliumPattern.defaultTrans;
            var defaultConfig = TransformDataPsylliumPattern.defaultConfig;

            _handTabType = DrawInnerTabs(_handTabType, 50);

            DrawPsylliumTransformConfigEdit(view);

            view.DrawLabel("ランダム位置", 200, 20);

            {
                var initialPosition = defaultConfig.randomPositionRange;
                var transformCache = view.GetTransformCache(null);
                transformCache.position = patternConfig.randomPositionRange;

                updateTransform |= TimelineLayerBase.DrawPosition(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    initialPosition);

                if (updateTransform)
                {
                    patternConfig.randomPositionRange = transformCache.position;
                }
            }

            view.DrawLabel("ランダム角度", 200, 20);

            {
                var initialEulerAngles = defaultConfig.randomEulerAnglesRange;
                var transformCache = view.GetTransformCache(null);
                var prevEulerAngles = Vector3.zero;
                transformCache.eulerAngles = patternConfig.randomEulerAnglesRange;

                updateTransform |= TimelineLayerBase.DrawEulerAngles(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    prevEulerAngles,
                    initialEulerAngles);

                if (updateTransform)
                {
                    patternConfig.randomEulerAnglesRange = transformCache.eulerAngles;
                }
            }
            
            updateTransform |= view.DrawCustomValueInt(
                defaultTrans.timeCountInfo,
                patternConfig.timeCount,
                y => patternConfig.timeCount = y);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.timeRangeInfo,
                patternConfig.timeRange,
                y => patternConfig.timeRange = y);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.timeShiftMinInfo,
                patternConfig.timeShiftMin,
                y => patternConfig.timeShiftMin = y);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.timeShiftMaxInfo,
                patternConfig.timeShiftMax,
                y => patternConfig.timeShiftMax = y);

            updateTransform |= view.DrawCustomValueIntRandom(
                defaultTrans.randomSeedInfo,
                patternConfig.randomSeed,
                y => patternConfig.randomSeed = y);

            if (updateTransform)
            {
                controller.ManualUpdate(psylliumPlayingTime);
            }

            {
                _copyToPatternComboBox.items = controller.patterns;
                _copyToPatternComboBox.DrawButton("コピー先", view);

                var copyToPattern = _copyToPatternComboBox.currentItem;

                if (view.DrawButton("コピー", 60, 20))
                {
                    if (copyToPattern != null && copyToPattern != pattern)
                    {
                        copyToPattern.patternConfig.CopyFrom(patternConfig);
                        controller.ManualUpdate(psylliumPlayingTime);
                    }
                }
            }

            view.SetEnabled(view.focusedComboBox == null);

            view.EndScrollView();
        }

        private void DrawPsylliumTransformConfigEdit(GUIView view)
        {
            var controller = _psylliumControllerComboBox.currentItem;
            if (controller == null)
            {
                view.DrawLabel("コントローラーを選択してください", 200, 20);
                return;
            }

            var pattern = _patternComboBox.currentItem;
            if (pattern == null)
            {
                view.DrawLabel("パターンを選択してください", 200, 20);
                return;
            }

            var transformConfig = pattern.transformConfig;
            var updateTransform = false;
            var defaultTrans = TransformDataPsylliumTransform.defaultTrans;
            var defaultConfig = TransformDataPsylliumTransform.defaultConfig;

            view.DrawLabel("移動", 200, 20);

            if (_handTabType == HandTabType.右手)
            {
                var initialPosition = defaultConfig.positionRight;
                var transformCache = view.GetTransformCache(null);
                transformCache.position = transformConfig.positionRight;

                updateTransform |= TimelineLayerBase.DrawPosition(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    initialPosition);

                if (updateTransform)
                {
                    transformConfig.positionRight = transformCache.position;
                }
            }
            else
            {
                var initialPosition = defaultConfig.positionLeft;
                var transformCache = view.GetTransformCache(null);
                transformCache.position = transformConfig.positionLeft;

                updateTransform |= TimelineLayerBase.DrawPosition(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    initialPosition);

                if (updateTransform)
                {
                    transformConfig.positionLeft = transformCache.position;
                }
            }

            view.DrawLabel("回転", 200, 20);

            if (_handTabType == HandTabType.右手)
            {
                var initialEulerAngles = defaultConfig.eulerAnglesRight;
                var prevEulerAngles = Vector3.zero;
                var transformCache = view.GetTransformCache(null);
                transformCache.eulerAngles = transformConfig.eulerAnglesRight;

                updateTransform |= TimelineLayerBase.DrawEulerAngles(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    prevEulerAngles,
                    initialEulerAngles);

                if (updateTransform)
                {
                    transformConfig.eulerAnglesRight = transformCache.eulerAngles;
                }
            }
            else
            {
                var initialEulerAngles = defaultConfig.eulerAnglesLeft;
                var prevEulerAngles = Vector3.zero;
                var transformCache = view.GetTransformCache(null);
                transformCache.eulerAngles = transformConfig.eulerAnglesLeft;

                updateTransform |= TimelineLayerBase.DrawEulerAngles(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    prevEulerAngles,
                    initialEulerAngles);

                if (updateTransform)
                {
                    transformConfig.eulerAnglesLeft = transformCache.eulerAngles;
                }
            }

            if (updateTransform)
            {
                if (_handTabType == HandTabType.両手)
                {
                    transformConfig.positionRight = transformConfig.positionLeft;
                    transformConfig.eulerAnglesRight = transformConfig.eulerAnglesLeft;
                }

                pattern.ClearTransformData();
                pattern.ApplyTransformData(transformConfig);
                controller.ManualUpdate(psylliumPlayingTime);
            }

            {
                _copyToTransformComboBox.items = controller.patterns;
                _copyToTransformComboBox.DrawButton("コピー先", view);

                var copyToPattern = _copyToTransformComboBox.currentItem;

                if (view.DrawButton("コピー", 60, 20))
                {
                    if (copyToPattern != null && copyToPattern != pattern)
                    {
                        copyToPattern.transformConfig.CopyFrom(transformConfig);
                        copyToPattern.ClearTransformData();
                        copyToPattern.ApplyTransformData(transformConfig);
                        controller.ManualUpdate(psylliumPlayingTime);
                    }
                }
            }

            view.DrawHorizontalLine(Color.gray);
        }

        private void DrawPsylliumAreaEdit(GUIView view)
        {
            view.SetEnabled(view.focusedComboBox == null);

            var controllers = psylliumManager.controllers;
            if (controllers.Count == 0)
            {
                view.DrawLabel("コントローラーが存在しません", 200, 20);
                return;
            }

            _psylliumControllerComboBox.items = controllers;
            _psylliumControllerComboBox.DrawButton("操作対象", view);

            var controller = _psylliumControllerComboBox.currentItem;
            if (controller == null)
            {
                view.DrawLabel("コントローラーを選択してください", 200, 20);
                return;
            }

            view.BeginHorizontal();
            {
                view.margin = 0;

                view.DrawLabel("エリア数", view.labelWidth, 20);

                view.DrawIntField(new GUIView.IntFieldOption
                {
                    value = controller.areas.Count,
                    width = view.viewRect.width - (view.labelWidth + 40 + view.padding.x * 2),
                    height = 20,
                });

                if (view.DrawButton("-", 20, 20))
                {
                    psylliumManager.RemoveArea(controller.groupIndex, true);
                }
                if (view.DrawButton("+", 20, 20))
                {
                    psylliumManager.AddArea(controller.groupIndex, true);
                }

                view.margin = GUIView.defaultMargin;
            }
            view.EndLayout();

            var areas = controller.areas;
            if (areas.Count == 0)
            {
                view.DrawLabel("エリアが存在しません", 200, 20);
                return;
            }

            _areaComboBox.items = areas;
            _areaComboBox.DrawButton("操作対象", view);

            var area = _areaComboBox.currentItem;

            if (area == null || area.transform == null)
            {
                view.DrawLabel("エリアを選択してください", 200, 20);
                return;
            }

            view.DrawHorizontalLine(Color.gray);

            view.AddSpace(5);

            view.BeginScrollView();

            view.SetEnabled(view.focusedComboBox == null && studioHackManager.isPoseEditing);

            var areaConfig = area.areaConfig;
            var transformCache = view.GetTransformCache();
            var defaultTrans = TransformDataPsylliumArea.defaultTrans;
            transformCache.position = areaConfig.position;
            transformCache.eulerAngles = areaConfig.rotation;
            var initialPosition = defaultTrans.initialPosition;
            var initialEulerAngles = defaultTrans.initialEulerAngles;
            var initialScale = Vector3.one;
            var updateTransform = false;
            var editType = TimelineLayerBase.TransformEditType.全て;

            updateTransform |= view.DrawToggle(area.displayName, areaConfig.visible, 200, 20, newValue =>
            {
                areaConfig.visible = newValue;
            });

            updateTransform |= TimelineLayerBase.DrawPosition(view, transformCache, editType, initialPosition);
            if (updateTransform)
            {
                areaConfig.position = transformCache.position;
            }

            updateTransform |= TimelineLayerBase.DrawEulerAngles(view, transformCache, editType,
                    GetPrevEulerAngles<PsylliumTimelineLayer>(area.name, initialEulerAngles), initialEulerAngles);
            if (updateTransform)
            {
                areaConfig.rotation = transformCache.eulerAngles;
            }

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.sizeXInfo,
                areaConfig.size.x,
                x => areaConfig.size.x = x);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.sizeYInfo,
                areaConfig.size.y,
                y => areaConfig.size.y = y);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.seatDistanceXInfo,
                areaConfig.seatDistance.x,
                x => areaConfig.seatDistance.x = x);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.seatDistanceYInfo,
                areaConfig.seatDistance.y,
                y => areaConfig.seatDistance.y = y);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.randomPositionRangeXInfo,
                areaConfig.randomPositionRange.x,
                x => areaConfig.randomPositionRange.x = x);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.randomPositionRangeYInfo,
                areaConfig.randomPositionRange.y,
                y => areaConfig.randomPositionRange.y = y);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.randomPositionRangeZInfo,
                areaConfig.randomPositionRange.z,
                z => areaConfig.randomPositionRange.z = z);

            view.DrawLabel("バー数の重み", 200, 20);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.barCountWeight0Info,
                areaConfig.barCountWeight0,
                y => areaConfig.barCountWeight0 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.barCountWeight1Info,
                areaConfig.barCountWeight1,
                y => areaConfig.barCountWeight1 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.barCountWeight2Info,
                areaConfig.barCountWeight2,
                y => areaConfig.barCountWeight2 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.barCountWeight3Info,
                areaConfig.barCountWeight3,
                y => areaConfig.barCountWeight3 = y);

            view.DrawLabel("色の重み", 200, 20);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.colorWeight1Info,
                areaConfig.colorWeight1,
                y => areaConfig.colorWeight1 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.colorWeight2Info,
                areaConfig.colorWeight2,
                y => areaConfig.colorWeight2 = y);

            view.DrawLabel("パターンの重み", 200, 20);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.patternWeight0Info,
                areaConfig.patternWeight0,
                y => areaConfig.patternWeight0 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.patternWeight1Info,
                areaConfig.patternWeight1,
                y => areaConfig.patternWeight1 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.patternWeight2Info,
                areaConfig.patternWeight2,
                y => areaConfig.patternWeight2 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.patternWeight3Info,
                areaConfig.patternWeight3,
                y => areaConfig.patternWeight3 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.patternWeight4Info,
                areaConfig.patternWeight4,
                y => areaConfig.patternWeight4 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.patternWeight5Info,
                areaConfig.patternWeight5,
                y => areaConfig.patternWeight5 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.patternWeight6Info,
                areaConfig.patternWeight6,
                y => areaConfig.patternWeight6 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.patternWeight7Info,
                areaConfig.patternWeight7,
                y => areaConfig.patternWeight7 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.patternWeight8Info,
                areaConfig.patternWeight8,
                y => areaConfig.patternWeight8 = y);
            
            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.patternWeight9Info,
                areaConfig.patternWeight9,
                y => areaConfig.patternWeight9 = y);

            updateTransform |= view.DrawCustomValueIntRandom(
                defaultTrans.randomSeedInfo,
                areaConfig.randomSeed,
                y => areaConfig.randomSeed = y);

            if (updateTransform)
            {
                area.Refresh();
            }

            view.DrawHorizontalLine(Color.gray);

            {
                _copyToAreaComboBox.items = areas;
                _copyToAreaComboBox.DrawButton("コピー先", view);

                view.BeginHorizontal();
                {
                    var copyToArea = _copyToAreaComboBox.currentItem;

                    if (view.DrawButton("コピー", 60, 20))
                    {
                        if (copyToArea != null && copyToArea != area)
                        {
                            copyToArea.CopyFrom(area, timelineConfig.psylliumAreaCopyIgnoreTransform);
                        }
                    }

                    if (view.DrawButton("全エリアにコピー", 120, 20))
                    {
                        foreach (var a in areas)
                        {
                            if (a != area)
                            {
                                a.CopyFrom(area, timelineConfig.psylliumAreaCopyIgnoreTransform);
                            }
                        }
                    }
                }
                view.EndLayout();

                view.DrawToggle("位置/角度/サイズを反映しない", timelineConfig.psylliumAreaCopyIgnoreTransform, 200, 20, newValue =>
                {
                    timelineConfig.psylliumAreaCopyIgnoreTransform = newValue;
                });

                view.DrawHorizontalLine(Color.gray);
                view.AddSpace(5);
            }

            view.SetEnabled(view.focusedComboBox == null);
            view.EndScrollView();
        }
    }
}
