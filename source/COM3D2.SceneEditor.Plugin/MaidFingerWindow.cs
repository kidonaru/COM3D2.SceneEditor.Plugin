using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 手指・足指のブレンドを操作するウィンドウ。
    /// MTE と同じくロック + 開閉スライダーで指ブレンドを操作する。
    /// プリセットタブでは保存した指の形を適用できる
    /// </summary>
    public class MaidFingerWindow : MaidWindowBase
    {
        public static readonly int WINDOW_ID = 8903356;

        /// <summary>フォーカスボタンの幅。Inspector のヘッダーと同じ正方形アイコン相当</summary>
        private const int FocusButtonWidth = 20;

        private static readonly string[] ArmDigitNames = { "親", "人", "中", "薬", "小" };
        private static readonly string[] LegDigitNames = { "親", "人", "中" };

        /// <summary>ウィンドウ内の内部タブ</summary>
        private enum DigitTabType
        {
            手指,
            足指,
            プリセット,
        }

        private DigitTabType _tabType = DigitTabType.手指;

        /// <summary>プリセット適用部位トグル 1 件分。部位と表示名を対で持つ</summary>
        private struct PresetTarget
        {
            public readonly FingerBlendType type;
            public readonly string name;

            public PresetTarget(FingerBlendType type, string name)
            {
                this.type = type;
                this.name = name;
            }
        }

        private static readonly PresetTarget[] PresetTargets =
        {
            new PresetTarget(FingerBlendType.RightArm, "右手"),
            new PresetTarget(FingerBlendType.LeftArm, "左手"),
            new PresetTarget(FingerBlendType.RightLeg, "右足"),
            new PresetTarget(FingerBlendType.LeftLeg, "左足"),
        };

        /// <summary>プリセットのどの部位を適用するか。PresetTargets と同じ並び</summary>
        private readonly bool[] _presetTargetEnabled;

        /// <summary>保存済みプリセット名一覧。描画のたびに再列挙はせず、表示時と保存/削除時に更新する</summary>
        private List<string> _presetNames = null;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "指";

        private static MaidFingerWindow _instance = null;
        public static MaidFingerWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MaidFingerWindow();
                }
                return _instance;
            }
        }

        private MaidFingerWindow()
        {
            // プリセット適用部位の既定は全選択
            _presetTargetEnabled = new bool[PresetTargets.Length];
            for (var i = 0; i < _presetTargetEnabled.Length; i++)
            {
                _presetTargetEnabled[i] = true;
            }
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.maidFingerPosX;
            y = config.maidFingerPosY;
            width = config.maidFingerWidth;
            height = config.maidFingerHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.maidFingerPosX = x;
            config.maidFingerPosY = y;
            config.maidFingerWidth = width;
            config.maidFingerHeight = height;
        }

        public override bool savedVisible
        {
            get => config.maidFingerVisible;
            set => config.maidFingerVisible = value;
        }

        /// <summary>
        /// 開いたときにプリセット一覧を取り直す。
        /// フォルダを直接編集された場合も開き直せば一覧に反映される
        /// </summary>
        protected override void OnShowChanged(bool visible)
        {
            if (visible)
            {
                _presetNames = null;
            }
        }

        protected override void DrawMaidContent(Maid target)
        {
            if (target == null)
            {
                return;
            }

            DrawHeader(view, target);

            _tabType = DrawInnerTabs(_tabType, 80);

            if (_tabType == DigitTabType.プリセット)
            {
                DrawPresetContent(view, target);
            }
            else
            {
                DrawFingerBlend(view, target, _tabType == DigitTabType.手指);
            }
        }

        /// <summary>
        /// タブの上の共通ヘッダー。どのタブからでも手指・足指をまとめて保存・リセットできる
        /// </summary>
        private void DrawHeader(GUIView view, Maid target)
        {
            view.BeginHorizontal();
            {
                if (view.DrawButton("指を保存", 90, ROW_HEIGHT))
                {
                    SaveFingerPresetPopupWindow.Show(
                        presetName => SavePreset(target, presetName));
                }

                // 指関節ごとのドラッグ点を出すトグル。表示条件は体のドラッグ点と
                // 同じ（ボーン表示 ON）なので、OFF 中に押しても点は出ない
                var isFingerEdit = maidManager.isFingerEditMode;
                if (view.DrawButton("個別編集", 80, ROW_HEIGHT, true,
                    isFingerEdit ? EditorSubWindow.ACCENT_COLOR : Color.white))
                {
                    maidManager.isFingerEditMode = !isFingerEdit;
                }

                // リセットは右端揃え
                const int resetButtonWidth = 60;
                view.currentPos.x = view.viewRect.width - view.padding.x * 2 - resetButtonWidth;

                if (view.DrawButton("リセット", resetButtonWidth, ROW_HEIGHT))
                {
                    ResetAllFingers(target);
                }
            }
            view.EndLayout();

            view.DrawHorizontalLine(Color.gray);
        }

        /// <summary>手指・足指すべてのスライダーとロックを初期状態へ戻す</summary>
        private static void ResetAllFingers(Maid target)
        {
            MaidMotionState.StopMotion(target);
            HistoryManager.instance.BeforeEdit(target, HistoryScope.Pose, "指リセット", GetPresetBones());

            foreach (var type in MaidFingerPresetManager.BlendTypes)
            {
                var unit = maidManager.fingerBlendController.GetUnit(type);
                if (unit == null)
                {
                    continue;
                }
                unit.Reset();
                unit.Apply();
            }
        }

        private void DrawFingerBlend(GUIView view, Maid target, bool isArm)
        {
            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            // 最後の要素なので高さ -1（残り全部）でウィンドウの伸縮に追従させる
            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            if (isArm)
            {
                DrawFingerBlendBlock(view, target,
                    FingerBlendType.RightArm, FingerBlendType.LeftArm, "右手", "左手", ArmDigitNames);
                DrawFingerBlendBlock(view, target,
                    FingerBlendType.LeftArm, FingerBlendType.RightArm, "左手", "右手", ArmDigitNames);
            }
            else
            {
                DrawFingerBlendBlock(view, target,
                    FingerBlendType.RightLeg, FingerBlendType.LeftLeg, "右足", "左足", LegDigitNames);
                DrawFingerBlendBlock(view, target,
                    FingerBlendType.LeftLeg, FingerBlendType.RightLeg, "左足", "右足", LegDigitNames);
            }

            view.EndScrollView();
        }

        /// <summary>1 部位（片手/片足）ぶんの指ブレンド UI</summary>
        private void DrawFingerBlendBlock(
            GUIView view,
            Maid maid,
            FingerBlendType type,
            FingerBlendType otherType,
            string name,
            string otherName,
            string[] digitNames)
        {
            var unit = maidManager.fingerBlendController.GetUnit(type);
            var otherUnit = maidManager.fingerBlendController.GetUnit(otherType);
            if (unit == null || otherUnit == null)
            {
                return;
            }

            view.BeginHorizontal();
            {
                view.DrawLabel(name, LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);

                // モーション再生でポーズが上書きされた後、スライダー値を再適用するためのボタン
                if (view.DrawButton("更新", 50, ROW_HEIGHT))
                {
                    RecordFingerEdit(maid, unit, "指ブレンド更新: " + name);
                    ApplyFingerBlend(unit);
                }

                // SceneView のカメラをこの部位の指へ寄せる。
                // アイコンは Inspector のフォーカスボタンと共通
                var focusIcon = ToolbarIcons.GetTexture(ToolbarIcons.Kind.Focus);
                if (view.DrawTextureButton(focusIcon, FocusButtonWidth, ROW_HEIGHT, 4f))
                {
                    FocusOnFinger(unit);
                }

                if (view.DrawButton(otherName + "にコピー", 100, ROW_HEIGHT))
                {
                    RecordFingerEdit(maid, otherUnit, "指コピー: " + name + "→" + otherName);
                    otherUnit.CopyFrom(unit);
                    ApplyFingerBlend(otherUnit);
                }
            }
            view.EndLayout();

            view.BeginHorizontal();
            {
                view.DrawLabel("ロック", LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);

                // ロック中の指はスライダーを動かしてもロック時点の値を保ち続ける
                for (var i = 0; i < unit.digitCount; i++)
                {
                    var isLock = unit.IsLock(i);
                    if (view.DrawButton(digitNames[i], 25, ROW_HEIGHT, true,
                        isLock ? EditorSubWindow.ACCENT_COLOR : Color.white))
                    {
                        RecordFingerEdit(maid, unit, "指ロック: " + name + digitNames[i]);
                        unit.SetLock(i, !isLock);
                        ApplyFingerBlend(unit);
                    }
                }

                var isAllLock = unit.isAllLock;
                if (view.DrawButton("全", 25, ROW_HEIGHT, true,
                    isAllLock ? EditorSubWindow.ACCENT_COLOR : Color.white))
                {
                    RecordFingerEdit(maid, unit, "指ロック全: " + name);
                    unit.LockAll(!isAllLock);
                    ApplyFingerBlend(unit);
                }

                if (view.DrawButton("反", 25, ROW_HEIGHT))
                {
                    RecordFingerEdit(maid, unit, "指ロック反転: " + name);
                    unit.LockReverse();
                    ApplyFingerBlend(unit);
                }
            }
            view.EndLayout();

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = name + " 開き",
                labelWidth = LABEL_WIDTH,
                width = -1,
                min = 0f,
                max = 1f,
                defaultValue = 0f,
                value = unit.valueOpen,
                onChanged = value =>
                {
                    RecordFingerEdit(maid, unit, "指ブレンド: " + name + " 開き");
                    unit.valueOpen = value;
                    ApplyFingerBlend(unit);
                },
            });

            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = name + " 閉じ",
                labelWidth = LABEL_WIDTH,
                width = -1,
                min = 0f,
                max = 1f,
                defaultValue = 0f,
                value = unit.valueFist,
                onChanged = value =>
                {
                    RecordFingerEdit(maid, unit, "指ブレンド: " + name + " 閉じ");
                    unit.valueFist = value;
                    ApplyFingerBlend(unit);
                },
            });

            view.DrawHorizontalLine();
        }

        /// <summary>
        /// プリセットタブ。保存済みプリセットを列挙し、押したら即適用する。
        /// 保存ボタンはどのタブからも押せるようヘッダー側にある
        /// </summary>
        private void DrawPresetContent(GUIView view, Maid target)
        {
            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            DrawPresetTargetToggles(view);

            // 最後の要素なので高さ -1（残り全部）でウィンドウの伸縮に追従させる
            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            if (_presetNames == null)
            {
                _presetNames = MaidFingerPresetManager.GetPresetNames();
            }

            if (_presetNames.Count == 0)
            {
                view.DrawLabel("保存されたプリセットはありません", -1, ROW_HEIGHT);
            }
            else
            {
                const int deleteButtonWidth = 50;
                // スクロールバー分は viewRect が既に差し引かれているため、削除ボタンと間隔だけ引く
                var nameButtonWidth = view.viewRect.width - view.padding.x * 2
                    - deleteButtonWidth - view.margin;

                foreach (var presetName in _presetNames)
                {
                    view.BeginHorizontal();
                    {
                        if (view.DrawButton(presetName, nameButtonWidth, ROW_HEIGHT))
                        {
                            LoadPreset(target, presetName);
                        }

                        if (view.DrawButton("削除", deleteButtonWidth, ROW_HEIGHT))
                        {
                            var targetName = presetName;
                            DialogPopupWindow.ShowConfirmDialog(
                                "プリセット「" + targetName + "」を削除しますか？",
                                () =>
                                {
                                    MaidFingerPresetManager.DeletePreset(targetName);
                                    _presetNames = null;
                                });
                        }
                    }
                    view.EndLayout();
                }
            }

            view.EndScrollView();
        }

        /// <summary>プリセットのうちどの部位を適用するかを選ぶトグル列</summary>
        private void DrawPresetTargetToggles(GUIView view)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel("適用部位", LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);

                for (var i = 0; i < PresetTargets.Length; i++)
                {
                    var isEnabled = _presetTargetEnabled[i];
                    if (view.DrawButton(PresetTargets[i].name, 45, ROW_HEIGHT, true,
                        isEnabled ? EditorSubWindow.ACCENT_COLOR : Color.white))
                    {
                        _presetTargetEnabled[i] = !isEnabled;
                    }
                }
            }
            view.EndLayout();

            view.DrawHorizontalLine();
        }

        /// <summary>トグルで選択されている部位の一覧</summary>
        private List<FingerBlendType> GetEnabledPresetTargets()
        {
            var targets = new List<FingerBlendType>();
            for (var i = 0; i < PresetTargets.Length; i++)
            {
                if (_presetTargetEnabled[i])
                {
                    targets.Add(PresetTargets[i].type);
                }
            }
            return targets;
        }

        /// <summary>
        /// ポップアップで確定した名前で保存し、一覧を更新する。
        /// 名前検証と上書き確認はポップアップ側で済んでいる
        /// </summary>
        private void SavePreset(Maid target, string presetName)
        {
            // ポップアップ表示中に操作対象が変わっていたら、別メイドの指を保存しないよう中止する
            if (maidManager.targetMaid != target)
            {
                DialogPopupWindow.ShowDialog("操作対象が変わったため保存を中止しました");
                return;
            }

            MaidFingerPresetManager.SavePreset(maidManager.fingerBlendController, presetName);
            _presetNames = MaidFingerPresetManager.GetPresetNames();
        }

        /// <summary>
        /// プリセットを適用する。
        /// トグルで選択され、かつプリセットに記録されている部位だけが書き換わる
        /// </summary>
        private void LoadPreset(Maid target, string presetName)
        {
            var targets = GetEnabledPresetTargets();
            if (targets.Count == 0)
            {
                DialogPopupWindow.ShowDialog("適用部位が選択されていません");
                return;
            }

            MaidMotionState.StopMotion(target);
            // undo で戻るのはボーンのみで、開き/握りスライダーの表示値までは戻らない
            HistoryManager.instance.BeforeEdit(target, HistoryScope.Pose,
                "指プリセット: " + presetName, GetPresetBones(targets));
            MaidFingerPresetManager.LoadPreset(
                maidManager.fingerBlendController, presetName, targets);
        }

        /// <summary>指定部位が書き換えるボーン。操作履歴の記録対象用</summary>
        private static IEnumerable<Transform> GetPresetBones(
            IEnumerable<FingerBlendType> types = null)
        {
            foreach (var type in types ?? MaidFingerPresetManager.BlendTypes)
            {
                var unit = maidManager.fingerBlendController.GetUnit(type);
                if (unit == null)
                {
                    continue;
                }
                foreach (var bone in unit.bones)
                {
                    yield return bone;
                }
            }
        }

        /// <summary>
        /// 指ブレンドの変更を記録する。ユニットの値を書き換える**前**に呼ぶこと。
        /// 後から呼ぶと変更前として変更後の値を控えてしまう
        /// </summary>
        private static void RecordFingerEdit(Maid maid, FingerBlendUnit unit, string description)
        {
            // 停止でボーンが動くため、変更前を控える前に停止させる
            MaidMotionState.StopMotion(maid);
            // undo で戻るのはボーンと開き/握り/ロックの表示値まで
            HistoryManager.instance.BeforeEdit(maid, HistoryScope.Pose, description, unit.bones);
        }

        /// <summary>
        /// 指のボーン全体が収まる範囲へ SceneView のカメラを寄せる。
        /// ボーンは Renderer を持たないため、位置から範囲を組み立てて渡す
        /// </summary>
        private static void FocusOnFinger(FingerBlendUnit unit)
        {
            var hasBone = false;
            var bounds = new Bounds();

            foreach (var bone in unit.bones)
            {
                if (bone == null)
                {
                    continue;
                }

                if (!hasBone)
                {
                    hasBone = true;
                    bounds = new Bounds(bone.position, Vector3.zero);
                    continue;
                }
                bounds.Encapsulate(bone.position);
            }

            if (!hasBone)
            {
                return;
            }

            SceneViewWindow.instance.FocusOnBounds(bounds);
        }

        /// <summary>ユニットの現在値をボーンへ反映する</summary>
        private static void ApplyFingerBlend(FingerBlendUnit unit)
        {
            unit.Apply();
        }
    }
}
