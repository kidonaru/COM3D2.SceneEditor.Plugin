using System;
using System.Collections.Generic;
using System.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 表情モーフを編集するウィンドウ。スライダーは 0-1、オプション系はトグル。
    /// プリセットタブではフォトモードの内蔵表情とユーザー保存表情を適用できる
    /// </summary>
    public class MaidFaceWindow : MaidWindowBase
    {
        public static readonly int WINDOW_ID = 8903355;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "表情";

        /// <summary>
        /// ウィンドウ内の内部タブ。先頭 4 つは FaceMorphCategory と同順で 1:1 対応
        /// </summary>
        private enum FaceTab
        {
            目,
            眉,
            口,
            オプション,
            視線,
            プリセット,
        }

        private FaceTab _tab = FaceTab.目;

        /// <summary>プリセットタブのユーザー保存表情カテゴリ名</summary>
        private const string MY_FACE_CATEGORY = "マイ表情";

        /// <summary>プリセットタブで選択中のカテゴリ</summary>
        private string _presetCategory = MY_FACE_CATEGORY;

        private readonly GUIComboBox<string> _presetCategoryComboBox = new GUIComboBox<string>
        {
            getName = (name, _) => name,
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        /// <summary>キー化していないときに選べる向け先。毎フレーム複製しないよう控えておく</summary>
        private static readonly List<MaidLookMode> UnkeyedLookModes =
            MaidLookBridge.GetSelectableModes(false);

        private readonly GUIComboBox<MaidLookMode> _lookModeComboBox = new GUIComboBox<MaidLookMode>
        {
            getName = (mode, _) => mode.ToString(),
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 100),
        };

        /// <summary>メイドモードの注視対象。呼出済みメイドから選ぶ</summary>
        private readonly GUIComboBox<Maid> _lookMaidComboBox = new GUIComboBox<Maid>
        {
            getName = (maid, _) => maid.status.fullNameJpStyle,
        };

        /// <summary>メイドモードで見る部位。タイムラインのキーと同じ列挙を使う</summary>
        private readonly GUIComboBox<MTEP.MaidPointType> _lookMaidPointComboBox =
            new GUIComboBox<MTEP.MaidPointType>
            {
                items = new List<MTEP.MaidPointType>((MTEP.MaidPointType[])
                    Enum.GetValues(typeof(MTEP.MaidPointType))),
                getName = (type, _) => MTEP.MaidCache.GetMaidPointTypeName(type),
            };

        /// <summary>モデルモードの注視対象。スタジオモデルから選ぶ</summary>
        private readonly GUIComboBox<MTEP.StudioModelStat> _lookModelComboBox =
            new GUIComboBox<MTEP.StudioModelStat>
            {
                getName = (model, _) => model.displayName,
            };

        /// <summary>タイムライン視線の行描画。Inspector の項目表示と共有する</summary>
        private readonly TimelineLookRowDrawer _timelineLookRowDrawer = new TimelineLookRowDrawer();

        /// <summary>瞳位置・瞳サイズの行描画。Inspector の項目表示と共有する</summary>
        private readonly EyesPosRowDrawer _eyesPosRowDrawer = new EyesPosRowDrawer();

        /// <summary>
        /// 目線種別。顔/瞳の追従トグルのプリセットで、書き込み先はタイムライン全体の設定。
        /// 選択確定は ComboBoxPopupWindow 側で後から呼ばれるため、
        /// 開いている間にタイムラインが閉じられた場合に備えて null を弾く
        /// </summary>
        private readonly GUIComboBox<Maid.EyeMoveType> _eyeMoveTypeComboBox =
            new GUIComboBox<Maid.EyeMoveType>
            {
                items = Enum.GetValues(typeof(Maid.EyeMoveType))
                    .Cast<Maid.EyeMoveType>().ToList(),
                getName = (type, _) => type.ToString(),
                onSelected = (type, _) =>
                {
                    var timeline = MTEP.TimelineManager.instance.timeline;
                    if (timeline != null)
                    {
                        timeline.eyeMoveType = type;
                    }
                },
            };

        private static MaidLookController lookController
            => MaidManipulateManager.instance.lookController;

        /// <summary>カテゴリ一覧のキャッシュ。毎フレームの再構築を避ける</summary>
        private List<string> _presetCategories = null;

        /// <summary>ユーザー保存表情の一覧。描画のたびに再列挙はせず、表示時と保存/削除時に更新する</summary>
        private List<string> _userPresetNames = null;

        /// <summary>内蔵プリセットの読み込みに失敗したか。毎フレームの再試行とログ多発を避ける</summary>
        private static bool _photoFaceDataLoadFailed = false;

        private static MaidFaceWindow _instance = null;
        public static MaidFaceWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MaidFaceWindow();
                }
                return _instance;
            }
        }

        private MaidFaceWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.maidFacePosX;
            y = config.maidFacePosY;
            width = config.maidFaceWidth;
            height = config.maidFaceHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.maidFacePosX = x;
            config.maidFacePosY = y;
            config.maidFaceWidth = width;
            config.maidFaceHeight = height;
        }

        public override bool savedVisible
        {
            get => config.maidFaceVisible;
            set => config.maidFaceVisible = value;
        }

        /// <summary>
        /// 開いたときにユーザー保存表情の一覧を取り直す。
        /// フォルダを直接編集された場合も開き直せば一覧に反映される。
        /// 内蔵プリセットの読み込み失敗も、開き直しを再試行の機会にする
        /// </summary>
        protected override void OnShowChanged(bool visible)
        {
            if (visible)
            {
                _userPresetNames = null;
                _presetCategories = null;
                _photoFaceDataLoadFailed = false;
            }
        }

        protected override void DrawMaidContent(Maid target)
        {
            if (target == null)
            {
                return;
            }

            DrawHeader(view, target);

            _tab = DrawInnerTabs(_tab, 70);

            if (_tab == FaceTab.プリセット)
            {
                DrawPresetContent(view, target);
            }
            else if (_tab == FaceTab.視線)
            {
                DrawLookContent(view, target);
            }
            else
            {
                DrawMorphList(view, target);
            }
        }

        /// <summary>モーフ一覧を描くタブか。視線・プリセットは対象カテゴリを持たない</summary>
        private bool isMorphTab => _tab != FaceTab.視線 && _tab != FaceTab.プリセット;

        /// <summary>現在タブに対応するモーフカテゴリ。プリセットタブでは呼ばない</summary>
        private FaceMorphCategory currentMorphCategory
        {
            get
            {
                switch (_tab)
                {
                    case FaceTab.目: return FaceMorphCategory.目;
                    case FaceTab.眉: return FaceMorphCategory.眉;
                    case FaceTab.口: return FaceMorphCategory.口;
                    case FaceTab.オプション: return FaceMorphCategory.オプション;
                    default: return FaceMorphCategory.目;
                }
            }
        }

        /// <summary>タブの上の共通ヘッダー。表情保存・強制上書き・リセットを表示する</summary>
        private void DrawHeader(GUIView view, Maid target)
        {
            view.BeginHorizontal();
            {
                // どのタブからでも現在の表情をマイ表情へ保存できるようにする。名前はポップアップで入力させる
                if (view.DrawButton("表情保存", 90, ROW_HEIGHT))
                {
                    SaveFacePresetPopupWindow.Show(presetName => SavePreset(target, presetName));
                }

                // まばたき中は全カテゴリのモーフ値が毎フレーム上書きされるため、明示的に切り替えられるようにする。
                // 表示は「編集した表情を固定するか」の視点に揃えるため、まばたきの反転として扱う。
                // 表情レイヤー再生中はタイムラインがまばたきを抑止するため編集不可にする (設定は解除時に復元される)
                var isForceOverride = !MaidFaceMorphController.GetMabataki(target);
                view.SetEnabled(!MaidFaceMorphController.IsMabatakiSuppressed(target));
                view.DrawToggle("強制上書き", isForceOverride, 95, ROW_HEIGHT,
                    newIsForceOverride =>
                    {
                        HistoryManager.instance.BeforeEdit(target, HistoryScope.Face, "強制上書き切替");
                        MaidFaceMorphController.SetMabataki(target, !newIsForceOverride);
                    });
                view.SetEnabled(true);

                // リセットは右端揃え。対象カテゴリを持つモーフ系タブでのみ出す
                if (isMorphTab)
                {
                    const int resetButtonWidth = 60;
                    view.currentPos.x = view.viewRect.width - view.padding.x * 2 - resetButtonWidth;

                    if (view.DrawButton("リセット", resetButtonWidth, ROW_HEIGHT))
                    {
                        HistoryManager.instance.BeforeEdit(target, HistoryScope.Face,
                            "表情リセット: " + currentMorphCategory);
                        MaidFaceMorphController.ResetCategory(target, currentMorphCategory);

                        // リセットは未編集状態へ戻す操作なので、カテゴリ内のチェックも外す
                        var store = FaceEditManager.instance.FindStore(target);
                        if (store != null)
                        {
                            foreach (var def in MaidFaceMorphController.GetAvailableMorphs(
                                target, currentMorphCategory))
                            {
                                store.Unmark(def.name);
                            }
                        }
                    }
                }
            }
            view.EndLayout();

            view.DrawHorizontalLine(Color.gray);
        }

        private void DrawMorphList(GUIView view, Maid target)
        {
            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            // 最後の要素なので高さ -1（残り全部）でウィンドウの伸縮に追従させる
            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            foreach (var def in MaidFaceMorphController.GetAvailableMorphs(target, currentMorphCategory))
            {
                FaceMorphRowDrawer.Draw(view, target, def, LABEL_WIDTH, ROW_HEIGHT);
            }

            view.EndScrollView();
        }

        /// <summary>
        /// 視線タブ。SE の向け先とタイムラインのキー化設定を 1 セクションで描く。
        /// 向け先 (trsLookTarget) の所有者は MaidLookController で、
        /// 「視線をキー化」が有効な間はタイムラインの注視先がそれを駆動する
        /// (MaidLookBridge を参照)。そのため両者を並べず、
        /// キー化の有無で有効になる行を切り替える
        /// </summary>
        private void DrawLookContent(GUIView view, Maid target)
        {
            var timeline = MTEP.TimelineManager.instance.timeline;
            var mode = lookController.GetMode(target);

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            DrawLookKeyToggleRow(view, timeline);

            // キー化トグルの反映は同じ描画中に起きるため、以降の行はトグル後の値で決める
            var isKeyed = TimelineLookRowDrawer.IsHeadKeyEnabled;

            DrawLookTargetRows(view, target, mode, isKeyed);
            DrawEyeMoveTypeRow(view, timeline);
            DrawHeadToCamRow(view, target);
            DrawLookDirectionSliders(view, target, mode, isKeyed);

            if (isKeyed)
            {
                DrawKeyedLookResetRow(view, target);
            }

            DrawEyesPosSection(view, target, timeline);
        }

        /// <summary>
        /// 瞳位置・瞳サイズ。旧レイヤー編集ウィンドウから移設。
        /// 書き込み先は瞳の Transform (MaidCache 経由) で「視線をキー化」には依らないため、
        /// キー化の有無に関わらず出す
        /// </summary>
        private void DrawEyesPosSection(GUIView view, Maid target, MTEP.TimelineData timeline)
        {
            var maidCache = MTEP.MaidManager.instance.GetMaidCache(target);
            if (maidCache == null)
            {
                return;
            }

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.DrawLabel("瞳位置", -1, ROW_HEIGHT);

            if (timeline == null)
            {
                view.DrawLabel("タイムライン未読込のため瞳位置はキー化されません",
                    -1, ROW_HEIGHT, textColor: Color.gray);
            }

            _eyesPosRowDrawer.DrawEyesPosImage(view, maidCache);

            foreach (var eyesType in EyesPosRowDrawer.AllEyesTypes)
            {
                _eyesPosRowDrawer.DrawLabeledEyesSliderRows(view, maidCache, eyesType);
            }
        }

        /// <summary>
        /// 顔向き左右/上下。キー化中はタイムラインの顔向きキー (MaidCache) を編集し、
        /// 注視先が手動のときだけ効く。キー化していなければ SE の顔向きを編集し、
        /// 向け先が「方向指定」のときだけ効く
        /// </summary>
        private void DrawLookDirectionSliders(
            GUIView view, Maid target, MaidLookMode mode, bool isKeyed)
        {
            view.AddSpace(5);

            if (isKeyed)
            {
                var maidCache = MTEP.MaidManager.instance.GetMaidCache(target);
                if (maidCache != null)
                {
                    _timelineLookRowDrawer.DrawLookDirectionRows(view, maidCache, LABEL_WIDTH);
                }
                return;
            }

            // DrawSliderValue は内部でボタン等を描き、その EndEnabled が GUI.enabled を
            // 基準値へ戻してしまう。BeginEnabled は入れ子にできないため、
            // 基準値そのものを動かす SetEnabled で囲む
            view.SetEnabled(mode == MaidLookMode.方向指定);
            DrawLookSlider(view, target, "顔向き左右", lookController.GetLookX(target),
                value => lookController.SetLook(target, value, lookController.GetLookY(target)));
            DrawLookSlider(view, target, "顔向き上下", lookController.GetLookY(target),
                value => lookController.SetLook(target, lookController.GetLookX(target), value));
            view.SetEnabled(true);
        }

        /// <summary>
        /// 視線をキー化するか (タイムラインの「顔/瞳の固定化」)。
        /// タイムライン全体の設定なので、対象メイドを問わず同じ値を出す
        /// </summary>
        private void DrawLookKeyToggleRow(GUIView view, MTEP.TimelineData timeline)
        {
            if (timeline == null)
            {
                view.DrawLabel("タイムライン未読込のため視線はキー化されません",
                    -1, ROW_HEIGHT, textColor: Color.gray);
                return;
            }

            view.DrawToggle("視線をキー化", timeline.useHeadKey, 130, ROW_HEIGHT,
                value => timeline.useHeadKey = value);
        }

        /// <summary>
        /// 向け先の行。キー化の有無で選択肢と書き込み先だけが変わり、
        /// 行の構成 (向け先 → 対象の指定) は変えない
        /// </summary>
        private void DrawLookTargetRows(
            GUIView view, Maid target, MaidLookMode mode, bool isKeyed)
        {
            if (isKeyed)
            {
                var maidCache = MTEP.MaidManager.instance.GetMaidCache(target);
                if (maidCache == null)
                {
                    view.DrawLabel("タイムライン側の対象メイドが見つかりません",
                        -1, ROW_HEIGHT, textColor: Color.yellow);
                    return;
                }
                _timelineLookRowDrawer.DrawLookAtTargetRows(
                    view, maidCache, LABEL_WIDTH, ROW_HEIGHT);
                return;
            }

            _lookModeComboBox.items = UnkeyedLookModes;
            _lookModeComboBox.currentIndex = UnkeyedLookModes.IndexOf(mode);
            _lookModeComboBox.onSelected = (newMode, _) =>
            {
                HistoryManager.instance.BeforeEdit(target, HistoryScope.Pose, "視線の向け先");
                lookController.SetMode(target, newMode);
            };
            DrawLabeledComboBox("向け先", _lookModeComboBox);

            // 対象の指定はキー化中の行 (向け先の直下に対象が続く) と並びを揃える
            if (mode == MaidLookMode.メイド)
            {
                DrawLookMaidRows(view, target);
            }
            else if (mode == MaidLookMode.モデル)
            {
                DrawLookModelRow(view, target);
            }
            else if (mode == MaidLookMode.オブジェクト)
            {
                DrawLookObjectRows(view, target);
            }
        }

        /// <summary>目線種別。顔/瞳の追従トグルをまとめて切り替えるプリセット</summary>
        private void DrawEyeMoveTypeRow(GUIView view, MTEP.TimelineData timeline)
        {
            if (timeline == null)
            {
                return;
            }

            _eyeMoveTypeComboBox.currentIndex = (int) timeline.eyeMoveType;
            DrawLabeledComboBox("メイド目線", _eyeMoveTypeComboBox);
        }

        /// <summary>顔・目の追従トグル。向け先と独立で、頭ボーンのドラッグでも落ちる</summary>
        private void DrawHeadToCamRow(GUIView view, Maid target)
        {
            var body = target.body0;
            view.BeginHorizontal();
            {
                // BeginEnabled はネスト非対応のため、無効化は各 DrawToggle の enabled 引数で個別に指定する
                view.DrawToggle("顔を向ける", body != null && body.boHeadToCam, 95, ROW_HEIGHT,
                    body != null, value =>
                    {
                        // カメラ追従は Pose スナップショットに含まれるため Pose で記録する
                        HistoryManager.instance.BeforeEdit(target, HistoryScope.Pose, "顔を向ける");
                        body.boHeadToCam = value;
                    });
                view.DrawToggle("目を向ける", body != null && body.boEyeToCam, 95, ROW_HEIGHT,
                    body != null, value =>
                    {
                        HistoryManager.instance.BeforeEdit(target, HistoryScope.Pose, "目を向ける");
                        body.boEyeToCam = value;
                    });
            }
            view.EndLayout();
        }

        /// <summary>メイドモードの注視対象と部位。キー化中は同じ行を TimelineLookRowDrawer が描く</summary>
        private void DrawLookMaidRows(GUIView view, Maid target)
        {
            var maids = MTEUtils.GetReadyMaidList();
            var targetMaid = lookController.GetTargetMaid(target);

            _lookMaidComboBox.items = maids;
            // 未選択のときは currentIndex が -1 になりボタン文字列が決まらないため、既定名で埋める
            // (先頭のメイドへ丸めると、選んでいないメイドが選択済みに見えてしまう)
            _lookMaidComboBox.defaultName = targetMaid == null ? "未選択" : null;
            _lookMaidComboBox.currentIndex = maids.IndexOf(targetMaid);
            _lookMaidComboBox.onSelected = (selected, _) =>
            {
                HistoryManager.instance.BeforeEdit(target, HistoryScope.Pose, "注視対象の指定");
                lookController.SetMaidTarget(
                    target, selected, lookController.GetMaidPointType(target));
            };
            DrawLabeledComboBox("メイド", _lookMaidComboBox);

            _lookMaidPointComboBox.currentIndex = (int) lookController.GetMaidPointType(target);
            _lookMaidPointComboBox.onSelected = (pointType, _) =>
            {
                HistoryManager.instance.BeforeEdit(target, HistoryScope.Pose, "注視ポイントの指定");
                lookController.SetMaidTarget(
                    target, lookController.GetTargetMaid(target), pointType);
            };
            DrawLabeledComboBox("ポイント", _lookMaidPointComboBox);
        }

        /// <summary>モデルモードの注視対象。キー化中は同じ行を TimelineLookRowDrawer が描く</summary>
        private void DrawLookModelRow(GUIView view, Maid target)
        {
            var models = MTEP.StudioModelManager.instance.models;
            var modelName = lookController.GetTargetModelName(target);

            _lookModelComboBox.items = models;
            // 未選択・モデルが消えたときは currentIndex が -1 になりボタン文字列が決まらないため既定名で埋める
            var index = models.FindIndex(model => model.name == modelName);
            _lookModelComboBox.defaultName = index >= 0 ? null : "未選択";
            _lookModelComboBox.currentIndex = index;
            _lookModelComboBox.onSelected = (selected, _) =>
            {
                HistoryManager.instance.BeforeEdit(target, HistoryScope.Pose, "注視対象の指定");
                lookController.SetModelTarget(target, selected.name);
            };
            DrawLabeledComboBox("モデル", _lookModelComboBox);
        }

        /// <summary>オブジェクトモードの注視対象。Hierarchy の選択をそのまま指定できる</summary>
        private void DrawLookObjectRows(GUIView view, Maid target)
        {
            var current = lookController.GetTarget(target);
            var selected = SelectionManager.instance.selectedObject;
            view.BeginHorizontal();
            {
                view.DrawLabel("注視対象", LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);
                view.DrawLabel(current != null ? current.name : "未設定", 150, ROW_HEIGHT);
            }
            view.EndLayout();

            // 選択が無いときは押しても何も起きないため無効化する
            if (view.DrawButton("選択中のオブジェクトを指定", 200, ROW_HEIGHT, selected != null))
            {
                HistoryManager.instance.BeforeEdit(target, HistoryScope.Pose, "注視対象の指定");
                lookController.SetTarget(target, selected.transform);
            }
        }

        /// <summary>キー化される視線の初期化。書き込み先は MaidCache のため履歴は記録しない</summary>
        private void DrawKeyedLookResetRow(GUIView view, Maid target)
        {
            var maidCache = MTEP.MaidManager.instance.GetMaidCache(target);
            if (maidCache == null)
            {
                return;
            }

            if (view.DrawButton("タイムライン視線を初期化", 190, ROW_HEIGHT))
            {
                maidCache.lookDirection = Vector2.zero;
                maidCache.lookAtTargetType = MTEP.LookAtTargetType.None;
            }
        }

        /// <summary>顔向きスライダー 1 本。値域はフォトモードに合わせて -1〜1</summary>
        private void DrawLookSlider(
            GUIView view, Maid target, string label, float value, Action<float> onChanged)
        {
            view.DrawSliderValue(new GUIView.SliderOption
            {
                label = label,
                labelWidth = LABEL_WIDTH,
                width = -1,
                min = -1f,
                max = 1f,
                step = 0.01f,
                defaultValue = 0f,
                value = value,
                onChanged = newValue =>
                {
                    HistoryManager.instance.BeforeEdit(target, HistoryScope.Pose, "視線: " + label);
                    onChanged(newValue);
                },
            });
        }

        /// <summary>
        /// プリセットタブ。カテゴリ選択 (マイ表情 + フォトモードのカテゴリ) と
        /// プリセットボタンの一覧を描画する
        /// </summary>
        private void DrawPresetContent(GUIView view, Maid target)
        {
            DrawPresetCategoryRow(view);

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);
            if (_presetCategory == MY_FACE_CATEGORY)
            {
                DrawUserPresetList(view, target);
            }
            else
            {
                DrawPhotoFacePresetList(view, target);
            }
            view.EndScrollView();
        }

        /// <summary>カテゴリ選択行。マイ表情 + フォトモードの表情カテゴリ一覧</summary>
        private void DrawPresetCategoryRow(GUIView view)
        {
            view.BeginHorizontal();
            {
                view.DrawLabel("カテゴリ", LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);

                if (_presetCategories == null)
                {
                    _presetCategories = new List<string> { MY_FACE_CATEGORY };
                    if (EnsurePhotoFaceDataLoaded())
                    {
                        foreach (var pair in PhotoFaceData.category_list)
                        {
                            _presetCategories.Add(pair.Key);
                        }
                    }
                }

                // 一覧の再構築で選択中カテゴリが消えたら、表示と描画がずれないよう選択も戻す
                if (!_presetCategories.Contains(_presetCategory))
                {
                    _presetCategory = MY_FACE_CATEGORY;
                }

                _presetCategoryComboBox.items = _presetCategories;
                _presetCategoryComboBox.currentIndex = _presetCategories.IndexOf(_presetCategory);
                _presetCategoryComboBox.onSelected = (name, _) => _presetCategory = name;
                _presetCategoryComboBox.DrawButton(view);
            }
            view.EndLayout();
        }

        /// <summary>
        /// フォトモードの内蔵プリセット一覧の読み込み。冪等。
        /// 失敗しても表情ウィンドウ全体は使えるよう、例外はログに留める
        /// </summary>
        private static bool EnsurePhotoFaceDataLoaded()
        {
            if (_photoFaceDataLoadFailed)
            {
                return false;
            }

            try
            {
                PhotoFaceData.Create();
                return PhotoFaceData.data != null;
            }
            catch (System.Exception e)
            {
                MTEUtils.LogException(e);
                _photoFaceDataLoadFailed = true;
                return false;
            }
        }

        /// <summary>内蔵プリセットをボタンで列挙し、押したら即適用する</summary>
        private void DrawPhotoFacePresetList(GUIView view, Maid target)
        {
            List<PhotoFaceData> presets;
            if (!EnsurePhotoFaceDataLoaded() ||
                !PhotoFaceData.category_list.TryGetValue(_presetCategory, out presets))
            {
                view.DrawLabel("このカテゴリに表情はありません", -1, ROW_HEIGHT);
                return;
            }

            foreach (var data in presets)
            {
                if (view.DrawButton(data.name, -1, ROW_HEIGHT))
                {
                    HistoryManager.instance.BeforeEdit(target, HistoryScope.Face,
                        "表情プリセット: " + data.name);
                    MaidFaceMorphController.ApplyPhotoFacePreset(target, data);
                }
            }
        }

        /// <summary>
        /// ユーザー保存表情の一覧。適用・削除ボタン付きの保存済み一覧を描画する。
        /// 保存ボタンはどのタブからも押せるようヘッダー側にある
        /// </summary>
        private void DrawUserPresetList(GUIView view, Maid target)
        {
            if (_userPresetNames == null)
            {
                _userPresetNames = MaidFacePresetManager.GetPresetNames();
            }

            if (_userPresetNames.Count == 0)
            {
                view.DrawLabel("保存された表情はありません", -1, ROW_HEIGHT);
                return;
            }

            const int deleteButtonWidth = 50;
            // スクロールバー分は viewRect が既に差し引かれているため、削除ボタンと間隔だけ引く
            var nameButtonWidth = view.viewRect.width - view.padding.x * 2
                - deleteButtonWidth - view.margin;

            foreach (var presetName in _userPresetNames)
            {
                view.BeginHorizontal();
                {
                    if (view.DrawButton(presetName, nameButtonWidth, ROW_HEIGHT))
                    {
                        HistoryManager.instance.BeforeEdit(target, HistoryScope.Face,
                            "表情プリセット: " + presetName);
                        MaidFacePresetManager.LoadPreset(target, presetName);
                    }

                    if (view.DrawButton("削除", deleteButtonWidth, ROW_HEIGHT))
                    {
                        DialogPopupWindow.ShowConfirmDialog(
                            "表情「" + presetName + "」を削除しますか？",
                            () =>
                            {
                                MaidFacePresetManager.DeletePreset(presetName);
                                _userPresetNames = null;
                            });
                    }
                }
                view.EndLayout();
            }
        }

        /// <summary>
        /// ポップアップで確定した名前で保存し、一覧を更新する。
        /// 名前検証と上書き確認はポップアップ側で済んでいる
        /// </summary>
        private void SavePreset(Maid target, string presetName)
        {
            // ポップアップ表示中に操作対象が変わっていたら、別メイドの表情を保存しないよう中止する
            if (maidManager.targetMaid != target)
            {
                DialogPopupWindow.ShowDialog("操作対象が変わったため保存を中止しました");
                return;
            }

            MaidFacePresetManager.SavePreset(target, presetName);
            _userPresetNames = MaidFacePresetManager.GetPresetNames();
        }
    }
}
