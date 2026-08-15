using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

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

        private readonly GUIComboBox<MaidLookMode> _lookModeComboBox = new GUIComboBox<MaidLookMode>
        {
            getName = (mode, _) => mode.ToString(),
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 100),
        };

        /// <summary>視線モードの選択肢。列挙の再生成を避けて使い回す</summary>
        private static readonly List<MaidLookMode> LOOK_MODES = new List<MaidLookMode>
        {
            MaidLookMode.カメラ,
            MaidLookMode.マウス,
            MaidLookMode.方向指定,
            MaidLookMode.オブジェクト,
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
                // 表示は「編集した表情を固定するか」の視点に揃えるため、まばたきの反転として扱う
                var isForceOverride = !MaidFaceMorphController.GetMabataki(target);
                view.DrawToggle("強制上書き", isForceOverride, 95, ROW_HEIGHT,
                    newIsForceOverride =>
                    {
                        HistoryManager.instance.BeforeEdit(target, HistoryScope.Face, "強制上書き切替");
                        MaidFaceMorphController.SetMabataki(target, !newIsForceOverride);
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
                var value = MaidFaceMorphController.GetMorphValue(target, def);

                if (def.isToggle)
                {
                    view.DrawToggle(def.displayName, value >= 0.5f, 150, ROW_HEIGHT, newValue =>
                    {
                        HistoryManager.instance.BeforeEdit(target, HistoryScope.Face,
                            "表情: " + def.displayName);
                        // まばたき中は編集値が毎フレーム上書きされるため、編集開始で自動的に止める
                        MaidFaceMorphController.SetMabataki(target, false);
                        MaidFaceMorphController.SetMorphValue(target, def, newValue ? 1f : 0f);
                    });
                }
                else
                {
                    view.DrawSliderValue(new GUIView.SliderOption
                    {
                        label = def.displayName,
                        labelWidth = LABEL_WIDTH,
                        width = -1,
                        min = 0f,
                        max = 1f,
                        step = 0.01f,
                        defaultValue = 0f,
                        value = value,
                        onChanged = newValue =>
                        {
                            HistoryManager.instance.BeforeEdit(target, HistoryScope.Face,
                                "表情: " + def.displayName);
                            // まばたき中は編集値が毎フレーム上書きされるため、編集開始で自動的に止める
                            MaidFaceMorphController.SetMabataki(target, false);
                            MaidFaceMorphController.SetMorphValue(target, def, newValue);
                        },
                    });
                }
            }

            view.EndScrollView();
        }

        /// <summary>
        /// 視線タブ。向け先の選択と顔向き左右/上下を描画する。
        /// 顔・目のトグルは向け先と独立で、頭ボーンのドラッグでも落ちる
        /// </summary>
        private void DrawLookContent(GUIView view, Maid target)
        {
            var body = target.body0;
            var mode = lookController.GetMode(target);

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            view.BeginHorizontal();
            {
                view.DrawLabel("向け先", LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);

                _lookModeComboBox.items = LOOK_MODES;
                _lookModeComboBox.currentIndex = LOOK_MODES.IndexOf(mode);
                _lookModeComboBox.onSelected = (newMode, _) =>
                {
                    HistoryManager.instance.BeforeEdit(target, HistoryScope.Pose, "視線の向け先");
                    lookController.SetMode(target, newMode);
                };
                _lookModeComboBox.DrawButton(view);
            }
            view.EndLayout();

            view.BeginHorizontal();
            {
                // 頭部をドラッグすると上書きされるため、追従は自動的に解除される。
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

            view.AddSpace(5);

            // DrawSliderValue は内部でボタン等を描き、その EndEnabled が GUI.enabled を
            // 基準値へ戻してしまう。BeginEnabled は入れ子にできないため、
            // 基準値そのものを動かす SetEnabled で囲む
            view.SetEnabled(mode == MaidLookMode.方向指定);
            DrawLookSlider(view, target, "顔向き左右", lookController.GetLookX(target),
                value => lookController.SetLook(target, value, lookController.GetLookY(target)));
            DrawLookSlider(view, target, "顔向き上下", lookController.GetLookY(target),
                value => lookController.SetLook(target, lookController.GetLookX(target), value));
            view.SetEnabled(true);

            if (mode != MaidLookMode.オブジェクト)
            {
                return;
            }

            view.AddSpace(5);

            var current = lookController.GetTarget(target);
            var selected = SelectionManager.instance.selectedObject;
            view.BeginHorizontal();
            {
                view.DrawLabel("注視対象", LABEL_WIDTH, ROW_HEIGHT, style: GUIView.gsLabelRight);
                view.DrawLabel(current != null ? current.name : "未設定", 150, ROW_HEIGHT);
            }
            view.EndLayout();

            // Hierarchy の選択をそのまま注視対象にできるようにする。
            // 選択が無いときは押しても何も起きないため無効化する
            if (view.DrawButton("選択中のオブジェクトを指定", 200, ROW_HEIGHT, selected != null))
            {
                HistoryManager.instance.BeforeEdit(target, HistoryScope.Pose, "注視対象の指定");
                lookController.SetTarget(target, selected.transform);
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
