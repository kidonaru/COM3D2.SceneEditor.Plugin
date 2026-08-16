using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityInjector;
using UnityInjector.Attributes;

namespace COM3D2.SceneEditor.Plugin
{
    public class GUIOption : GUIOptionBase
    {
        public override float keyRepeatTimeFirst => config.keyRepeatTimeFirst;
        public override float keyRepeatTime => config.keyRepeatTime;
        public override bool useHSVColor
        {
            get => config.useHSVColor;
            set
            {
                config.useHSVColor = value;
                config.dirty = true;
            }
        }
        public override Color windowHoverColor => config.windowHoverColor;
        public override Color accentColor => EditorSubWindow.ACCENT_COLOR;
        // 未指定だと白い四角になってしまうため、切り替えアイコンを差し込む
        public override Texture2D changeIcon =>
            ToolbarIcons.GetTexture(ToolbarIcons.Kind.Change) ?? base.changeIcon;
        // タイルのお気に入りボタン機構はプリセットの自動ロード指定に使うため、ホームアイコンを割り当てる
        public override Texture2D favoriteOffIcon =>
            ToolbarIcons.GetTexture(ToolbarIcons.Kind.Home);
        // 毎フレーム参照されるため、生成したテクスチャは保持して使い回す
        private Texture2D _favoriteOnIcon = null;
        // ON 状態は同じホームアイコンにアクセント色を乗算して表す
        public override Texture2D favoriteOnIcon
        {
            get
            {
                if (_favoriteOnIcon == null)
                {
                    _favoriteOnIcon = ToolbarIcons.CreateTintedTexture(
                        ToolbarIcons.Kind.Home, EditorSubWindow.ACCENT_COLOR);
                }
                return _favoriteOnIcon;
            }
        }

        private static Config config => ConfigManager.instance.config;
    }

    [
        PluginFilter("COM3D2x64"),
        PluginName(PluginInfo.PluginFullName),
        PluginVersion(PluginInfo.PluginVersion)
    ]
    public class SceneEditorPlugin : PluginBase
    {
        // ギアメニュー追従用の画面サイズ検知。WindowManager 側の検知は isEnable 中しか
        // 回らないため、プラグイン UI が閉じていても動くようここで独立して追跡する
        private int _gearLastScreenWidth = 0;
        private int _gearLastScreenHeight = 0;
        private bool _gearRepositionPending = false;

        private bool _isEnable = false;
        public bool isEnable
        {
            get => _isEnable;
            set
            {
                if (_isEnable == value)
                {
                    return;
                }

                _isEnable = value;
                UpdateGearMenu();

                if (value)
                {
                    OnPluginEnable();
                }
                else
                {
                    OnPluginDisable();
                }
            }
        }

        public static SceneEditorPlugin instance { get; private set; }

        private static ManagerRegistry managerRegistry => ManagerRegistry.instance;
        private static ConfigManager configManager => ConfigManager.instance;
        private static Config config => ConfigManager.instance.config;
        private static WindowManager windowManager => WindowManager.instance;

        public SceneEditorPlugin()
        {
        }

        public void Awake()
        {
            GameObject.DontDestroyOnLoad(this);
            instance = this;
        }

        public void Start()
        {
            try
            {
                Initialize();
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public void Update()
        {
            try
            {
                if (!config.pluginEnabled)
                {
                    return;
                }

                if (config.GetKeyDown(KeyBindType.PluginToggle))
                {
                    isEnable = !isEnable;
                }

                CheckGearMenuReposition();

                // SceneDaily のプリセット自動ロードは UI (isEnable) と無関係に進める。
                // 適用開始前の待機は UpdateAutoLoad 内の CharacterMgr.IsBusy 判定だけで
                // 完結するため、適用後の保留 (isLoading) だけ isEnable 無効中もポンプすれば足りる
                ScenePresetManager.UpdateAutoLoad();
                if (!isEnable)
                {
                    if (ScenePresetManager.isLoading)
                    {
                        MaidManipulateManager.instance.UpdateMaidLoading();
                    }

                    // 視線のマウス追従は UI 非表示中も止めない。
                    // プリセットで復元したマウスモードが固まったままになるため
                    MaidManipulateManager.instance.lookController.Update();
                }

                if (isEnable)
                {
                    UpdateGizmoToolKey();
                    UpdateHistoryKey();
                    UpdateEditModeKey();
                    managerRegistry.Update();
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        /// <summary>
        /// キー入力でギズモの操作種別を切り替える。
        /// テキスト入力中 (keyboardControl 保持中) は誤発動を防ぐため無視する
        /// </summary>
        private void UpdateGizmoToolKey()
        {
            if (GUIUtility.keyboardControl != 0)
            {
                return;
            }

            if (config.GetKeyDown(KeyBindType.GizmoMove))
            {
                GizmoRenderer.currentTool = GizmoTool.Move;
            }
            else if (config.GetKeyDown(KeyBindType.GizmoRotate))
            {
                GizmoRenderer.currentTool = GizmoTool.Rotate;
            }
            else if (config.GetKeyDown(KeyBindType.GizmoScale))
            {
                GizmoRenderer.currentTool = GizmoTool.Scale;
            }
        }

        /// <summary>
        /// キー入力で操作履歴を undo/redo する。
        /// テキスト入力中 (keyboardControl 保持中) は誤発動を防ぐため無視する
        /// </summary>
        private void UpdateHistoryKey()
        {
            if (GUIUtility.keyboardControl != 0)
            {
                return;
            }

            if (config.GetKeyDown(KeyBindType.Undo))
            {
                HistoryManager.instance.Undo();
            }
            else if (config.GetKeyDown(KeyBindType.Redo))
            {
                HistoryManager.instance.Redo();
            }
        }

        /// <summary>
        /// キー入力で編集モードを切り替える。
        /// テキスト入力中 (keyboardControl 保持中) は Tab のフォーカス移動と
        /// 取り合いになるため無視する
        /// </summary>
        private void UpdateEditModeKey()
        {
            if (GUIUtility.keyboardControl != 0)
            {
                return;
            }

            if (config.GetKeyDown(KeyBindType.EditModeToggle))
            {
                var manager = MaidManipulateManager.instance;
                manager.isEditMode = !manager.isEditMode;
            }
        }

        /// <summary>
        /// 画面サイズの変化を検知し、サイズが安定した最初のフレームで
        /// ギアメニューを画面右上へ再配置する
        /// </summary>
        private void CheckGearMenuReposition()
        {
            if (Screen.width != _gearLastScreenWidth || Screen.height != _gearLastScreenHeight)
            {
                // 初回 (初期値 0) は基準値の記録のみ行い、起動直後の不要な再配置を避ける
                bool isFirst = _gearLastScreenWidth == 0;
                _gearLastScreenWidth = Screen.width;
                _gearLastScreenHeight = Screen.height;
                _gearRepositionPending = !isFirst;
            }
            else if (_gearRepositionPending && GearMenu.Buttons.IsReady)
            {
                // SysShortcut 未生成中はフラグを保持し、生成後のフレームで再配置する
                _gearRepositionPending = false;
                GearMenu.Buttons.OnScreenSizeChanged();
            }
        }

        public void LateUpdate()
        {
            try
            {
                if (!config.pluginEnabled)
                {
                    return;
                }

                if (isEnable)
                {
                    managerRegistry.LateUpdate();
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
            try
            {
                if (!config.pluginEnabled)
                {
                    return;
                }

                if (scene.name == "SceneTitle")
                {
                    this.isEnable = false;
                }

                // ギアメニューアイコンが未追加または破棄済みなら再追加する
                // （Unity の == オーバーロードにより破棄済みオブジェクトも null 扱いになる）
                if (gearMenuIcon == null)
                {
                    AddGearMenu();
                }

                // UI の有効状態と無関係に発動させるため、マネージャ経由にしない
                ScenePresetManager.OnChangedSceneLevel(scene.name);

                managerRegistry.OnChangedSceneLevel(scene, sceneMode);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        void OnApplicationQuit()
        {
            // 有効なまま終了した場合も最新のウィンドウ位置を保存する。
            // 無効時は Close 済みで isShowWnd が false のため、呼ぶと
            // 保存済みの表示状態を false で上書きしてしまう
            if (isEnable)
            {
                GameViewWindow.instance.SavePlacement();
                windowManager.SavePlacements();
            }
            configManager.SaveConfigXml();
        }

        private void Initialize()
        {
            try
            {
                MTEUtils.Log("初期化中...");
                MTEUtils.LogDebug("Unity Version: " + Application.unityVersion);

                // GameView表示領域内では Input.mousePosition が RT座標へ変換されるため、
                // MTEUtils 側のスクリーン座標前提の処理には生座標を渡す
                MTEUtils.mousePositionGetter = () => InputRemapper.rawMousePosition;

                // MTEUtils 側の WindowResizeController が他窓被りを判定できるようにする
                MTEUtils.isOverOtherWindowChecker = GuiWindowTracker.IsOverWindowExcept;

                configManager.Init();

                GUIView.option = new GUIOption();

                if (!config.pluginEnabled)
                {
                    MTEUtils.Log("プラグインが無効になっています");
                    return;
                }

                SceneManager.sceneLoaded += OnChangedSceneLevel;

                // UI の有効状態と無関係に発動させるため、マネージャ登録には載せない
                ScenePresetAutoLoadPatch.Init();

                // ゲーム側の CameraMain.Update に直接フックするため、
                // マネージャの Update ループには乗らない (isEnable は自前で見ている)
                ScreenshotHotkeyPatch.Init();

                managerRegistry.RegisterManager(ConfigManager.instance);
                managerRegistry.RegisterManager(InputRemapper.instance);
                managerRegistry.RegisterManager(WindowManager.instance);
                managerRegistry.RegisterManager(GameViewManager.instance);
                managerRegistry.RegisterManager(SelectionManager.instance);
                managerRegistry.RegisterManager(SceneViewManager.instance);
                managerRegistry.RegisterManager(StudioLightManager.instance);
                managerRegistry.RegisterManager(PngPlacementManager.instance);
                // 選択状態が確定してからドラッグ点・ギズモを更新するため SelectionManager より後に登録する
                managerRegistry.RegisterManager(MaidManipulateManager.instance);
                // 操作対象メイドが確定してからボーンツリーを解決するため MaidManipulateManager より後に登録する
                managerRegistry.RegisterManager(BoneEditManager.instance);
                // 各操作の BeforeEdit を受けてマウス解放で確定するだけなので登録順は問わない
                managerRegistry.RegisterManager(HistoryManager.instance);
                // 各ウィンドウの状態更新後にドラッグ判定を行うため WindowManager より後に登録する
                managerRegistry.RegisterManager(TabGroupManager.instance);
                managerRegistry.RegisterManager(WindowConnectManager.instance);

                AddGearMenu();
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        GameObject gearMenuIcon = null;

        public void AddGearMenu()
        {
            // SysShortcut 生成前に呼ばれた場合は何もしない（シーンロード時に再試行される）
            if (!GearMenu.Buttons.IsReady)
            {
                return;
            }

            gearMenuIcon = GearMenu.Buttons.Add(
                PluginInfo.PluginName,
                PluginInfo.PluginName,
                PluginInfo.Icon,
                (go) =>
                {
                    isEnable = !isEnable;
                });
        }

        public void RemoveGearMenu()
        {
            if (gearMenuIcon != null)
            {
                GearMenu.Buttons.Remove(gearMenuIcon);
                gearMenuIcon = null;
            }
        }

        private void UpdateGearMenu()
        {
            if (gearMenuIcon != null)
            {
                GearMenu.Buttons.SetFrameColor(gearMenuIcon, isEnable ? Color.blue : Color.white);
            }
        }

        public void OnGUI()
        {
            try
            {
                if (isEnable)
                {
                    windowManager.OnGUI();
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        public void OnLoad()
        {
            MTEUtils.LogDebug("SceneEditorPlugin.OnLoad");
            managerRegistry.OnLoad();
        }

        private void OnPluginEnable()
        {
            MTEUtils.Log("プラグインが有効になりました");
            OnLoad();

            GameViewWindow.instance.isShowWnd = true;
            MenuBarWindow.instance.isShowWnd = true;
            GameViewManager.instance.EnterWindowMode();
            // 前回モードを終了した時点の SceneView 等の表示状態を復元する
            windowManager.RestoreVisibility();
            // 最大化は GameView を連結グループから外すため、グループ復元の後に行う
            GameViewManager.instance.RestoreMaximized();

            // 初回起動時は復元元の配置が無いため、同梱の既定レイアウトで初期配置を整える。
            // 表示状態も上書きするので RestoreVisibility の後に適用すること
            if (configManager.ConsumeFirstLaunch())
            {
                WindowLayoutManager.ApplyDefaultLayout();
            }
        }

        private void OnPluginDisable()
        {
            MTEUtils.Log("プラグインが無効になりました");
            // ウィンドウ位置を保存してから各マネージャを片付ける
            GameViewWindow.instance.SavePlacement();
            managerRegistry.OnPluginDisable();
        }
    }
}
