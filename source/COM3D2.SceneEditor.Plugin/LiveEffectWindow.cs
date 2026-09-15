using System;
using System.Collections.Generic;
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
        /// <summary>カスタム値のラベル幅 (「ShiftMin」等が収まる幅。KeyFrameInspector と揃える)</summary>
        private const float CustomLabelWidth = 100f;

        /// <summary>カスタム値のスライダー幅 (-1 でウィンドウ幅いっぱい。KeyFrameInspector と揃える)</summary>
        private const float CustomSliderWidth = -1f;

        /// <summary>
        /// サイリウムの Transform 行のラベル幅。最長の「ランダム位置」が収まる幅で、
        /// 移動・回転の行と XYZ の列を揃える
        /// </summary>
        private const float PsylliumTransformLabelWidth = 80f;

        public static readonly int WINDOW_ID = 8903393;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "ライブ演出";

        private static readonly int ROW_HEIGHT = 20;

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

            try
            {
                DrawTopTabs();
            }
            finally
            {
                TimelineLayerGate.End(_view);
            }

            // ボタン押下で _rootView に登録されたフォーカスをポップアップへ引き渡す。
            // 操作対象を番号タブにした現在このウィンドウにコンボは無く実質 no-op だが、
            // コンボを足したときに取りこぼさないよう定型として残す
            // (同じ理由で各描画の view.SetEnabled(view.focusedComboBox == null) も残している)
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

            // タイムライン未読込でも編集できる。各コントローラーは MonoBehaviour 自身が描画・更新し、
            // 自動値やサイリウムの時間進行はレイヤーが担うため、未読込は「レイヤー未追加」と同じ扱いになる
            // (プリセットで復元したライブ演出をタイムライン無しで調整する用途)
            _topTab = _view.DrawTabs(_topTab, 70, ROW_HEIGHT);

            switch (_topTab)
            {
                case TopTab.ライト:
                    TimelineLayerGate.Begin(_view, typeof(StageLightTimelineLayer), ROW_HEIGHT);
                    DrawStageLight(_view);
                    break;
                case TopTab.レーザー:
                    TimelineLayerGate.Begin(_view, typeof(StageLaserTimelineLayer), ROW_HEIGHT);
                    DrawStageLaser(_view);
                    break;
                case TopTab.サイリウム:
                    TimelineLayerGate.Begin(_view, typeof(PsylliumTimelineLayer), ROW_HEIGHT);
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
        /// ボタン操作を「履歴へ記録してから実行する」形に包む。
        /// GUIView.DrawButton は onBeforeValueChanged を通さないため、
        /// 増減ボタンのコールバックはこれを通して記録する
        /// </summary>
        private static Action Recorded(string label, Action action)
        {
            return () =>
            {
                LiveEffectSnapshot.RecordEdit(label);
                action();
            };
        }

        /// <summary>
        /// 操作対象の番号タブ行 (共有ドロワーの薄い包み)。
        /// ライブ演出の対象は増減の上下限を持たないため、行の高さだけを固定して
        /// canAdd / canRemove / labelWidth は既定のまま使う
        /// </summary>
        private static T DrawTargetTabs<T>(
            GUIView view, string label, IList<T> items, ref int index,
            Action onAdd = null, Action onRemove = null) where T : class
        {
            return TargetTabsDrawer.Draw(
                view, label, items, ref index, ROW_HEIGHT, onAdd, onRemove);
        }

        /// <summary>タブを描かずに選択中の対象だけを取り出す。範囲外なら null</summary>
        private static T GetTarget<T>(IList<T> items, int index) where T : class
        {
            return index >= 0 && index < items.Count ? items[index] : null;
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
        /// ライトの操作対象・コピー先の選択添字 (番号タブで切り替える)。
        /// 一括・個別のサブタブをまたいで選択を保つため、添字はここに 1 つだけ持つ
        /// </summary>
        private int _lightControllerIndex = 0;
        private int _lightIndex = 0;
        private int _copyToLightIndex = 0;

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

            var controller = DrawTargetTabs(
                view, "コントローラー", stageLightManager.controllers, ref _lightControllerIndex,
                Recorded("ライトコントローラー追加", () => stageLightManager.AddController(true)),
                Recorded("ライトコントローラー削除", () => stageLightManager.RemoveController(true)));
            if (controller == null) return;

            // 一括タブではライトを選ばないが、増減ボタンと番号 (本数の目安) はここに出す
            DrawTargetTabs(
                view, "ライト", controller.lights, ref _lightIndex,
                Recorded("ライト追加", () => stageLightManager.AddLight(controller.groupIndex, true)),
                Recorded("ライト削除", () => stageLightManager.RemoveLight(controller.groupIndex, true)));

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.BeginScrollView();

            view.BeginAutoEditMode(() => LiveEffectSnapshot.RecordEdit("ライト一括"));

            _lightRowDrawer.DrawControllerRows(view, controller, controller.name);

            view.EndAutoEditMode();
            view.EndScrollView();
        }

        private void DrawStageLightEdit(GUIView view)
        {
            view.SetEnabled(view.focusedComboBox == null);

            var controller = DrawTargetTabs(
                view, "コントローラー", stageLightManager.controllers, ref _lightControllerIndex,
                Recorded("ライトコントローラー追加", () => stageLightManager.AddController(true)),
                Recorded("ライトコントローラー削除", () => stageLightManager.RemoveController(true)));
            if (controller == null) return;

            var lights = controller.lights;
            var light = DrawTargetTabs(
                view, "ライト", lights, ref _lightIndex,
                Recorded("ライト追加", () => stageLightManager.AddLight(controller.groupIndex, true)),
                Recorded("ライト削除", () => stageLightManager.RemoveLight(controller.groupIndex, true)));

            if (light == null) return;
            if (light.transform == null)
            {
                view.DrawLabel("ライトが生成されていません", 200, ROW_HEIGHT);
                return;
            }

            view.DrawHorizontalLine(Color.gray);

            view.AddSpace(5);

            view.BeginScrollView();

            view.BeginAutoEditMode(() => LiveEffectSnapshot.RecordEdit("ライト"));

            _lightRowDrawer.DrawLightRows(view, controller, light, light.name);

            view.DrawHorizontalLine(Color.gray);

            {
                var copyToLight = DrawTargetTabs(view, "コピー先", lights, ref _copyToLightIndex);

                if (view.DrawButton("コピー", 60, 20))
                {
                    if (copyToLight != null && copyToLight != light)
                    {
                        LiveEffectSnapshot.RecordEdit("ライトのコピー");
                        copyToLight.CopyFrom(light);
                    }
                }

                view.DrawHorizontalLine(Color.gray);
                view.AddSpace(5);
            }

            view.EndAutoEditMode();
            view.EndScrollView();
        }

        /// <summary>
        /// レーザーの操作対象・コピー先の選択添字 (番号タブで切り替える)。
        /// 一括・個別のサブタブをまたいで選択を保つため、添字はここに 1 つだけ持つ
        /// </summary>
        private int _laserControllerIndex = 0;
        private int _laserIndex = 0;
        private int _copyToLaserControllerIndex = 0;
        private int _copyToLaserIndex = 0;

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

            var controller = DrawTargetTabs(
                view, "コントローラー", stageLaserManager.controllers, ref _laserControllerIndex,
                Recorded("レーザーコントローラー追加", () => stageLaserManager.AddController(true)),
                Recorded("レーザーコントローラー削除", () => stageLaserManager.RemoveController(true)));
            if (controller == null) return;

            // 一括タブではレーザーを選ばないが、増減ボタンと番号 (本数の目安) はここに出す
            DrawTargetTabs(
                view, "レーザー", controller.lasers, ref _laserIndex,
                Recorded("レーザー追加", () => stageLaserManager.AddLaser(controller.groupIndex, true)),
                Recorded("レーザー削除", () => stageLaserManager.RemoveLaser(controller.groupIndex, true)));

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.BeginScrollView();

            view.BeginAutoEditMode(() => LiveEffectSnapshot.RecordEdit("レーザー一括"));

            _laserRowDrawer.DrawControllerRows(view, controller, controller.name);

            view.DrawHorizontalLine(Color.gray);

            {
                var copyToController = DrawTargetTabs(view, "コピー先", stageLaserManager.controllers, ref _copyToLaserControllerIndex);

                if (view.DrawButton("コピー", 60, 20))
                {
                    if (copyToController != null && copyToController != controller)
                    {
                        LiveEffectSnapshot.RecordEdit("レーザーコントローラーのコピー");
                        copyToController.CopyFrom(controller);
                        copyToController.UpdateLasers();
                    }
                }

                view.DrawHorizontalLine(Color.gray);
                view.AddSpace(5);
            }

            view.EndAutoEditMode();
            view.EndScrollView();
        }

        private void DrawStageLaserEdit(GUIView view)
        {
            view.SetEnabled(view.focusedComboBox == null);

            var controller = DrawTargetTabs(
                view, "コントローラー", stageLaserManager.controllers, ref _laserControllerIndex,
                Recorded("レーザーコントローラー追加", () => stageLaserManager.AddController(true)),
                Recorded("レーザーコントローラー削除", () => stageLaserManager.RemoveController(true)));
            if (controller == null) return;

            var lasers = controller.lasers;
            var laser = DrawTargetTabs(
                view, "レーザー", lasers, ref _laserIndex,
                Recorded("レーザー追加", () => stageLaserManager.AddLaser(controller.groupIndex, true)),
                Recorded("レーザー削除", () => stageLaserManager.RemoveLaser(controller.groupIndex, true)));

            if (laser == null) return;
            if (laser.transform == null)
            {
                view.DrawLabel("レーザーが生成されていません", 200, ROW_HEIGHT);
                return;
            }

            view.DrawHorizontalLine(Color.gray);

            view.AddSpace(5);

            view.BeginScrollView();

            view.BeginAutoEditMode(() => LiveEffectSnapshot.RecordEdit("レーザー"));

            _laserRowDrawer.DrawLaserRows(view, controller, laser, laser.name);

            view.DrawHorizontalLine(Color.gray);

            {
                var copyToLaser = DrawTargetTabs(view, "コピー先", lasers, ref _copyToLaserIndex);

                if (view.DrawButton("コピー", 60, 20))
                {
                    if (copyToLaser != null && copyToLaser != laser)
                    {
                        LiveEffectSnapshot.RecordEdit("レーザーのコピー");
                        copyToLaser.CopyFrom(laser);
                    }
                }

                view.DrawHorizontalLine(Color.gray);
                view.AddSpace(5);
            }

            view.EndAutoEditMode();
            view.EndScrollView();
        }

        /// <summary>
        /// サイリウムの操作対象・コピー先の選択添字 (番号タブで切り替える)。
        /// 基本・バー・持ち手・アニメ・エリアのサブタブをまたいで選択を保つため、
        /// コントローラーの添字はここに 1 つだけ持つ
        /// </summary>
        private int _psylliumControllerIndex = 0;
        private int _areaIndex = 0;
        private int _patternIndex = 0;
        private int _copyToPsylliumControllerIndex = 0;
        private int _copyToPatternIndex = 0;
        private int _copyToTransformIndex = 0;
        private int _copyToAreaIndex = 0;

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

            var controller = DrawTargetTabs(
                view, "コントローラー", psylliumManager.controllers, ref _psylliumControllerIndex,
                Recorded("サイリウムコントローラー追加", () => psylliumManager.AddController(true)),
                Recorded("サイリウムコントローラー削除", () => psylliumManager.RemoveController(true)));
            if (controller == null) return;

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.BeginScrollView();

            view.BeginAutoEditMode(() => LiveEffectSnapshot.RecordEdit("サイリウム"));

            _psylliumRowDrawer.DrawControllerRows(view, controller);

            view.DrawHorizontalLine(Color.gray);

            {
                var copyToController = DrawTargetTabs(view, "コピー先", psylliumManager.controllers, ref _copyToPsylliumControllerIndex);

                if (view.DrawButton("コピー", 60, 20))
                {
                    if (copyToController != null && copyToController != controller)
                    {
                        LiveEffectSnapshot.RecordEdit("サイリウムコントローラーのコピー");
                        copyToController.CopyFrom(controller);
                        copyToController.Refresh();
                    }
                }
            }

            view.EndAutoEditMode();
            view.EndScrollView();
        }

        private void DrawPsylliumBarConfigEdit(GUIView view)
        {
            view.SetEnabled(view.focusedComboBox == null);

            var controller = DrawTargetTabs(
                view, "コントローラー", psylliumManager.controllers, ref _psylliumControllerIndex,
                Recorded("サイリウムコントローラー追加", () => psylliumManager.AddController(true)),
                Recorded("サイリウムコントローラー削除", () => psylliumManager.RemoveController(true)));
            if (controller == null) return;

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.BeginScrollView();

            view.BeginAutoEditMode(() => LiveEffectSnapshot.RecordEdit("サイリウム バー設定"));

            _psylliumRowDrawer.DrawBarConfigRows(view, controller, controller.barConfig.name);

            view.EndAutoEditMode();
            view.EndScrollView();
        }

        private void DrawPsylliumHandConfigEdit(GUIView view)
        {
            view.SetEnabled(view.focusedComboBox == null);

            var controller = DrawTargetTabs(
                view, "コントローラー", psylliumManager.controllers, ref _psylliumControllerIndex,
                Recorded("サイリウムコントローラー追加", () => psylliumManager.AddController(true)),
                Recorded("サイリウムコントローラー削除", () => psylliumManager.RemoveController(true)));
            if (controller == null) return;

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.BeginScrollView();

            view.BeginAutoEditMode(() => LiveEffectSnapshot.RecordEdit("サイリウム 持ち手設定"));

            _psylliumRowDrawer.DrawHandConfigRows(view, controller);

            view.EndAutoEditMode();
            view.EndScrollView();
        }

        private void DrawPsylliumPatternConfigEdit(GUIView view)
        {
            view.SetEnabled(view.focusedComboBox == null);

            var controller = DrawTargetTabs(
                view, "コントローラー", psylliumManager.controllers, ref _psylliumControllerIndex,
                Recorded("サイリウムコントローラー追加", () => psylliumManager.AddController(true)),
                Recorded("サイリウムコントローラー削除", () => psylliumManager.RemoveController(true)));
            if (controller == null) return;

            var pattern = DrawTargetTabs(
                view, "パターン", controller.patterns, ref _patternIndex,
                Recorded("サイリウムパターン追加", () => psylliumManager.AddPattern(controller.groupIndex, true)),
                Recorded("サイリウムパターン削除", () => psylliumManager.RemovePattern(controller.groupIndex, true)));

            if (pattern == null) return;

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.BeginScrollView();

            view.BeginAutoEditMode(() => LiveEffectSnapshot.RecordEdit("サイリウム パターン"));

            var patternConfig = pattern.patternConfig;
            var updateTransform = false;
            var defaultTrans = TransformDataPsylliumPattern.defaultTrans;
            var defaultConfig = TransformDataPsylliumPattern.defaultConfig;

            _handTabType = DrawInnerTabs(_handTabType, 50);

            DrawPsylliumTransformConfigEdit(view);

            {
                var initialPosition = defaultConfig.randomPositionRange;
                var transformCache = view.GetTransformCache(null);
                transformCache.position = patternConfig.randomPositionRange;

                updateTransform |= TimelineLayerBase.DrawPosition(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    initialPosition,
                    label: "ランダム位置",
                    labelWidth: PsylliumTransformLabelWidth);

                if (updateTransform)
                {
                    patternConfig.randomPositionRange = transformCache.position;
                }
            }

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
                    initialEulerAngles,
                    label: "ランダム角度",
                    labelWidth: PsylliumTransformLabelWidth);

                if (updateTransform)
                {
                    patternConfig.randomEulerAnglesRange = transformCache.eulerAngles;
                }
            }

            updateTransform |= view.DrawCustomValueInt(
                defaultTrans.timeCountInfo,
                patternConfig.timeCount,
                y => patternConfig.timeCount = y,
                labelWidth: CustomLabelWidth,
                sliderWidth: CustomSliderWidth);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.timeRangeInfo,
                patternConfig.timeRange,
                y => patternConfig.timeRange = y,
                labelWidth: CustomLabelWidth,
                sliderWidth: CustomSliderWidth);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.timeShiftMinInfo,
                patternConfig.timeShiftMin,
                y => patternConfig.timeShiftMin = y,
                labelWidth: CustomLabelWidth,
                sliderWidth: CustomSliderWidth);

            updateTransform |= view.DrawCustomValueFloat(
                defaultTrans.timeShiftMaxInfo,
                patternConfig.timeShiftMax,
                y => patternConfig.timeShiftMax = y,
                labelWidth: CustomLabelWidth,
                sliderWidth: CustomSliderWidth);

            updateTransform |= view.DrawCustomValueIntRandom(
                defaultTrans.randomSeedInfo,
                patternConfig.randomSeed,
                y => patternConfig.randomSeed = y);

            if (updateTransform)
            {
                controller.ManualUpdate(psylliumPlayingTime);
            }

            {
                var copyToPattern = DrawTargetTabs(view, "コピー先", controller.patterns, ref _copyToPatternIndex);

                if (view.DrawButton("コピー", 60, 20))
                {
                    if (copyToPattern != null && copyToPattern != pattern)
                    {
                        LiveEffectSnapshot.RecordEdit("サイリウムパターンのコピー");
                        copyToPattern.patternConfig.CopyFrom(patternConfig);
                        controller.ManualUpdate(psylliumPlayingTime);
                    }
                }
            }

            view.EndAutoEditMode();

            view.EndScrollView();
        }

        private void DrawPsylliumTransformConfigEdit(GUIView view)
        {
            var controller = GetTarget(psylliumManager.controllers, _psylliumControllerIndex);
            if (controller == null)
            {
                view.DrawLabel("コントローラーを選択してください", 200, 20);
                return;
            }

            var pattern = GetTarget(controller.patterns, _patternIndex);
            if (pattern == null)
            {
                view.DrawLabel("パターンを選択してください", 200, 20);
                return;
            }

            var transformConfig = pattern.transformConfig;
            var updateTransform = false;
            var defaultTrans = TransformDataPsylliumTransform.defaultTrans;
            var defaultConfig = TransformDataPsylliumTransform.defaultConfig;

            if (_handTabType == HandTabType.右手)
            {
                var initialPosition = defaultConfig.positionRight;
                var transformCache = view.GetTransformCache(null);
                transformCache.position = transformConfig.positionRight;

                updateTransform |= TimelineLayerBase.DrawPosition(
                    view,
                    transformCache,
                    TimelineLayerBase.TransformEditType.全て,
                    initialPosition,
                    label: "移動",
                    labelWidth: PsylliumTransformLabelWidth);

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
                    initialPosition,
                    label: "移動",
                    labelWidth: PsylliumTransformLabelWidth);

                if (updateTransform)
                {
                    transformConfig.positionLeft = transformCache.position;
                }
            }

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
                    initialEulerAngles,
                    label: "回転",
                    labelWidth: PsylliumTransformLabelWidth);

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
                    initialEulerAngles,
                    label: "回転",
                    labelWidth: PsylliumTransformLabelWidth);

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
                var copyToPattern = DrawTargetTabs(view, "コピー先", controller.patterns, ref _copyToTransformIndex);

                if (view.DrawButton("コピー", 60, 20))
                {
                    if (copyToPattern != null && copyToPattern != pattern)
                    {
                        LiveEffectSnapshot.RecordEdit("サイリウム移動回転のコピー");
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

            var controller = DrawTargetTabs(
                view, "コントローラー", psylliumManager.controllers, ref _psylliumControllerIndex,
                Recorded("サイリウムコントローラー追加", () => psylliumManager.AddController(true)),
                Recorded("サイリウムコントローラー削除", () => psylliumManager.RemoveController(true)));
            if (controller == null) return;

            var areas = controller.areas;
            var area = DrawTargetTabs(
                view, "エリア", areas, ref _areaIndex,
                Recorded("サイリウムエリア追加", () => psylliumManager.AddArea(controller.groupIndex, true)),
                Recorded("サイリウムエリア削除", () => psylliumManager.RemoveArea(controller.groupIndex, true)));

            if (area == null) return;
            if (area.transform == null)
            {
                view.DrawLabel("エリアが生成されていません", 200, ROW_HEIGHT);
                return;
            }

            view.DrawHorizontalLine(Color.gray);

            view.AddSpace(5);

            view.BeginScrollView();

            view.BeginAutoEditMode(() => LiveEffectSnapshot.RecordEdit("サイリウム エリア"));

            _psylliumRowDrawer.DrawAreaRows(view, area);

            view.DrawHorizontalLine(Color.gray);

            {
                var copyToArea = DrawTargetTabs(view, "コピー先", areas, ref _copyToAreaIndex);

                view.BeginHorizontal();
                {
                    if (view.DrawButton("コピー", 60, 20))
                    {
                        if (copyToArea != null && copyToArea != area)
                        {
                            LiveEffectSnapshot.RecordEdit("サイリウムエリアのコピー");
                            copyToArea.CopyFrom(area, timelineConfig.psylliumAreaCopyIgnoreTransform);
                        }
                    }

                    if (view.DrawButton("全エリアにコピー", 120, 20))
                    {
                        LiveEffectSnapshot.RecordEdit("サイリウムエリアの全コピー");

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

                view.DrawToggle("位置/角度/サイズ/配置を反映しない", timelineConfig.psylliumAreaCopyIgnoreTransform, 250, 20, newValue =>
                {
                    timelineConfig.psylliumAreaCopyIgnoreTransform = newValue;
                });

                view.DrawHorizontalLine(Color.gray);
                view.AddSpace(5);
            }

            view.EndAutoEditMode();
            view.EndScrollView();
        }
    }
}
