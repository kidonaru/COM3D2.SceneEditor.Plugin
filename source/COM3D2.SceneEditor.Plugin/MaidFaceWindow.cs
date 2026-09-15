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

        /// <summary>タイムライン視線の行描画。Inspector の項目表示と共有する</summary>
        private readonly TimelineLookRowDrawer _timelineLookRowDrawer = new TimelineLookRowDrawer();

        /// <summary>瞳位置・瞳サイズの行描画。Inspector の項目表示と共有する</summary>
        private readonly EyesPosRowDrawer _eyesPosRowDrawer = new EyesPosRowDrawer();

        /// <summary>
        /// 目線種別。顔/瞳の追従フラグを決める設定で、書き込み先はタイムライン全体の設定。
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

            // 視線タブは瞳レイヤー、それ以外（表情・プリセット）は表情レイヤーへ記録される。
            // プリセット適用もモーフ値を直接書くのでスライダーと同じ扱い
            var layerType = _tab == FaceTab.視線
                ? typeof(MTEP.EyesTimelineLayer)
                : typeof(MTEP.MorphTimelineLayer);
            TimelineLayerGate.Begin(view, layerType, target, ROW_HEIGHT);

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
                // 表情レイヤー再生中はキー値が実効値になる。再生中の変更が次フレームで戻るのは
                // モーフ行と同じ挙動なので、ここでは無効化しない
                var isForceOverride = MaidFaceMorphController.IsForceOverride(target);
                view.DrawToggle("強制上書き", isForceOverride, 95, ROW_HEIGHT,
                    newIsForceOverride =>
                    {
                        HistoryManager.instance.BeforeEdit(target, HistoryScope.Face, "強制上書き切替");
                        MaidFaceMorphController.SetForceOverride(target, newIsForceOverride);
                    });

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
        /// 視線タブ。向け先 (trsLookTarget) の所有者は MaidLookController だが、
        /// 入口はタイムラインの注視先キー (MaidCache) に一本化しており、
        /// MaidLookBridge がそれをコントローラへ流す。
        /// 顔・瞳の追従フラグは「メイド目線」が決めるため、ここでは個別に出さない
        /// </summary>
        private void DrawLookContent(GUIView view, Maid target)
        {
            var timeline = MTEP.TimelineManager.instance.timeline;

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            // 最後の要素なので高さ -1（残り全部）でウィンドウの伸縮に追従させる
            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            DrawTimelineLookSection(view, target, timeline);
            DrawEyesPosSection(view, target, timeline);

            view.EndScrollView();
        }

        /// <summary>
        /// メイド目線・注視先・顔向きキー。書き込み先はタイムライン設定と MaidCache で、
        /// タイムライン未読込のときは編集できないため案内だけ出す
        /// </summary>
        private void DrawTimelineLookSection(GUIView view, Maid target, MTEP.TimelineData timeline)
        {
            if (timeline == null)
            {
                view.DrawLabel("タイムライン未読込のため視線は編集できません",
                    -1, ROW_HEIGHT, textColor: Color.gray);
                return;
            }

            var maidCache = MTEP.MaidManager.instance.GetMaidCache(target);
            if (maidCache == null)
            {
                // ここへ来るのはタイムライン読込中に限られ、その場合は
                // タブ冒頭の TimelineLayerGate が同じ案内を出しているので重ねない
                return;
            }

            DrawEyeMoveTypeRow(view, timeline);
            _timelineLookRowDrawer.DrawLookAtTargetRows(view, maidCache, LABEL_WIDTH, ROW_HEIGHT);

            view.AddSpace(5);
            _timelineLookRowDrawer.DrawLookDirectionRows(view, maidCache, LABEL_WIDTH);

            DrawKeyedLookResetRow(view, maidCache);
        }

        /// <summary>
        /// 瞳位置・瞳サイズ。
        /// 書き込み先は瞳の Transform (MaidCache 経由) で、注視先やメイド目線には依らない
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
        /// 目線種別。顔・瞳の追従とそらしをまとめて決める (MaidLookBridge.ApplyEyeMoveType)。
        /// 「無し」がタイムラインに視線を動かさせない指定になる
        /// </summary>
        private void DrawEyeMoveTypeRow(GUIView view, MTEP.TimelineData timeline)
        {
            _eyeMoveTypeComboBox.currentIndex = (int) timeline.eyeMoveType;
            DrawLabeledComboBox("メイド目線", _eyeMoveTypeComboBox);
        }

        /// <summary>キー化される視線の初期化。書き込み先は MaidCache のため履歴は記録しない</summary>
        private void DrawKeyedLookResetRow(GUIView view, MTEP.MaidCache maidCache)
        {
            if (view.DrawButton("タイムライン視線を初期化", 190, ROW_HEIGHT))
            {
                maidCache.lookDirection = Vector2.zero;
                maidCache.lookAtTargetType = MTEP.LookAtTargetType.None;
            }
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
