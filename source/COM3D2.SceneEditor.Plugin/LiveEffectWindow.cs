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

        // ライトタブは常に 1 対象ぶんしか描かないので、行ドロワーも 1 つで足りる
        private readonly StageLightRowDrawer _lightRowDrawer = new StageLightRowDrawer();

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

            _lightRowDrawer.DrawControllerRows(view, controller, controller.name);

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

            _lightRowDrawer.DrawLightRows(view, controller, light, light.name);

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

        // レーザータブは常に 1 対象ぶんしか描かないので、行ドロワーも 1 つで足りる
        private readonly StageLaserRowDrawer _laserRowDrawer = new StageLaserRowDrawer();

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

            _laserRowDrawer.DrawControllerRows(view, controller, controller.name);

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

            _laserRowDrawer.DrawLaserRows(view, controller, laser, laser.name);

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

        // サイリウムタブは常に 1 対象ぶんしか描かないので、行ドロワーも 1 つで足りる
        private readonly PsylliumRowDrawer _psylliumRowDrawer = new PsylliumRowDrawer();

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

            _psylliumRowDrawer.DrawControllerRows(view, controller);

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

            _psylliumRowDrawer.DrawBarConfigRows(view, controller, controller.barConfig.name);

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

            _psylliumRowDrawer.DrawHandConfigRows(view, controller);

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

            _psylliumRowDrawer.DrawAreaRows(view, area);

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
