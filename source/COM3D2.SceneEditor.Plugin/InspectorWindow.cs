using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 選択対象を表示・編集するウィンドウ。選択種別で内容を出し分け、
    /// ボーン選択中は回転オフセットのスライダー、メイド選択中は切替とカメラ追従の行を出す。
    /// 通常オブジェクトは Transform を表示・編集する。
    /// X/Y/Z ラベルの左右ドラッグで値を増減でき (Shift で 0.1 倍)、数値入力も併用できる。
    /// ギズモ操作による変化は毎フレーム反映される
    /// </summary>
    public class InspectorWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903352;

        private const float LabelWidth = 40f;
        private const float RowHeight = 20f;

        // ヘッダー行のアクティブトグルとフォーカスボタンの幅 (どちらも正方形)
        private const float HeaderToggleWidth = 20f;
        private const float HeaderFocusButtonWidth = 20f;

        // 1px ドラッグあたりの増減量
        private const float PositionSensitivity = 0.01f;
        private const float RotationSensitivity = 1f;
        private const float ScaleSensitivity = 0.01f;

        // 拡縮連動で比率の分母に使えない「実質 0」とみなす閾値 (丸め誤差の許容)
        private const float ScaleZeroEpsilon = 1e-6f;

        private static SelectionManager selectionManager => SelectionManager.instance;
        private static MaidManipulateManager maidManager => MaidManipulateManager.instance;
        private static BoneEditManager boneEditManager => BoneEditManager.instance;

        /// <summary>スロットボーンの汎用回転スライダー軸ラベル (ローカル X/Y/Z)</summary>
        private static readonly string[] SlotBoneAxisLabels = { "X回転", "Y回転", "Z回転" };

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "Inspector";

        /// <summary>コンボのポップアップ描画用。ウィンドウ原点基準で描くため内容ビューと分ける</summary>
        private readonly GUIView _rootView = new GUIView();

        private readonly GUIView _view = new GUIView();

        private readonly GUIComboBox<BoneSliderDef> _boneComboBox = new GUIComboBox<BoneSliderDef>
        {
            getName = (def, _) => def.displayName,
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        private readonly GUIComboBox<Maid> _maidComboBox = new GUIComboBox<Maid>
        {
            getName = (maid, _) => maid == null ? "なし" : maid.status.fullNameJpStyle,
            buttonSize = new Vector2(200, 20),
            contentSize = new Vector2(200, 300),
        };

        // 回転はオイラー角をキャッシュして編集する。Transform から毎フレーム読み直すと
        // quaternion との変換で 180 度付近の表現が入れ替わり、ドラッグ中に値が飛ぶため
        private Vector3 _eulerAngles = Vector3.zero;
        private Quaternion _cachedRotation = Quaternion.identity;
        private GameObject _cachedTarget = null;
        private bool _cachedUseLocal = true;

        private static InspectorWindow _instance = null;
        public static InspectorWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new InspectorWindow();
                }
                return _instance;
            }
        }

        private InspectorWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.inspectorPosX;
            y = config.inspectorPosY;
            width = config.inspectorWidth;
            height = config.inspectorHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.inspectorPosX = x;
            config.inspectorPosY = y;
            config.inspectorWidth = width;
            config.inspectorHeight = height;
        }

        public override bool savedVisible
        {
            get => config.inspectorVisible;
            set => config.inspectorVisible = value;
        }

        public override void Update()
        {
            base.Update();

            // 委譲先がコンボのドロップダウンを自前で出せるよう、
            // 非表示のフレームも含めてウィンドウ状態を公開し続ける
            InspectorHost.UpdateWindowState(windowRect, isWndVisible);
        }

        protected override void DrawContent()
        {
            _rootView.Init(new Rect(0f, 0f, windowRect.width, windowRect.height));
            // 内容ビューを子にして、どちらに描いたコンボもフォーカス状態を共有させる
            _view.parent = _rootView;
            _view.Init(ToLocalRect(contentRect));

            var go = selectionManager.selectedObject;

            _view.DrawHorizontalLine(Color.gray);
            _view.AddSpace(5);

            if (selectionManager.hasIKSelection)
            {
                _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);
                DrawIKContent();
                _view.EndScrollView();
            }
            else if (selectionManager.hasBoneSelection)
            {
                _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);
                DrawBoneContent();
                _view.EndScrollView();
            }
            else if (boneEditManager.editMode && boneEditManager.selectedBone != null)
            {
                _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);
                DrawSlotBoneContent();
                _view.EndScrollView();
            }
            else if (go == null)
            {
                _view.DrawLabel("オブジェクトが選択されていません", -1, RowHeight);
            }
            else
            {
                // 外部プラグイン管理下のオブジェクトは内容描画を丸ごと委譲する
                // (スクロールも委譲先が必要に応じて自前で行う)
                if (InspectorHost.TryDraw(go, _view.viewRect))
                {
                    ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this);
                    return;
                }

                _view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

                var maid = go.GetComponent<Maid>();
                if (maid != null)
                {
                    DrawMaidContent(maid);
                }

                DrawHeader(go);
                DrawGizmoToolRow();

                var t = go.transform;
                // ギズモの Local/Global 切替に合わせて表示・編集する座標系も切り替える
                var useLocal = GizmoRenderer.useLocalSpace;
                SyncEulerCache(go, useLocal);

                DrawVector3Row("位置", PositionSensitivity,
                    useLocal ? t.localPosition : t.position,
                    value =>
                    {
                        RecordObjectEdit(go);
                        SetPosition(t, value, useLocal);
                    },
                    () =>
                    {
                        RecordObjectEdit(go);
                        SetPosition(t, Vector3.zero, useLocal);
                    });

                DrawVector3Row("回転", RotationSensitivity, _eulerAngles,
                    value =>
                    {
                        RecordObjectEdit(go);
                        ApplyEulerAngles(t, value, useLocal);
                    },
                    () =>
                    {
                        RecordObjectEdit(go);
                        ApplyEulerAngles(t, Vector3.zero, useLocal);
                    });

                // ワールドスケールは Transform に書き戻せないため拡縮は常にローカル
                DrawScaleRow(go, t);

                // PNG 配置は Transform に続けて固有パラメータも編集させる
                PngPlacementInspector.Draw(_view, go);

                _view.EndScrollView();
            }

            // ボタン押下で _rootView に登録されたフォーカスをポップアップへ引き渡す
            // (MaidWindowBase と同じ流儀)
            ComboBoxPopupWindow.instance.ProcessFocus(_rootView, this);
        }

        /// <summary>
        /// IK 選択時の専用表示。IK 座標（ワールド位置）を数値・ドラッグで編集できる。
        /// 値は毎フレーム対象ボーンから読むため、ドラッグ点の操作にも追従する
        /// </summary>
        private void DrawIKContent()
        {
            var point = selectionManager.selectedIKPoint;
            var maid = point.maid;

            // 退避中は表示に戻す際に上書きされるため操作させない (DrawBoneContent と同じ理由)
            if (!maidManager.IsVisible(maid))
            {
                _view.DrawLabel("非表示中はポーズを操作できません", -1, RowHeight,
                    textColor: Color.yellow);
                return;
            }

            _view.DrawLabel("IK: " + point.displayName, -1, RowHeight);

            // 衣装変更等でチェーンや対象ボーンが失われた場合は編集させない
            if (!point.canEdit)
            {
                _view.DrawLabel("この IK は操作できません", -1, RowHeight, textColor: Color.yellow);
                return;
            }

            DrawVector3Row("位置", PositionSensitivity, point.targetPosition,
                value => point.ApplyTargetPosition(value),
                null);

            DrawIKHoldToggle(point, maid);
            DrawMuneYureToggle(point, maid);
        }

        /// <summary>
        /// 選択中の IK 点に対応する IK 固定トグル。
        /// 肩・胸には対応する固定タイプが無いため何も出さない
        /// </summary>
        private void DrawIKHoldToggle(MaidIKDragPoint point, Maid maid)
        {
            MaidIKHoldType holdType;
            if (!MaidIKHoldController.TryGetHoldType(point.followBone.name, out holdType))
            {
                return;
            }

            var holdController = maidManager.ikHoldController;

            _view.BeginHorizontal();
            {
                _view.DrawToggle("IK固定", holdController.GetHold(maid, holdType),
                    100, RowHeight,
                    on =>
                    {
                        HistoryManager.instance.BeforeEdit(maid, HistoryScope.IK,
                            "IK固定: " + MaidIKHoldController.GetHoldTypeName(holdType));
                        holdController.SetHold(maid, holdType, on);
                    });

                // IK 固定はボーン編集（編集モード）中しか効かないため、モード外なら注意を出す
                // (MaidIKWindow と同じ案内)
                if (!maidManager.isEditMode)
                {
                    _view.DrawLabel("※編集モードで有効", -1, RowHeight, textColor: Color.yellow);
                }
            }
            _view.EndLayout();
        }

        /// <summary>
        /// 胸の IK 点を選択しているときの揺れもの ON/OFF。
        /// 胸を手付けすると自動で OFF になるため、戻すための唯一の導線になる
        /// </summary>
        private void DrawMuneYureToggle(MaidIKDragPoint point, Maid maid)
        {
            if (!point.isMune)
            {
                return;
            }

            var controller = maidManager.muneYureController;
            var isLeft = point.isMuneLeft;

            _view.DrawToggle("胸を揺らす", controller.GetYure(maid, isLeft), 100, RowHeight,
                on =>
                {
                    HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                        on ? "胸を揺らす" : "胸の揺れを止める");
                    controller.SetYure(maid, isLeft, on);
                });
        }

        /// <summary>
        /// ボーン選択時の専用表示。ボーンドロップダウン + 軸オフセットスライダー。
        /// Transform 行は出さない（ボーン操作は停止ポーズ基準のオフセット角で行う）
        /// </summary>
        private void DrawBoneContent()
        {
            var maid = selectionManager.selectedBoneMaid;

            // 退避中は表示に戻す際に上書きされるため操作させない
            if (!maidManager.IsVisible(maid))
            {
                _view.DrawLabel("非表示中はポーズを操作できません", -1, RowHeight,
                    textColor: Color.yellow);
                return;
            }

            // ギズモ・ドラッグ点で掴んでいるボーンがあれば選択を自動で追従させる
            var grabbedDef = MaidBoneSliderController.FindDef(
                maidManager.boneGizmoController.grabbedBoneName
                ?? MaidDragBoneTracker.draggingBoneName);
            if (grabbedDef != null && grabbedDef != selectionManager.selectedBoneDef)
            {
                selectionManager.SelectBone(maid, grabbedDef);
            }

            var selectedDef = selectionManager.selectedBoneDef;

            _view.BeginHorizontal();
            {
                _view.DrawLabel("ボーン", LabelWidth, RowHeight);

                _boneComboBox.items = MaidBoneSliderController.allDefs;
                _boneComboBox.currentIndex = MaidBoneSliderController.allDefs.IndexOf(selectedDef);
                _boneComboBox.onSelected = (def, _) => selectionManager.SelectBone(maid, def);
                _boneComboBox.DrawButton(_view);
            }
            _view.EndLayout();

            DrawGizmoToolRow();

            if (MaidBoneSliderController.GetBone(maid, selectedDef.boneName) == null)
            {
                // ボディ構成差異でボーンが無い場合は操作させない
                _view.DrawLabel("このボーンは存在しません", -1, RowHeight, textColor: Color.yellow);
                return;
            }

            // 再生中は基準ポーズが定まらないため値を読まず、操作された瞬間に停止して書き込む
            var offset = MaidMotionState.IsPlaying(maid)
                ? Vector3.zero
                : MaidBoneSliderController.GetOffset(maid, selectedDef);

            for (var i = 0; i < selectedDef.axes.Length; i++)
            {
                var axisIndex = i;
                var axis = selectedDef.axes[i];

                _view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = axis.label,
                    labelWidth = LabelWidth,
                    width = -1,
                    min = axis.min,
                    max = axis.max,
                    step = 0.1f,
                    defaultValue = 0f,
                    value = offset[axisIndex],
                    onChanged = value =>
                    {
                        MaidMotionState.StopMotion(maid);
                        HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                            "ボーン回転: " + selectedDef.displayName,
                            new[] { MaidBoneSliderController.GetBone(maid, selectedDef.boneName) });
                        MaidBoneSliderController.SetOffsetAxis(maid, selectedDef, axisIndex, value);
                    },
                });
            }
        }

        /// <summary>
        /// ボーン編集ウィンドウで選択したスロットボーン（ポーズ定義なし）の専用表示。
        /// 差分ストアの元値を基準にしたオフセット回転スライダーで編集し、編集はストアへ記録する
        /// </summary>
        private void DrawSlotBoneContent()
        {
            var maid = maidManager.targetMaid;
            var bone = boneEditManager.selectedBone;

            // 退避中は表示に戻す際に上書きされるため操作させない (DrawBoneContent と同じ理由)
            if (!maidManager.IsVisible(maid))
            {
                _view.DrawLabel("非表示中はボーンを操作できません", -1, RowHeight,
                    textColor: Color.yellow);
                return;
            }

            _view.DrawLabel(bone.name, -1, RowHeight);

            DrawGizmoToolRow();
            DrawSlotBoneYureToggle(maid, bone);

            var offset = boneEditManager.GetSelectedBoneOffset(maid);

            for (var i = 0; i < SlotBoneAxisLabels.Length; i++)
            {
                var axisIndex = i;

                _view.DrawSliderValue(new GUIView.SliderOption
                {
                    label = SlotBoneAxisLabels[i],
                    labelWidth = LabelWidth,
                    width = -1,
                    min = -180f,
                    max = 180f,
                    step = 0.1f,
                    defaultValue = 0f,
                    value = offset[axisIndex],
                    onChanged = value =>
                    {
                        HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose,
                            "ボーン編集: " + bone.name, new[] { bone });
                        boneEditManager.SetSelectedBoneOffsetAxis(maid, axisIndex, value);
                    },
                });
            }
        }

        /// <summary>揺れ物理探索結果のキャッシュ (ボーン単位)</summary>
        private Transform _yureCheckedBone;
        private SlotYureTargets _yureTargets;

        /// <summary>「揺れもの」は LabelWidth (40) に収まらないため専用幅</summary>
        private const float YureLabelWidth = 60f;

        /// <summary>
        /// 選択ボーンの揺れもの ON/OFF。対象ボーンを駆動している
        /// TBoneHair_ / DynamicBone / DynamicSkirtBone だけを切り替える。
        /// 関連する物理が無いボーンではトグルを無効化する
        /// </summary>
        private void DrawSlotBoneYureToggle(Maid maid, Transform bone)
        {
            // 関連物理は装着物が変わらない限り不変なので、ボーン単位で探索結果を使い回す
            // (着替えでボーンの Transform ごと作り直されるため、古い参照を掴み続けることはない)
            if (bone != _yureCheckedBone)
            {
                _yureCheckedBone = bone;
                _yureTargets = SlotYureUtil.FindTargets(maid, boneEditManager.targetSlotName, bone);
            }
            var hasYure = _yureTargets != null;

            _view.BeginHorizontal();
            {
                _view.DrawLabel("揺れもの", YureLabelWidth, RowHeight);
                _view.DrawToggle("有効",
                    hasYure && SlotYureUtil.GetYureState(_yureTargets),
                    100, RowHeight, hasYure,
                    on =>
                    {
                        RecordYureEdit(maid, boneEditManager.targetSlotName, bone.name, on);
                        SlotYureUtil.SetYureState(_yureTargets, on);
                    });
            }
            _view.EndLayout();
        }

        /// <summary>
        /// 揺れものの切替を履歴へ登録する。
        /// 対象コンポーネントは着替えで作り直されるため参照は持たず、
        /// 適用時に (スロット, ボーン名) から探索し直す
        /// </summary>
        private static void RecordYureEdit(Maid maid, string slotName, string boneName, bool newState)
        {
            HistoryManager.instance.AddEntry(new DelegateHistoryEntry(
                "揺れもの: " + boneName,
                () => ApplyYureState(maid, slotName, boneName, !newState),
                () => ApplyYureState(maid, slotName, boneName, newState),
                () => HistoryScopeUtils.CanEditMaid(maid)));
        }

        /// <summary>スロットとボーン名から揺れ物理を引き当てて切り替える</summary>
        private static void ApplyYureState(Maid maid, string slotName, string boneName, bool state)
        {
            var slotObj = SlotBoneManager.GetSlotObject(maid, slotName);
            var bone = SlotBoneManager.FindBone(slotObj, boneName);
            var targets = SlotYureUtil.FindTargets(maid, slotName, bone);
            SlotYureUtil.SetYureState(targets, state);
        }

        /// <summary>
        /// メイド選択時の専用行。メイド切替のみ。
        /// この下に共通の Transform 行・ギズモ行が続く
        /// </summary>
        private void DrawMaidContent(Maid maid)
        {
            var maids = MTEUtils.GetReadyMaidList();

            _view.BeginHorizontal();
            {
                _view.DrawLabel("メイド", LabelWidth, RowHeight);

                _maidComboBox.items = maids;
                _maidComboBox.currentIndex = maids.IndexOf(maid);
                _maidComboBox.onSelected = (selected, _) =>
                {
                    // 操作対象も揃えて他のメイド系ウィンドウと表示を一致させる
                    maidManager.targetMaid = selected;
                    selectionManager.Select(selected.gameObject);
                };
                _maidComboBox.DrawButton(_view);
            }
            _view.EndLayout();

            _view.DrawHorizontalLine();
        }

        /// <summary>
        /// ギズモの操作種別と軸空間の切り替え。
        /// SceneView / GameView 双方のギズモがこの設定を共有する
        /// </summary>
        private void DrawGizmoToolRow()
        {
            GizmoToolRowDrawer.Draw(_view, new GizmoToolRowOption
            {
                labelWidth = LabelWidth,
                height = RowHeight,
                getTool = () => GizmoRenderer.currentTool,
                setTool = tool => GizmoRenderer.currentTool = tool,
                getUseLocalSpace = () => GizmoRenderer.useLocalSpace,
                setUseLocalSpace = value => GizmoRenderer.useLocalSpace = value,
            });
        }

        /// <summary>アクティブトグル + オブジェクト名 + 右端のフォーカスボタンの 1 行</summary>
        private void DrawHeader(GameObject go)
        {
            // 名前ラベルを自動幅にするとフォーカスボタンが右端からはみ出すため、
            // 残り幅を明示計算して割り当てる (DrawVector3Row と同じ式)。
            // margin は NextElement が要素ごとに加算するため、要素数ぶん引く
            var labelWidth = _view.viewRect.width - _view.padding.x * 2
                - (HeaderToggleWidth + _view.margin)
                - (HeaderFocusButtonWidth + _view.margin)
                - _view.margin;

            _view.BeginHorizontal();
            {
                _view.DrawToggle(go.activeSelf, HeaderToggleWidth, RowHeight, value =>
                {
                    RecordObjectEdit(go);
                    go.SetActive(value);
                });
                _view.DrawLabel(go.name, labelWidth, RowHeight);

                var focusIcon = ToolbarIcons.GetTexture(ToolbarIcons.Kind.Focus);
                if (_view.DrawTextureButton(focusIcon, HeaderFocusButtonWidth, RowHeight, 4f))
                {
                    SceneViewWindow.instance.FocusOn(go);
                }
            }
            _view.EndLayout();
        }

        /// <summary>
        /// ギズモ操作・選択変更・座標系の切替で Transform 側が変わったらキャッシュを取り直す。
        /// 自分で書き込んだ回転は _cachedRotation と一致するのでリセットされない
        /// </summary>
        private void SyncEulerCache(GameObject go, bool useLocal)
        {
            var rotation = useLocal ? go.transform.localRotation : go.transform.rotation;
            if (_cachedTarget != go || _cachedUseLocal != useLocal || rotation != _cachedRotation)
            {
                _cachedTarget = go;
                _cachedUseLocal = useLocal;
                _cachedRotation = rotation;
                _eulerAngles = useLocal
                    ? go.transform.localEulerAngles
                    : go.transform.eulerAngles;
            }
        }

        /// <summary>Object 行の編集を操作履歴へ記録する (ドラッグ中の連続変更は 1 件に集約される)</summary>
        private static void RecordObjectEdit(GameObject go)
        {
            HistoryManager.instance.BeforeEdit(
                go.GetComponent<Maid>(), HistoryScope.Object,
                "オブジェクト編集: " + go.name, new[] { go.transform });
        }

        private static void SetPosition(Transform t, Vector3 value, bool useLocal)
        {
            if (useLocal)
            {
                t.localPosition = value;
            }
            else
            {
                t.position = value;
            }
        }

        private void ApplyEulerAngles(Transform t, Vector3 eulerAngles, bool useLocal)
        {
            _eulerAngles = eulerAngles;
            if (useLocal)
            {
                t.localEulerAngles = eulerAngles;
                _cachedRotation = t.localRotation;
            }
            else
            {
                t.eulerAngles = eulerAngles;
                _cachedRotation = t.rotation;
            }
        }

        /// <summary>
        /// 拡縮行。連動トグル ON のときは編集した軸の変化比率を他軸へも掛けて
        /// XYZ を同時に拡縮する (編集前の値が 0 の軸は比率が定まらないため全軸を同値にする)
        /// </summary>
        private void DrawScaleRow(GameObject go, Transform t)
        {
            _view.DrawVector3Row(new GUIView.Vector3RowOption
            {
                label = "拡縮",
                labelWidth = LabelWidth,
                height = RowHeight,
                dragSensitivity = ScaleSensitivity,
                value = t.localScale,
                onChangedAxis = (value, index) =>
                {
                    RecordObjectEdit(go);
                    if (config.inspectorScaleLinked)
                    {
                        var oldValue = t.localScale[index];
                        var newValue = value[index];
                        if (Mathf.Abs(oldValue) > ScaleZeroEpsilon)
                        {
                            var linked = t.localScale * (newValue / oldValue);
                            linked[index] = newValue;
                            // 極小値からの編集で比率が発散した場合は連動を諦めて単軸だけ反映する
                            if (!float.IsInfinity(linked.x) && !float.IsNaN(linked.x) &&
                                !float.IsInfinity(linked.y) && !float.IsNaN(linked.y) &&
                                !float.IsInfinity(linked.z) && !float.IsNaN(linked.z))
                            {
                                value = linked;
                            }
                        }
                        else
                        {
                            value = Vector3.one * newValue;
                        }
                    }
                    t.localScale = value;
                },
                onReset = () =>
                {
                    RecordObjectEdit(go);
                    t.localScale = Vector3.one;
                },
                linkIcon = ToolbarIcons.GetTexture(ToolbarIcons.Kind.Link),
                linked = config.inspectorScaleLinked,
                onLinkChanged = on =>
                {
                    config.inspectorScaleLinked = on;
                    config.dirty = true;
                },
            });
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
                labelWidth = LabelWidth,
                height = RowHeight,
                dragSensitivity = dragSensitivity,
                value = value,
                onChanged = onChanged,
                onReset = onReset,
            });
        }
    }
}
