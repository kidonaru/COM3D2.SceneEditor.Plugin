using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// SceneView ウィンドウ。専用カメラの RT をレターボックスなしで表示し、
    /// UnityEditor 風のカメラ操作・クリック選択・ギズモ操作の入力を受け付ける。
    ///
    /// 注意: シーン遷移時に SceneViewManager がカメラ GO を即時破棄するため、
    /// _cameraController は破棄済み Transform を掴んだままになりうる。
    /// 参照する箇所では null チェックに加えて sceneViewManager.isActive を必ず確認すること
    /// </summary>
    public class SceneViewWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903350;
        // RenderTexture の最小サイズ (0 サイズの RT は作れない)
        private const int MIN_RT_SIZE = 64;

        // ツールバーの寸法。アイコントグルは正方形、テキスト幅はアイコン読み込み失敗時のフォールバック用
        public static readonly int TOOLBAR_HEIGHT = 24;
        public static readonly int TOOLBAR_ITEM_HEIGHT = 20;
        public static readonly int TOOLBAR_TOGGLE_WIDTH = 72;
        public static readonly int TOOLBAR_ITEM_MARGIN = 2;
        // アイコンをボタン枠より少し小さく描くための余白 (両側合計)
        private const float TOOLBAR_ICON_OFFSET = 4f;
        // シーン描画に重ねる帯の色
        private static readonly Color TOOLBAR_BG_COLOR = new Color(0f, 0f, 0f, 0.5f);
        // カメラ角度プリセットのボタン幅
        private static readonly int VIEW_PRESET_BUTTON_WIDTH = 24;

        private static SceneViewManager sceneViewManager => SceneViewManager.instance;
        private static SelectionManager selectionManager => SelectionManager.instance;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "SceneView";
        // 描画ビューを持つ点で GameView と同じ性格のため最小サイズも揃える
        protected override int minWidth => GameViewWindow.MIN_WIDTH;
        protected override int minHeight => GameViewWindow.MIN_HEIGHT;

        /// <summary>ビューポート全域がカメラ操作のため空き領域ドラッグは無効</summary>
        protected override bool allowContentDrag => false;

        /// <summary>ツールバーの描画用ビュー。項目を横並びのフローレイアウトで置く</summary>
        private readonly GUIView _toolbarView = new GUIView
        {
            padding = new Vector2(FRAME, (TOOLBAR_HEIGHT - TOOLBAR_ITEM_HEIGHT) * 0.5f),
            margin = TOOLBAR_ITEM_MARGIN,
        };

        private SceneViewCameraController _cameraController = null;
        private bool _dragging = false;

        /// <summary>SceneView カメラの操作状態。CameraWindow からの数値編集にも使う (非表示中は null)</summary>
        public SceneViewCameraController cameraController => _cameraController;

        /// <summary>SceneView で掴んでいるドラッグ点。IK に限らず顔・上体・骨盤も対象</summary>
        private IMaidDragPoint _draggingPoint = null;
        // ツールバーの帯 (ウィンドウローカル座標)。シーンへの入力を除外する範囲でもある
        private Rect _toolbarLocalRect = Rect.zero;
        // 右上のカメラ角度プリセット帯 (ウィンドウローカル座標)。こちらもシーン入力の除外範囲
        private Rect _toolbarRightLocalRect = Rect.zero;

        /// <summary>右上ツールバーの描画用ビュー。左のツールバーとは独立したフローレイアウト</summary>
        private readonly GUIView _toolbarRightView = new GUIView
        {
            padding = new Vector2(FRAME, (TOOLBAR_HEIGHT - TOOLBAR_ITEM_HEIGHT) * 0.5f),
            margin = TOOLBAR_ITEM_MARGIN,
        };

        private static SceneViewWindow _instance = null;
        public static SceneViewWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new SceneViewWindow();
                }
                return _instance;
            }
        }

        private SceneViewWindow()
        {
        }

        /// <summary>RT の描画領域 (スクリーンGUI座標)。レターボックスなしの全面</summary>
        public Rect drawRect => contentRect;

        // RT のサイズ。内容領域を RT の最小サイズでクランプしたもの
        public int rtPixelWidth => Mathf.Max(contentPixelWidth, MIN_RT_SIZE);
        public int rtPixelHeight => Mathf.Max(contentPixelHeight, MIN_RT_SIZE);

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.sceneViewPosX;
            y = config.sceneViewPosY;
            width = config.sceneViewWidth;
            height = config.sceneViewHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.sceneViewPosX = x;
            config.sceneViewPosY = y;
            config.sceneViewWidth = width;
            config.sceneViewHeight = height;
        }

        public override bool savedVisible
        {
            get => config.sceneViewVisible;
            set => config.sceneViewVisible = value;
        }

        protected override void OnShowChanged(bool visible)
        {
            if (visible)
            {
                ActivateSceneView();
            }
            else
            {
                DeactivateSceneView();
            }
        }

        /// <summary>非アクティブタブの間は専用カメラと RT を止めて描画コストを抑える</summary>
        protected override void OnTabVisibleChanged(bool visible)
        {
            if (!visible)
            {
                DeactivateSceneView();
            }
            // 表示に戻ったときの再アクティブ化は Update() の
            // 「sceneViewManager.isActive でなければ ActivateSceneView」で行われる
        }

        /// <summary>カメラと RT を止め、カメラ操作の状態も捨てる</summary>
        private void DeactivateSceneView()
        {
            // 非表示中は Update が止まり、SceneView 発のドラッグを終える経路が無くなる
            // (ゲーム画面側のマウスメッセージは掴んだカメラが違うため弾かれる)。
            // 畳まないと IK が掴んだ位置を解き続け、四肢が固定されたまま戻らなくなる
            if (_draggingPoint != null)
            {
                _draggingPoint.CancelDrag();
            }
            ClearDragTracking();

            sceneViewManager.Deactivate();
            _cameraController = null;
            _dragging = false;
        }

        /// <summary>カメラと RT を用意し、カメラ操作を紐付ける</summary>
        private void ActivateSceneView()
        {
            sceneViewManager.Activate(rtPixelWidth, rtPixelHeight);

            var camera = sceneViewManager.sceneCamera;
            if (camera == null)
            {
                MTEUtils.LogError("SceneView カメラを作成できませんでした");
                return;
            }

            _cameraController = new SceneViewCameraController(camera.transform);
        }

        protected override void OnResizeEnd()
        {
            if (sceneViewManager.isActive)
            {
                sceneViewManager.ResizeRenderTexture(rtPixelWidth, rtPixelHeight);
            }
        }

        /// <summary>
        /// 対象の全 Renderer が収まる位置までカメラを寄せる (Hierarchy ダブルクリック等の外部起点用)。
        /// SceneView 非表示・カメラ未生成時は何もしない
        /// </summary>
        public void FocusOn(GameObject go)
        {
            if (go == null || _cameraController == null || !sceneViewManager.isActive)
            {
                return;
            }
            _cameraController.Focus(PluginUtils.CalcObjectBounds(go));
        }

        /// <summary>GUI座標が SceneView の 3D シーン領域上か (ツールバー・リサイズつかみ・他窓は除外)</summary>
        public bool IsSceneViewActiveAt(Vector2 guiPos)
        {
            // ツールバーはシーンに重なっているため、その上ではシーンへの入力を無効にする
            var toolbarRect = new Rect(
                windowRect.x + _toolbarLocalRect.x,
                windowRect.y + _toolbarLocalRect.y,
                _toolbarLocalRect.width,
                _toolbarLocalRect.height);

            var toolbarRightRect = new Rect(
                windowRect.x + _toolbarRightLocalRect.x,
                windowRect.y + _toolbarRightLocalRect.y,
                _toolbarRightLocalRect.width,
                _toolbarRightLocalRect.height);

            var rect = drawRect;
            return isWndVisible &&
                rect.width > 0f && rect.height > 0f &&
                rect.Contains(guiPos) &&
                !toolbarRect.Contains(guiPos) &&
                !toolbarRightRect.Contains(guiPos) &&
                !IsOverResizeHandle(guiPos) &&
                !GuiWindowTracker.IsOverWindowExcept(WINDOW_ID, guiPos);
        }

        /// <summary>GUI座標を RT ピクセル座標 (左下原点) へ変換する</summary>
        public Vector2 GuiToRtPoint(Vector2 guiPos)
        {
            var rt = sceneViewManager.renderTexture;
            var rect = drawRect;
            if (rt == null || rect.width <= 0f || rect.height <= 0f)
            {
                return Vector2.zero;
            }

            return new Vector2(
                (guiPos.x - rect.x) * rt.width / rect.width,
                rt.height - (guiPos.y - rect.y) * rt.height / rect.height);
        }

        protected override void DrawContent()
        {
            var rt = sceneViewManager.renderTexture;
            if (rt != null)
            {
                GUI.DrawTexture(ToLocalRect(drawRect), rt, ScaleMode.StretchToFill, false);
            }
        }

        /// <summary>背景/メイド/ギズモ表示とパースのトグル列。シーン描画に重ねて表示する</summary>
        protected override void DrawToolbar()
        {
            var bgIcon = ToolbarIcons.GetTexture(ToolbarIcons.Kind.Bg);
            var maidIcon = ToolbarIcons.GetTexture(ToolbarIcons.Kind.Maid);
            var gizmoIcon = ToolbarIcons.GetTexture(ToolbarIcons.Kind.Gizmo);
            var orthoIcon = ToolbarIcons.GetTexture(ToolbarIcons.Kind.Ortho);

            // 帯の幅を先に求め、半透明の背景を敷いてからボタンを描く。マージンは項目間の 3 箇所分
            var totalWidth = FRAME * 2 + TOOLBAR_ITEM_MARGIN * 3 +
                GetToolbarToggleWidth(bgIcon) + GetToolbarToggleWidth(maidIcon) +
                GetToolbarToggleWidth(gizmoIcon) + GetToolbarToggleWidth(orthoIcon);
            _toolbarLocalRect = new Rect(0, HEADER_HEIGHT, totalWidth, TOOLBAR_HEIGHT);

            var prevColor = GUI.color;
            GUI.color = TOOLBAR_BG_COLOR;
            GUI.DrawTexture(_toolbarLocalRect, Texture2D.whiteTexture);
            GUI.color = prevColor;

            var view = _toolbarView;
            view.Init(0, HEADER_HEIGHT, windowRect.width, TOOLBAR_HEIGHT);
            view.BeginHorizontal();

            DrawToolbarToggle(view, bgIcon, "背景", config.sceneViewShowBg,
                value => config.sceneViewShowBg = value);
            DrawToolbarToggle(view, maidIcon, "メイド", config.sceneViewShowMaid,
                value => config.sceneViewShowMaid = value);
            DrawToolbarToggle(view, gizmoIcon, "ギズモ", config.sceneViewShowGizmo,
                value => config.sceneViewShowGizmo = value);
            DrawToolbarToggle(view, orthoIcon, "平行投影", config.sceneViewOrthographic,
                value => config.sceneViewOrthographic = value);

            view.EndLayout();

            DrawViewPresetToolbar();
        }

        /// <summary>
        /// 右上のカメラ角度プリセット列。各軸の + 方向から注視点を見る構図へ切り替える
        /// (注視点と距離は維持し、角度だけを変更する)
        /// </summary>
        private void DrawViewPresetToolbar()
        {
            // 帯の幅: ボタン 3 個 + 項目間マージン 2 箇所
            var totalWidth = FRAME * 2 + TOOLBAR_ITEM_MARGIN * 2 + VIEW_PRESET_BUTTON_WIDTH * 3;
            _toolbarRightLocalRect = new Rect(
                windowRect.width - totalWidth, HEADER_HEIGHT, totalWidth, TOOLBAR_HEIGHT);

            var prevColor = GUI.color;
            GUI.color = TOOLBAR_BG_COLOR;
            GUI.DrawTexture(_toolbarRightLocalRect, Texture2D.whiteTexture);
            GUI.color = prevColor;

            var view = _toolbarRightView;
            view.Init(_toolbarRightLocalRect.x, HEADER_HEIGHT, totalWidth, TOOLBAR_HEIGHT);
            view.BeginHorizontal();

            // +X 方向から: カメラ前方が -X (ヨー 270 度)
            DrawViewPresetButton(view, "X", 270f);
            // +Y 方向から (真上): ヨーは現在値を維持して真下を向く
            DrawViewPresetButton(view, "Y", null);
            // +Z 方向から: カメラ前方が -Z (ヨー 180 度)
            DrawViewPresetButton(view, "Z", 180f);

            view.EndLayout();
        }

        /// <summary>
        /// 角度プリセットボタン 1 個。yaw が null なら真上 (ヨー維持) 視点。
        /// 既にその構図なら反対側の軸から見る構図へ切り替える (連打で正負を往復する)
        /// </summary>
        private void DrawViewPresetButton(GUIView view, string label, float? yaw)
        {
            if (!view.DrawButton(label, VIEW_PRESET_BUTTON_WIDTH, TOOLBAR_ITEM_HEIGHT))
            {
                return;
            }
            if (_cameraController == null || !sceneViewManager.isActive)
            {
                return;
            }

            var current = _cameraController.aroundAngle;
            // 真上視点はヨーを維持したままピッチだけを反転させる
            var positiveSideView = yaw.HasValue
                ? new Vector2(yaw.Value, 0f)
                : new Vector2(current.x, 90f);
            var negativeSideView = yaw.HasValue
                ? new Vector2((yaw.Value + 180f) % 360f, 0f)
                : new Vector2(current.x, -90f);

            _cameraController.aroundAngle = IsSameAngle(current, positiveSideView)
                ? negativeSideView
                : positiveSideView;
        }

        /// <summary>ヨー・ピッチが実質同じ向きか (360 度をまたぐ表現差を吸収して比較する)</summary>
        private static bool IsSameAngle(Vector2 a, Vector2 b)
        {
            const float tolerance = 0.5f;
            return Mathf.Abs(Mathf.DeltaAngle(a.x, b.x)) < tolerance
                && Mathf.Abs(Mathf.DeltaAngle(a.y, b.y)) < tolerance;
        }

        /// <summary>アイコンなら正方形、テキストフォールバックなら固定幅</summary>
        private static float GetToolbarToggleWidth(Texture2D icon)
        {
            return icon != null ? TOOLBAR_ITEM_HEIGHT : TOOLBAR_TOGGLE_WIDTH;
        }

        /// <summary>変更されたときだけ config を更新し、カメラ・フィルタへ反映する</summary>
        private void DrawToolbarToggle(
            GUIView view, Texture2D icon, string label, bool value, Action<bool> setValue)
        {
            Action<bool> onChanged = newValue =>
            {
                setValue(newValue);
                config.dirty = true;
                sceneViewManager.ApplyViewSettings();
            };

            // アイコンを読み込めなかったときはテキストトグルにフォールバックする
            if (icon != null)
            {
                view.DrawToggle(icon, value, TOOLBAR_ITEM_HEIGHT, TOOLBAR_ITEM_HEIGHT,
                    onChanged, TOOLBAR_ICON_OFFSET);
            }
            else
            {
                view.DrawToggle(label, value, TOOLBAR_TOGGLE_WIDTH, TOOLBAR_ITEM_HEIGHT, onChanged);
            }
        }

        public override void Update()
        {
            base.Update();

            if (!isWndVisible)
            {
                // 一時非表示にした瞬間もドラッグを打ち切る。
                // ドラッグ継続中は領域外でも操作を維持する作りのため、
                // ここで落とさないとボタンを離すまでカメラが回り続ける
                _dragging = false;
                return;
            }

            // シーン遷移等でカメラが破棄されていたら作り直す
            if (!sceneViewManager.isActive)
            {
                ActivateSceneView();
            }

            var guiPos = InputRemapper.rawGuiPosition;
            if (!isResizing)
            {
                UpdatePointerInput(guiPos);
            }
            // 入力の有無に関わらず毎フレーム呼び、慣性・イージングを進める
            if (_cameraController != null && sceneViewManager.isActive)
            {
                _cameraController.UpdateTransform();
            }
            UpdateOrthographicSize();
        }

        /// <summary>ギズモドラッグ → クリック選択 → カメラ操作の優先順位で入力を処理する</summary>
        private void UpdatePointerInput(Vector2 guiPos)
        {
            var gizmo = sceneViewManager.isActive ? sceneViewManager.gizmoRenderer : null;
            var camera = sceneViewManager.isActive ? sceneViewManager.sceneCamera : null;

            // 1. ギズモドラッグが最優先 (継続中は領域外へ出ても維持する)
            if (gizmo != null && gizmo.isDragging)
            {
                if (Input.GetMouseButton(0))
                {
                    gizmo.UpdateDrag(GuiToRtPoint(guiPos));
                }
                else
                {
                    gizmo.EndDrag();
                }
            }
            // 2. 外部ギズモのドラッグ継続 (この SceneView で始まったものだけ)
            else if (GizmoHost.IsExternalDragging(camera))
            {
                if (Input.GetMouseButton(0))
                {
                    GizmoHost.UpdateExternalDrag(camera, GuiToRtPoint(guiPos));
                }
                else
                {
                    GizmoHost.EndExternalDrag();
                }
            }
            // 2.5 ドラッグ点の継続 (ギズモと同じく領域外へ出ても維持する)
            else if (_draggingPoint != null)
            {
                var rtPoint = GuiToRtPoint(guiPos);
                if (Input.GetMouseButton(0))
                {
                    _draggingPoint.UpdateDrag(rtPoint);
                }
                else
                {
                    _draggingPoint.EndDrag(rtPoint);
                    ClearDragTracking();
                }
            }
            // 3. 左クリック開始: 自前ギズモ → 外部ギズモ → ボーンピック → 選択の順に試す。
            //    ギズモ類はハンドルの明示 UI なのでシーン内容 (関節・オブジェクト) より優先する
            //    (Alt 押下中はオービット操作)
            else if (Input.GetMouseButtonDown(0) &&
                !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt) &&
                sceneViewManager.isActive && IsSceneViewActiveAt(guiPos))
            {
                var rtPoint = GuiToRtPoint(guiPos);
                if ((gizmo == null || !gizmo.TryBeginDrag(rtPoint)) &&
                    !GizmoHost.TryBeginExternalDrag(camera, rtPoint))
                {
                    // ドラッグ点 (IK・顔・上体・骨盤) のタップはボーンピック・通常選択より優先する。
                    // SceneView は Unity のマウスメッセージが届かないため、ここで直接掴む。
                    // 素通しだけだと SelectAtRay がメイド選択に化けて IK 選択を消してしまう
                    var dragPoint = selectionManager.FindDragPointAtRay(camera, rtPoint);
                    if (dragPoint != null)
                    {
                        if (dragPoint.BeginDrag(camera, rtPoint))
                        {
                            _draggingPoint = dragPoint;
                            MaidDragPointRing.SetScenePressed(dragPoint.gameObject);
                        }
                        else
                        {
                            // 掴めない状態でも IK 点は選択だけ切り替える
                            // (他の点は離した時点で自分で選択するため何もしない)
                            var ikPoint = dragPoint as MaidIKDragPoint;
                            if (ikPoint != null)
                            {
                                selectionManager.SelectIK(ikPoint);
                            }
                        }
                    }
                    // ボーン編集モード中は関節クリックを通常のオブジェクト選択より優先する
                    else if (sceneViewManager.boneLineRenderer == null ||
                        !sceneViewManager.boneLineRenderer.TryPickBone(rtPoint))
                    {
                        selectionManager.SelectAtRay(camera, rtPoint);
                    }
                }
            }

            var isGizmoDragging = (gizmo != null && gizmo.isDragging) ||
                GizmoHost.IsExternalDragging(camera);
            UpdateDragPointHover(guiPos, camera, isGizmoDragging);

            // 4. カメラ操作 (このビューの自前・外部ギズモ・ドラッグ点操作中は抑止。
            //    別ビューで始まった外部ドラッグはこちらのカメラ操作を止めない)
            if (!isGizmoDragging && _draggingPoint == null)
            {
                UpdateCameraInput(guiPos);
            }
        }

        /// <summary>直前にホバー判定を行ったカーソル位置。動いていない間はレイを飛ばさない</summary>
        private Vector2 _lastHoverGuiPos = new Vector2(float.NaN, float.NaN);

        /// <summary>
        /// SceneView 上のドラッグ点のホバー表示を更新する。
        /// Unity のマウスメッセージが届かないため毎フレームのレイ判定で代替するが、
        /// FindDragPointAtRay は Physics.RaycastAll でヒット数分を確保するため、
        /// ドラッグ点が居るボーン表示中・カーソルが動いたときだけに絞る
        /// </summary>
        private void UpdateDragPointHover(Vector2 guiPos, Camera camera, bool isGizmoDragging)
        {
            // 掴んでいる間はその点を強調したままにする。ギズモ操作中もホバーを探す意味がない
            if (_draggingPoint != null || isGizmoDragging)
            {
                return;
            }

            if (camera == null || !IsSceneViewActiveAt(guiPos) ||
                !MaidManipulateManager.instance.isBoneVisible)
            {
                MaidDragPointRing.SetSceneHovered(null);
                // 次に領域へ入ったら位置が同じでも引き直す
                _lastHoverGuiPos = new Vector2(float.NaN, float.NaN);
                return;
            }

            if (guiPos == _lastHoverGuiPos)
            {
                return;
            }
            _lastHoverGuiPos = guiPos;

            var point = selectionManager.FindDragPointAtRay(camera, GuiToRtPoint(guiPos));
            MaidDragPointRing.SetSceneHovered(point != null ? point.gameObject : null);
        }

        /// <summary>
        /// SceneView 側の掴み追跡と強調表示を戻す。
        /// ドラッグ点自身の終了処理 (EndDrag / CancelDrag) とは役割を分けている
        /// </summary>
        private void ClearDragTracking()
        {
            _draggingPoint = null;
            MaidDragPointRing.SetScenePressed(null);
            MaidDragPointRing.SetSceneHovered(null);
            _lastHoverGuiPos = new Vector2(float.NaN, float.NaN);
        }

        private void UpdateCameraInput(Vector2 guiPos)
        {
            if (_cameraController == null || !sceneViewManager.isActive)
            {
                return;
            }

            // ドラッグ開始はシーン領域内のみ。継続中は領域外に出ても操作を維持する
            var isActiveAt = IsSceneViewActiveAt(guiPos);
            if (!_dragging && !isActiveAt)
            {
                return;
            }

            // ゲーム内カメラと同じく Input.GetAxis ベースで入力する
            var mouseAxis = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));

            if (Input.GetMouseButton(1))
            {
                _dragging = true;
                _cameraController.Rotate(mouseAxis);
                UpdateFlyThrough();
            }
            else if (Input.GetMouseButton(0) &&
                (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)))
            {
                _dragging = true;
                _cameraController.Rotate(mouseAxis);
            }
            else if (Input.GetMouseButton(2))
            {
                _dragging = true;
                _cameraController.Pan(mouseAxis);
            }
            else
            {
                _dragging = false;
            }

            var scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f && isActiveAt)
            {
                _cameraController.Zoom(scroll);
            }

            // F: 選択対象へフォーカス
            if (isActiveAt && Input.GetKeyDown(KeyCode.F))
            {
                FocusOn(selectionManager.selectedObject);
            }
        }

        /// <summary>右ボタン押下中の WASD/QE フライスルー</summary>
        private void UpdateFlyThrough()
        {
            var dir = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) dir += Vector3.forward;
            if (Input.GetKey(KeyCode.S)) dir += Vector3.back;
            if (Input.GetKey(KeyCode.A)) dir += Vector3.left;
            if (Input.GetKey(KeyCode.D)) dir += Vector3.right;
            if (Input.GetKey(KeyCode.E)) dir += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) dir += Vector3.down;

            _cameraController.Fly(dir, Time.deltaTime,
                Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        }

        /// <summary>ortho 中はピボット距離から表示範囲を毎フレーム同期し、ホイールズームを効かせる</summary>
        private void UpdateOrthographicSize()
        {
            var camera = sceneViewManager.sceneCamera;
            if (!sceneViewManager.isActive || camera == null ||
                !camera.orthographic || _cameraController == null)
            {
                return;
            }

            camera.orthographicSize = _cameraController.pivotDistance *
                Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }
    }
}
