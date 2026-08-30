using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// カメラの構図を数値/スライダーで確認・編集するウィンドウ。
    /// 操作対象は Main (CameraMain) / SceneView 用カメラ / サブカメラから選べる。
    /// Main は注視点・距離・回転・FOV を UltimateOrbitCamera の API で編集し、
    /// SceneView も同じ構図モデルを SceneViewCameraController の API で編集する。
    /// 値は毎フレーム読み戻すため、マウス操作や他機能による変更もそのまま表示へ反映される
    /// </summary>
    public class CameraWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903359;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "カメラ";

        private static readonly int ROW_HEIGHT = 20;
        private static readonly int LABEL_WIDTH = 70;

        private static readonly string[] TargetNames = { "Main", "SceneView", "サブカメラ" };
        private static readonly int TargetButtonWidth = 90;

        // カメラプリセットのスロット数 (ボタン 1〜10)
        private const int PresetCount = 10;
        private static readonly int PresetButtonWidth = 20;

        /// <summary>保存済みプリセットの右クリックメニュー項目</summary>
        private enum PresetMenuAction
        {
            Overwrite,
            Load,
            Remove,
        }

        private static readonly PresetMenuAction[] PresetMenuActions =
        {
            PresetMenuAction.Overwrite,
            PresetMenuAction.Load,
            PresetMenuAction.Remove,
        };

        private static readonly Vector2 PresetMenuContentSize = new Vector2(80, 60);

        /// <summary>操作対象。TargetNames の添字 (0: Main, 1: SceneView, 2: サブカメラ)</summary>
        private int _targetIndex = 0;

        // コンボのフォーカスはルートビューで共有されるため、内容ビューを子にする
        private readonly GUIView _rootView = new GUIView();
        private readonly GUIView _view = new GUIView();

        /// <summary>右クリックメニューの対象スロット (1〜10)</summary>
        private int _presetMenuSlot = 0;

        private readonly GUIComboBox<PresetMenuAction> _presetMenuComboBox =
            new GUIComboBox<PresetMenuAction>
            {
                items = new List<PresetMenuAction>(PresetMenuActions),
                getName = (action, _) => GetPresetMenuLabel(action),
                buttonSize = new Vector2(PresetButtonWidth, ROW_HEIGHT),
                contentSize = PresetMenuContentSize,
            };

        // メイドの部位へ注視点を移すフォーカス行のコンボ
        private readonly GUIComboBox<MTEP.MaidCache> _focusMaidComboBox =
            new GUIComboBox<MTEP.MaidCache>
            {
                getName = (maidCache, _) => maidCache == null ? "未選択" : maidCache.fullName,
                buttonSize = new Vector2(120, ROW_HEIGHT),
                contentSize = new Vector2(150, 300),
                showArrow = false,
            };

        private readonly GUIComboBox<MTEP.MaidPointType> _focusPointComboBox =
            new GUIComboBox<MTEP.MaidPointType>
            {
                items = Enum.GetValues(typeof(MTEP.MaidPointType))
                    .Cast<MTEP.MaidPointType>().ToList(),
                getName = (type, _) => MTEP.MaidCache.GetMaidPointTypeName(type),
                buttonSize = new Vector2(60, ROW_HEIGHT),
                contentSize = new Vector2(80, 300),
                showArrow = false,
            };

        // ---- サブカメラタブ ----

        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.StudioHackManager studioHackManager => MTEP.StudioHackManager.instance;
        private static MTEP.SubCameraManager subCameraManager => MTEP.SubCameraManager.instance;

        private readonly GUIComboBox<MTEP.SubCameraData> _subCameraComboBox =
            new GUIComboBox<MTEP.SubCameraData>
            {
                getName = (cameraData, _) => cameraData.displayName,
                labelWidth = LABEL_WIDTH,
                buttonSize = new Vector2(150, ROW_HEIGHT),
                contentSize = new Vector2(150, 300),
            };

        // 回転オフセットのキャッシュとコンボ開閉状態をカメラごとに分けるため名前で引く
        // (台数上限 8 なので減った分の掃除はしない)
        private readonly ItemRowDrawerCache<SubCameraRowDrawer> _subCameraRowDrawers =
            new ItemRowDrawerCache<SubCameraRowDrawer>();

        private static CameraWindow _instance = null;
        public static CameraWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new CameraWindow();
                }
                return _instance;
            }
        }

        private CameraWindow()
        {
            _presetMenuComboBox.onSelected = (action, _) => OnPresetMenuSelected(action);
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.cameraPosX;
            y = config.cameraPosY;
            width = config.cameraWidth;
            height = config.cameraHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.cameraPosX = x;
            config.cameraPosY = y;
            config.cameraWidth = width;
            config.cameraHeight = height;
        }

        public override bool savedVisible
        {
            get => config.cameraVisible;
            set => config.cameraVisible = value;
        }

        protected override void DrawContent()
        {
            _rootView.Init(new Rect(0f, 0f, windowRect.width, windowRect.height));
            _view.parent = _rootView;
            _view.Init(ToLocalRect(contentRect));

            DrawTargetRow();

            if (_targetIndex == 0)
            {
                // プリセットは Main カメラ専用のため他タブでは行を出さない
                DrawPresetRow();
                DrawMainCameraContent();
            }
            else if (_targetIndex == 1)
            {
                DrawSceneViewCameraContent();
            }
            else if (_targetIndex == 2)
            {
                DrawSubCameraContent();
            }

            // 右クリックで _rootView に登録されたフォーカスをポップアップへ引き渡す
            ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this);
        }

        /// <summary>操作対象カメラの切り替え行</summary>
        private void DrawTargetRow()
        {
            _view.BeginHorizontal();
            {
                _view.DrawLabel("対象", LABEL_WIDTH, ROW_HEIGHT);
                for (var i = 0; i < TargetNames.Length; i++)
                {
                    var isCurrent = _targetIndex == i;
                    if (_view.DrawButton(TargetNames[i], TargetButtonWidth, ROW_HEIGHT, true,
                        isCurrent ? Color.cyan : Color.white))
                    {
                        _targetIndex = i;
                    }
                }
            }
            _view.EndLayout();
        }

        /// <summary>
        /// メインカメラのプリセット行。
        /// 左クリックは保存済みならロード、未保存なら現在のカメラを新規登録。
        /// 保存済みの右クリックは上書き/ロード/削除のメニューを開く
        /// </summary>
        private void DrawPresetRow()
        {
            _view.BeginHorizontal();
            {
                _view.DrawLabel("プリセット", LABEL_WIDTH, ROW_HEIGHT);
                for (var slot = 1; slot <= PresetCount; slot++)
                {
                    var hasData = config.GetCameraPreset(slot) != null;

                    // 判定は同じサイズの DrawButton の直前で行う (次要素の矩形を先読みするため)
                    if (hasData && IsRightClickOnNextPresetButton())
                    {
                        OpenPresetMenu(slot);
                    }

                    if (_view.DrawButton(slot.ToString(), PresetButtonWidth, ROW_HEIGHT, true,
                        hasData ? Color.white : Color.gray))
                    {
                        OnPresetButton(slot);
                    }
                }
            }
            _view.EndLayout();
        }

        /// <summary>次に描くプリセットボタンの上で右クリックされたか</summary>
        private bool IsRightClickOnNextPresetButton()
        {
            var ev = Event.current;
            return ev.type == EventType.MouseDown && ev.button == 1 &&
                _view.IsMouseOverRect(PresetButtonWidth, ROW_HEIGHT);
        }

        /// <summary>プリセットボタン押下。保存済みならロード、未保存なら現在のカメラを登録する</summary>
        private void OnPresetButton(int slot)
        {
            // ポップアップ側はボタン上のクリックを閉じる契機にしないため、ここで閉じる
            ComboBoxPopupWindow.instance.Close();

            var stored = config.GetCameraPreset(slot);
            if (stored != null)
            {
                LoadPreset(slot, stored);
            }
            else
            {
                SavePreset(slot);
            }
        }

        /// <summary>保存済みスロットの右クリックメニューを開く。同じスロットの再右クリックは閉じる</summary>
        private void OpenPresetMenu(int slot)
        {
            Event.current.Use();

            // コンボは全スロット共用のため、開いたままだと ProcessFocus のトグルが別スロットで誤爆する
            var wasOpen = ComboBoxPopupWindow.instance.IsOpenFor(this);
            ComboBoxPopupWindow.instance.Close();
            if (wasOpen && _presetMenuSlot == slot)
            {
                return;
            }

            _presetMenuSlot = slot;
            // 前回の選択を引きずって項目がハイライトされないようにする
            _presetMenuComboBox.currentIndex = -1;
            _presetMenuComboBox.buttonPos =
                _view.GetDrawRect(PresetButtonWidth, ROW_HEIGHT).position + _view.scrollOffset;
            _view.SetFocusComboBox(_presetMenuComboBox);
        }

        /// <summary>右クリックメニューの表示名</summary>
        private static string GetPresetMenuLabel(PresetMenuAction action)
        {
            switch (action)
            {
                case PresetMenuAction.Overwrite: return "上書き";
                case PresetMenuAction.Load: return "ロード";
                case PresetMenuAction.Remove: return "削除";
                default: return "";
            }
        }

        /// <summary>右クリックメニューの選択</summary>
        private void OnPresetMenuSelected(PresetMenuAction action)
        {
            var slot = _presetMenuSlot;
            switch (action)
            {
                case PresetMenuAction.Overwrite:
                    SavePreset(slot);
                    break;
                case PresetMenuAction.Load:
                    var stored = config.GetCameraPreset(slot);
                    if (stored != null)
                    {
                        LoadPreset(slot, stored);
                    }
                    break;
                case PresetMenuAction.Remove:
                    if (config.RemoveCameraPreset(slot))
                    {
                        config.dirty = true;
                    }
                    break;
            }
        }

        /// <summary>現在のメインカメラの構図をスロットへ保存する (新規登録・上書き共用)</summary>
        private void SavePreset(int slot)
        {
            var mainCamera = GameMain.Instance.MainCamera;
            var camera = mainCamera != null ? mainCamera.camera : null;
            if (camera == null)
            {
                return;
            }

            config.SetCameraPreset(slot, SerializeCameraPreset(mainCamera, camera));
            config.dirty = true;
        }

        /// <summary>保存済みの構図をメインカメラへ適用する</summary>
        private void LoadPreset(int slot, string stored)
        {
            var mainCamera = GameMain.Instance.MainCamera;
            var camera = mainCamera != null ? mainCamera.camera : null;
            if (camera == null)
            {
                return;
            }

            MainCameraRowDrawer.RecordCameraEdit("プリセット " + slot);
            ApplyCameraPreset(mainCamera, camera, stored);
        }

        /// <summary>カメラ状態を "tx,ty,tz,dist,yaw,pitch,roll,fov" 形式へ変換する</summary>
        private static string SerializeCameraPreset(CameraMain mainCamera, Camera camera)
        {
            var targetPos = mainCamera.GetTargetPos();
            var aroundAngle = mainCamera.GetAroundAngle();
            return string.Format(CultureInfo.InvariantCulture,
                "{0:F4},{1:F4},{2:F4},{3:F4},{4:F2},{5:F2},{6:F2},{7:F2}",
                targetPos.x, targetPos.y, targetPos.z,
                mainCamera.GetDistance(),
                aroundAngle.x, aroundAngle.y,
                camera.transform.eulerAngles.z,
                camera.fieldOfView);
        }

        /// <summary>保存済みプリセット文字列をカメラへ適用する。不正な文字列は無視する</summary>
        private static void ApplyCameraPreset(CameraMain mainCamera, Camera camera, string value)
        {
            var parts = value.Split(',');
            if (parts.Length != 8)
            {
                return;
            }

            var values = new float[8];
            for (var i = 0; i < 8; i++)
            {
                // TryParse は "NaN"/"Infinity" も受理するため、カメラが破綻しないよう弾く
                if (!float.TryParse(parts[i], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out values[i]) ||
                    float.IsNaN(values[i]) || float.IsInfinity(values[i]))
                {
                    return;
                }
            }

            // 並び順は SerializeCameraPreset の書式と一致させること
            var targetPos = new Vector3(values[0], values[1], values[2]);
            var distance = values[3];
            var aroundAngle = new Vector2(values[4], values[5]);
            var roll = values[6];
            var fov = values[7];

            mainCamera.SetTargetPos(targetPos);
            mainCamera.SetDistance(distance);
            mainCamera.SetAroundAngle(aroundAngle);

            var eulerAngles = camera.transform.eulerAngles;
            eulerAngles.z = roll;
            camera.transform.eulerAngles = eulerAngles;

            camera.fieldOfView = fov;
        }

        private void DrawMainCameraContent()
        {
            var mainCamera = GameMain.Instance.MainCamera;
            var camera = mainCamera != null ? mainCamera.camera : null;

            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            if (mainCamera == null || camera == null)
            {
                _view.DrawLabel("メインカメラが見つかりません", -1, ROW_HEIGHT,
                    textColor: Color.yellow);
                return;
            }

            _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            MainCameraRowDrawer.DrawTargetPosRow(_view, mainCamera, LABEL_WIDTH, ROW_HEIGHT);
            _view.DrawHorizontalLine();
            MainCameraRowDrawer.DrawAngleSliders(_view, mainCamera, camera, LABEL_WIDTH, ROW_HEIGHT);
            _view.DrawHorizontalLine();
            MainCameraRowDrawer.DrawDistanceFovSliders(_view, mainCamera, camera, LABEL_WIDTH, ROW_HEIGHT);
            _view.DrawHorizontalLine();
            DrawResetAndMatchSceneViewRow(mainCamera, camera);
            _view.DrawHorizontalLine();
            DrawFocusRow(mainCamera);

            _view.EndScrollView();
        }

        /// <summary>
        /// SceneView カメラを編集できる状態か。
        /// cameraController は破棄済み Transform を掴んだままになりうるため、
        /// null チェックだけでなく isActive も併せて見る (SceneViewWindow の注意書きに従う)
        /// </summary>
        private static bool TryGetSceneViewCamera(out Camera camera,
            out SceneViewCameraController controller)
        {
            camera = SceneViewManager.instance.sceneCamera;
            controller = SceneViewWindow.instance.cameraController;
            return camera != null && controller != null && SceneViewManager.instance.isActive;
        }

        /// <summary>
        /// SceneView 用カメラの編集。Main と同じく注視点・距離・回転のオービットモデルで編集する
        /// (実体は SceneViewCameraController の公開 API)
        /// </summary>
        private void DrawSceneViewCameraContent()
        {
            Camera camera;
            SceneViewCameraController controller;
            var available = TryGetSceneViewCamera(out camera, out controller);

            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            if (!available)
            {
                _view.DrawLabel("SceneView が開かれていません", -1, ROW_HEIGHT,
                    textColor: Color.yellow);
                return;
            }

            _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            // リセット先はメインカメラの初期注視点と同じ座標 (SceneView 独自の値ではない)
            Vector3RowDrawer.Draw(_view, "注視点", MainCameraRowDrawer.PositionDragSensitivity,
                LABEL_WIDTH, ROW_HEIGHT, controller.targetPos,
                value => controller.targetPos = value,
                () => controller.targetPos = MainCameraRowDrawer.DefaultTargetPos);

            _view.DrawHorizontalLine();

            // 旋回で 0〜360 に丸まるため、表示は ±180 度へ正規化する
            var aroundAngle = controller.aroundAngle;
            var yaw = AngleUtils.NormalizeAngle(aroundAngle.x);
            var pitch = AngleUtils.NormalizeAngle(aroundAngle.y);

            MainCameraRowDrawer.DrawAxisSlider(_view, "ヨー", yaw, -180f, 180f, 0.1f,
                AngleUtils.NormalizeAngle(MainCameraRowDrawer.DefaultAroundAngle.x),
                LABEL_WIDTH, ROW_HEIGHT,
                value => controller.aroundAngle = new Vector2(value, pitch));
            MainCameraRowDrawer.DrawAxisSlider(_view, "ピッチ", pitch, -90f, 90f, 0.1f,
                MainCameraRowDrawer.DefaultAroundAngle.y, LABEL_WIDTH, ROW_HEIGHT,
                value => controller.aroundAngle = new Vector2(yaw, value));

            _view.DrawHorizontalLine();

            MainCameraRowDrawer.DrawAxisSlider(_view, "距離", controller.distance, 0.1f, 30f, 0.01f,
                MainCameraRowDrawer.DefaultDistance, LABEL_WIDTH, ROW_HEIGHT,
                value => controller.distance = value);

            MainCameraRowDrawer.DrawAxisSlider(_view, "FOV", camera.fieldOfView, 1f, 179f, 0.1f,
                MainCameraRowDrawer.DefaultFov, LABEL_WIDTH, ROW_HEIGHT,
                value => camera.fieldOfView = value);

            _view.DrawHorizontalLine();

            _view.BeginHorizontal();
            {
                // Main のリセットと同じ構図 (注視点・距離・回転・FOV) へ戻す
                if (_view.DrawButton("リセット", 100, ROW_HEIGHT))
                {
                    controller.targetPos = MainCameraRowDrawer.DefaultTargetPos;
                    controller.distance = MainCameraRowDrawer.DefaultDistance;
                    controller.aroundAngle = MainCameraRowDrawer.DefaultAroundAngle;
                    camera.fieldOfView = MainCameraRowDrawer.DefaultFov;
                }

                // メインカメラの構図へ合わせ直す
                if (_view.DrawButton("メインカメラへ合わせる", 160, ROW_HEIGHT))
                {
                    var gameMain = GameMain.Instance;
                    var mainCameraMain = gameMain != null ? gameMain.MainCamera : null;
                    var mainCamera = mainCameraMain != null ? mainCameraMain.camera : null;
                    if (mainCamera != null)
                    {
                        controller.targetPos = mainCameraMain.GetTargetPos();
                        controller.distance = mainCameraMain.GetDistance();
                        controller.aroundAngle = mainCameraMain.GetAroundAngle();
                        camera.fieldOfView = mainCamera.fieldOfView;
                    }
                }
            }
            _view.EndLayout();

            _view.EndScrollView();
        }

        /// <summary>
        /// リセットと SceneView 追従の行。
        /// リセットはエディット画面相当の初期構図へ戻す。
        /// CameraMain.Reset はフェードやマスクの再初期化まで走って画面が暗転するため使わず、
        /// 構図に関わる値だけを書き戻す
        /// </summary>
        private void DrawResetAndMatchSceneViewRow(CameraMain mainCamera, Camera camera)
        {
            _view.BeginHorizontal();
            {
                if (_view.DrawButton("リセット", 100, ROW_HEIGHT))
                {
                    MainCameraRowDrawer.RecordCameraEdit("リセット");
                    mainCamera.SetTargetPos(MainCameraRowDrawer.DefaultTargetPos);
                    mainCamera.SetDistance(MainCameraRowDrawer.DefaultDistance);
                    mainCamera.SetAroundAngle(MainCameraRowDrawer.DefaultAroundAngle);

                    var eulerAngles = camera.transform.eulerAngles;
                    eulerAngles.z = 0f;
                    camera.transform.eulerAngles = eulerAngles;

                    camera.fieldOfView = MainCameraRowDrawer.DefaultFov;
                }

                // SceneView が開かれていないと参照する構図が無いため押せない
                Camera sceneCamera;
                SceneViewCameraController controller;
                var canMatchSceneView = TryGetSceneViewCamera(out sceneCamera, out controller);

                if (_view.DrawButton("SceneViewカメラへ合わせる", 190, ROW_HEIGHT,
                    enabled: canMatchSceneView))
                {
                    MainCameraRowDrawer.RecordCameraEdit("SceneViewへ合わせる");
                    mainCamera.SetTargetPos(controller.targetPos);
                    mainCamera.SetDistance(controller.distance);
                    mainCamera.SetAroundAngle(controller.aroundAngle);

                    // SceneView カメラはロールを持たないため、傾いたままだと構図が一致しない
                    var eulerAngles = camera.transform.eulerAngles;
                    eulerAngles.z = 0f;
                    camera.transform.eulerAngles = eulerAngles;

                    camera.fieldOfView = sceneCamera.fieldOfView;
                }
            }
            _view.EndLayout();
        }

        /// <summary>選んだメイドの部位へ注視点を移すフォーカス行</summary>
        private void DrawFocusRow(CameraMain mainCamera)
        {
            _view.BeginHorizontal();
            {
                _view.DrawLabel("フォーカス", LABEL_WIDTH, ROW_HEIGHT);

                _focusMaidComboBox.items = MTEP.MaidManager.instance.maidCaches;
                _focusMaidComboBox.DrawButton(_view);
                _focusPointComboBox.DrawButton(_view);

                var maidCache = _focusMaidComboBox.currentItem;
                if (_view.DrawButton("移動", 60, ROW_HEIGHT, enabled: maidCache != null))
                {
                    var point = maidCache.GetPointTransform(_focusPointComboBox.currentItem);
                    if (point != null)
                    {
                        MainCameraRowDrawer.RecordCameraEdit("フォーカス");
                        mainCamera.SetTargetPos(point.position);
                    }
                }
            }
            _view.EndLayout();
        }

        /// <summary>
        /// サブカメラの管理タブ。台数の増減と選択したカメラの編集を行う。
        /// サブカメラはタイムライン文脈でのみ生成・更新されるため、
        /// タイムライン未読込時は使えない (SubCameraItemInspector と同じ制約)
        /// </summary>
        private void DrawSubCameraContent()
        {
            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            if (timelineManager.timeline == null)
            {
                _view.DrawLabel("タイムライン読込後に使用できます", -1, ROW_HEIGHT,
                    textColor: Color.yellow);
                return;
            }

            _view.SetEnabled(_view.focusedComboBox == null);

            DrawSubCameraCountRow();

            var subCameras = subCameraManager.subCameras;
            if (subCameras.Count == 0)
            {
                _view.DrawLabel("サブカメラが存在しません", -1, ROW_HEIGHT);
                return;
            }

            // 台数を減らすと選択が範囲外に残るため、末尾へ寄せ直す
            _subCameraComboBox.items = subCameras;
            _subCameraComboBox.currentIndex =
                Mathf.Clamp(_subCameraComboBox.currentIndex, 0, subCameras.Count - 1);
            _subCameraComboBox.DrawButton("操作対象", _view);

            var cameraData = _subCameraComboBox.currentItem;
            if (cameraData == null || cameraData.camera == null)
            {
                _view.DrawLabel("サブカメラを選択してください", -1, ROW_HEIGHT);
                return;
            }

            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            // 編集していない間はレイヤーが毎フレーム再生値を書き戻すため、
            // 編集モードでないときは触らせない (レイヤー UI と同じ制約)
            if (!studioHackManager.isPoseEditing)
            {
                _view.DrawLabel("編集モード中のみサブカメラを操作できます", -1, ROW_HEIGHT,
                    textColor: Color.yellow);
            }
            _view.SetEnabled(_view.focusedComboBox == null && studioHackManager.isPoseEditing);

            _subCameraRowDrawers.Get(cameraData.name)
                .Draw(_view, cameraData, LABEL_WIDTH, ROW_HEIGHT);

            _view.SetEnabled(_view.focusedComboBox == null);
            _view.EndScrollView();
        }

        /// <summary>サブカメラ台数の増減行</summary>
        private void DrawSubCameraCountRow()
        {
            CountRowDrawer.Draw(_view, "サブカメラ数", ROW_HEIGHT,
                subCameraManager.subCameras.Count,
                MTEP.SubCameraManager.MinSubCameraCount,
                MTEP.SubCameraManager.MaxSubCameraCount,
                x => subCameraManager.SetCameraCount(x));
        }
    }
}
