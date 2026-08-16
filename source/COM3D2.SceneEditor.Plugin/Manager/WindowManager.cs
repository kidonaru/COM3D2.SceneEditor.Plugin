using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 他の Manager と違い ManagerBase ではなく MTEUtils の WindowManagerBase を継承する。
    /// ウィンドウ管理の実装を他プラグインと共有するための例外
    /// </summary>
    public class WindowManager : WindowManagerBase
    {
        private static GameViewManager gameViewManager => GameViewManager.instance;

        private bool _isCameraControlDisabled = false;

        /// <summary>
        /// プラグインのウィンドウを一時的に隠しているか。
        /// isShowWnd を書き換えずに描画だけ止めるため、復帰時は配置・タブ・連結がそのまま戻る。
        /// 一時的な表示切替なので config へは保存しない (セッション限り)。
        /// 復帰手段はメニューバーの「ウィンドウ表示」トグルだけなので、
        /// あちらに表示条件を付けるならここから戻れる経路も併せて用意すること
        /// </summary>
        public bool isWindowsHidden { get; set; }

        private static WindowManager _instance = null;
        public static WindowManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new WindowManager();
                }
                return _instance;
            }
        }

        private WindowManager()
        {
        }

        public override void Init()
        {
            base.Init();

            AddWindow(GameViewWindow.instance);
            AddWindow(MenuBarWindow.instance);
            AddWindow(SceneViewWindow.instance);
            AddWindow(HierarchyWindow.instance);
            AddWindow(InspectorWindow.instance);
            AddWindow(MaidCallWindow.instance);
            AddWindow(MaidPoseWindow.instance);
            AddWindow(MaidFaceWindow.instance);
            AddWindow(MaidFingerWindow.instance);
            AddWindow(MaidIKWindow.instance);
            AddWindow(MaidUndressWindow.instance);
            AddWindow(MaidGravityWindow.instance);
            AddWindow(BoneEditWindow.instance);
            AddWindow(CameraWindow.instance);
            AddWindow(BackgroundWindow.instance);
            AddWindow(BgmWindow.instance);
            AddWindow(LightWindow.instance);
            AddWindow(PngPlacementWindow.instance);
            AddWindow(PresetWindow.instance);
            AddWindow(HistoryWindow.instance);
            AddWindow(SettingWindow.instance);

            // ComboBoxPopupWindow はホストの描画中に開閉が確定するため、
            // コンボボックスを持つウィンドウより後に登録すること (同フレームで描画させる)
            AddWindow(ComboBoxPopupWindow.instance);
            // ColorPickerWindow も同様に、色欄を持つウィンドウの描画中に開閉が確定する
            AddWindow(ColorPickerWindow.instance);
            AddWindow(SavePresetPopupWindow.instance);
            AddWindow(SaveLayoutPopupWindow.instance);
            AddWindow(SavePosePopupWindow.instance);
            AddWindow(SaveFacePresetPopupWindow.instance);
            AddWindow(SaveFingerPresetPopupWindow.instance);
            AddWindow(DialogPopupWindow.instance);
        }

        protected override void OnAfterUpdate()
        {
            UpdateCameraControl();
        }

        /// <summary>
        /// 一時非表示中はメニューバー (復帰操作の入口) と GameView (ゲーム画面そのもの) 以外を描かない。
        /// 描画を止めれば GuiWindowTracker の矩形も期限切れになるため、
        /// 隠れた領域でのカメラ操作の抑止も自動で解ける
        /// </summary>
        public override void OnGUI()
        {
            if (!isWindowsHidden)
            {
                base.OnGUI();
                // ウィンドウより後に描いて最前面にする。一時非表示中も通知は出す
                ToastManager.OnGUI();
                return;
            }

            GUIView.InitStyles();

            // 登録順と同じ順で描き、重なり順を通常時と揃える
            GameViewWindow.instance.OnGUI();
            MenuBarWindow.instance.OnGUI();
            ToastManager.OnGUI();
        }

        protected override void OnBeforeScreenSizeDispatch()
        {
            // アプリウィンドウのリサイズ中にドラッグ追跡が残っていると、
            // 配置スケールによる移動をドラッグと誤認してスナップが発動するため中断する
            WindowConnectManager.instance.CancelDrag();
        }

        protected override void OnAfterScreenSizeDispatch()
        {
            // min クランプ等で連結グループの隣接がずれうるため、群単位でクランプし直す
            WindowConnectManager.instance.ClampGroups();
        }

        /// <summary>サブウィンドウの配置と表示状態を config へ書き出す</summary>
        public void SavePlacements()
        {
            foreach (var window in windows)
            {
                var subWindow = window as EditorSubWindow;
                if (subWindow != null)
                {
                    subWindow.SavePlacement();
                }
            }

            TabGroupManager.instance.SaveGroups();
            WindowConnectManager.instance.SaveGroups();
        }

        /// <summary>config に保存された表示状態を復元する</summary>
        public void RestoreVisibility()
        {
            foreach (var window in windows)
            {
                var subWindow = window as EditorSubWindow;
                if (subWindow != null)
                {
                    subWindow.isShowWnd = subWindow.savedVisible;
                }
            }

            // 表示状態を復元してからでないと、非表示ウィンドウをグループへ入れてしまう
            TabGroupManager.instance.RestoreGroups();
            WindowConnectManager.instance.RestoreGroups();
        }

        /// <summary>
        /// モード中はGameViewの描画領域内にカーソルがある時だけカメラ操作を許可する。
        /// 領域外 (他のGUIウィンドウ等) でのドラッグでカメラが回る誤爆を防ぐ。
        /// 判定を picking (InputRemapper) と同じ条件に揃えることで、座標変換されない
        /// レターボックスの余白や他プラグインのウィンドウ上でカメラだけ動くのを避ける
        /// </summary>
        private void UpdateCameraControl()
        {
            if (!gameViewManager.isWindowMode)
            {
                RestoreCameraControl();
                return;
            }

            var mainCamera = GameMain.Instance.MainCamera;
            if (mainCamera == null)
            {
                return;
            }

            // ギズモを掴んでいる間は、カーソルが描画領域内にあってもカメラを動かさない
            var gizmo = gameViewManager.gizmoRenderer;
            var shouldDisable = !InputRemapper.IsGameViewActiveAt(InputRemapper.rawGuiPosition)
                || (gizmo != null && gizmo.isDragging);

            if (shouldDisable)
            {
                // 自分が無効化する前から無効なら他プラグイン等の管理下なので触らない（復帰時に誤って有効化しないため）。
                // 無効化後に外部から有効へ戻された場合は毎フレーム無効化し直す
                if (_isCameraControlDisabled || mainCamera.GetControl())
                {
                    CameraControlArbiter.SetControl(mainCamera, false);
                    _isCameraControlDisabled = true;
                }
            }
            else if (_isCameraControlDisabled)
            {
                CameraControlArbiter.SetControl(mainCamera, true);
                _isCameraControlDisabled = false;
            }
        }

        private void RestoreCameraControl()
        {
            if (_isCameraControlDisabled)
            {
                _isCameraControlDisabled = false;

                var mainCamera = GameMain.Instance.MainCamera;
                if (mainCamera != null)
                {
                    CameraControlArbiter.SetControl(mainCamera, true);
                }
            }
        }

        protected override void OnBeforeCloseWindows()
        {
            RestoreCameraControl();

            // 閉じると表示状態が false になるため、先に保存する
            SavePlacements();
        }
    }
}
