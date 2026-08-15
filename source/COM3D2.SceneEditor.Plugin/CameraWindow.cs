using System.Collections.Generic;
using System.Globalization;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// カメラの構図を数値/スライダーで確認・編集するウィンドウ。
    /// 操作対象は Main (CameraMain) と SceneView 用カメラから選べる。
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

        // リセット時の既定値 (CameraMain.Reset の Target カメラ初期値に合わせる)
        private static readonly Vector3 DefaultTargetPos = new Vector3(0f, 1.5f, 0f);
        private static readonly float DefaultDistance = 2f;
        private static readonly Vector2 DefaultAroundAngle = new Vector2(180f, 10f);
        private static readonly float DefaultFov = 35f;

        private static readonly string[] TargetNames = { "Main", "SceneView" };
        private static readonly int TargetButtonWidth = 90;

        // 座標行 (Inspector の座標行と同じ形式) のドラッグ感度
        private const float PositionDragSensitivity = 0.01f;

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

        /// <summary>操作対象。TargetNames の添字 (0: Main, 1: SceneView)</summary>
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
                // プリセットは Main カメラ専用のため SceneView タブでは行を出さない
                DrawPresetRow();
                DrawMainCameraContent();
            }
            else
            {
                DrawSceneViewCameraContent();
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

            RecordCameraEdit("プリセット " + slot);
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

            DrawTargetPosRow(mainCamera);
            _view.DrawHorizontalLine();
            DrawAngleSliders(mainCamera, camera);
            _view.DrawHorizontalLine();
            DrawDistanceFovSliders(mainCamera, camera);
            _view.DrawHorizontalLine();
            DrawResetRow(mainCamera, camera);

            _view.EndScrollView();
        }

        /// <summary>
        /// SceneView 用カメラの編集。Main と同じく注視点・距離・回転のオービットモデルで編集する
        /// (実体は SceneViewCameraController の公開 API)
        /// </summary>
        private void DrawSceneViewCameraContent()
        {
            var camera = SceneViewManager.instance.sceneCamera;
            var controller = SceneViewWindow.instance.cameraController;

            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            if (camera == null || controller == null || !SceneViewManager.instance.isActive)
            {
                _view.DrawLabel("SceneView が開かれていません", -1, ROW_HEIGHT,
                    textColor: Color.yellow);
                return;
            }

            _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            // リセット先はメインカメラの初期注視点と同じ座標 (SceneView 独自の値ではない)
            DrawVector3Row("注視点", PositionDragSensitivity, controller.targetPos,
                value => controller.targetPos = value,
                () => controller.targetPos = DefaultTargetPos);

            _view.DrawHorizontalLine();

            // 旋回で 0〜360 に丸まるため、表示は ±180 度へ正規化する
            var aroundAngle = controller.aroundAngle;
            var yaw = NormalizeAngle(aroundAngle.x);
            var pitch = NormalizeAngle(aroundAngle.y);

            DrawAxisSlider("ヨー", yaw, -180f, 180f, 0.1f,
                NormalizeAngle(DefaultAroundAngle.x),
                value => controller.aroundAngle = new Vector2(value, pitch));
            DrawAxisSlider("ピッチ", pitch, -90f, 90f, 0.1f,
                DefaultAroundAngle.y,
                value => controller.aroundAngle = new Vector2(yaw, value));

            _view.DrawHorizontalLine();

            DrawAxisSlider("距離", controller.distance, 0.1f, 30f, 0.01f,
                DefaultDistance, value => controller.distance = value);

            DrawAxisSlider("FOV", camera.fieldOfView, 1f, 179f, 0.1f,
                DefaultFov, value => camera.fieldOfView = value);

            _view.DrawHorizontalLine();

            _view.BeginHorizontal();
            {
                // Main のリセットと同じ構図 (注視点・距離・回転・FOV) へ戻す
                if (_view.DrawButton("リセット", 100, ROW_HEIGHT))
                {
                    controller.targetPos = DefaultTargetPos;
                    controller.distance = DefaultDistance;
                    controller.aroundAngle = DefaultAroundAngle;
                    camera.fieldOfView = DefaultFov;
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

        /// <summary>注視点のワールド座標。Inspector の座標行と同じ表示形式で編集する</summary>
        private void DrawTargetPosRow(CameraMain mainCamera)
        {
            DrawVector3Row("注視点", PositionDragSensitivity, mainCamera.GetTargetPos(),
                value =>
                {
                    RecordCameraEdit("注視点");
                    mainCamera.SetTargetPos(value);
                },
                () =>
                {
                    RecordCameraEdit("注視点");
                    mainCamera.SetTargetPos(DefaultTargetPos);
                });
        }

        /// <summary>メインカメラの操作を履歴へ記録する。SceneView カメラは対象にしない</summary>
        private static void RecordCameraEdit(string label)
        {
            HistoryManager.instance.BeforeEdit(null, HistoryScope.Camera, "カメラ: " + label);
        }

        /// <summary>ラベル + XYZ (ドラッグラベル + 数値入力) + リセットボタンの 1 行</summary>
        private void DrawVector3Row(
            string label,
            float dragSensitivity,
            Vector3 value,
            System.Action<Vector3> onChanged,
            System.Action onReset)
        {
            _view.DrawVector3Row(new GUIView.Vector3RowOption
            {
                label = label,
                labelWidth = LABEL_WIDTH,
                height = ROW_HEIGHT,
                dragSensitivity = dragSensitivity,
                value = value,
                onChanged = onChanged,
                onReset = onReset,
            });
        }

        /// <summary>
        /// 回転。GetAroundAngle は x がヨー (水平旋回)、y がピッチ (仰俯角)。
        /// ロールは UltimateOrbitCamera が管理しないため Transform へ直接書く
        /// </summary>
        private void DrawAngleSliders(CameraMain mainCamera, Camera camera)
        {
            var aroundAngle = mainCamera.GetAroundAngle();

            // 旋回中は値が際限なく積み上がるため、表示は ±180 度へ正規化する
            var yaw = NormalizeAngle(aroundAngle.x);
            var pitch = NormalizeAngle(aroundAngle.y);
            var roll = NormalizeAngle(camera.transform.eulerAngles.z);

            DrawAxisSlider("ヨー", yaw, -180f, 180f, 0.1f,
                NormalizeAngle(DefaultAroundAngle.x), value =>
                {
                    RecordCameraEdit("ヨー");
                    mainCamera.SetAroundAngle(new Vector2(value, pitch));
                });
            DrawAxisSlider("ピッチ", pitch, -90f, 90f, 0.1f,
                DefaultAroundAngle.y, value =>
                {
                    RecordCameraEdit("ピッチ");
                    mainCamera.SetAroundAngle(new Vector2(yaw, value));
                });
            DrawAxisSlider("ロール", roll, -180f, 180f, 0.1f, 0f, value =>
                {
                    RecordCameraEdit("ロール");
                    var eulerAngles = camera.transform.eulerAngles;
                    eulerAngles.z = value;
                    camera.transform.eulerAngles = eulerAngles;
                });
        }

        /// <summary>注視点からの距離と視野角</summary>
        private void DrawDistanceFovSliders(CameraMain mainCamera, Camera camera)
        {
            DrawAxisSlider("距離", mainCamera.GetDistance(), 0.1f, 30f, 0.01f,
                DefaultDistance, value =>
                {
                    RecordCameraEdit("距離");
                    mainCamera.SetDistance(value);
                });

            DrawAxisSlider("FOV", camera.fieldOfView, 1f, 179f, 0.1f,
                DefaultFov, value =>
                {
                    RecordCameraEdit("FOV");
                    camera.fieldOfView = value;
                });
        }

        /// <summary>
        /// エディット画面相当の初期構図へ戻す。
        /// CameraMain.Reset はフェードやマスクの再初期化まで走って画面が暗転するため使わず、
        /// 構図に関わる値だけを書き戻す
        /// </summary>
        private void DrawResetRow(CameraMain mainCamera, Camera camera)
        {
            if (_view.DrawButton("リセット", 100, ROW_HEIGHT))
            {
                RecordCameraEdit("リセット");
                mainCamera.SetTargetPos(DefaultTargetPos);
                mainCamera.SetDistance(DefaultDistance);
                mainCamera.SetAroundAngle(DefaultAroundAngle);

                var eulerAngles = camera.transform.eulerAngles;
                eulerAngles.z = 0f;
                camera.transform.eulerAngles = eulerAngles;

                camera.fieldOfView = DefaultFov;
            }
        }

        /// <summary>共通書式のスライダー 1 行</summary>
        private void DrawAxisSlider(
            string label, float value, float min, float max, float step,
            float defaultValue, System.Action<float> onChanged)
        {
            _view.DrawSliderValue(new GUIView.SliderOption
            {
                label = label,
                labelWidth = LABEL_WIDTH,
                width = -1,
                min = min,
                max = max,
                step = step,
                defaultValue = defaultValue,
                value = value,
                onChanged = onChanged,
            });
        }

        /// <summary>角度を (-180, 180] へ正規化する</summary>
        private static float NormalizeAngle(float angle)
        {
            angle = Mathf.Repeat(angle, 360f);
            return angle > 180f ? angle - 360f : angle;
        }
    }
}
