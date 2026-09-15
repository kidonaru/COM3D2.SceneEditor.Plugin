using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムライン操作ウィンドウ。TimelineWindow 上段にあった操作パネル
    /// (ファイル/フレーム操作/キーフレーム/範囲操作/操作対象/表示トグル) を独立させたもの
    /// </summary>
    public class TimelineControlWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903391;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "タイムライン操作";
        protected override int minWidth => 300;
        // 横並びを折り返すウィンドウなので、ヘッダー + 1 行分まで縮められる
        protected override int minHeight => 60;

        private static TimelineControlWindow _instance = null;
        public static TimelineControlWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TimelineControlWindow();
                }
                return _instance;
            }
        }

        private static MTEP.SceneEditorHack studioHack => MTEP.SceneEditorHack.instance;
        private static MTEP.MaidManager maidManager => MTEP.MaidManager.instance;
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.TimelineData timeline => timelineManager.timeline;
        private static MTEP.ITimelineLayer currentLayer => timelineManager.currentLayer;
        private static MTEP.StudioModelManager modelManager => MTEP.StudioModelManager.instance;
        private static MTEP.Config timelineConfig => MTEP.ConfigManager.instance.config;

        private static string anmName
        {
            get => timeline.anmName;
            set => timeline.anmName = value;
        }

        private readonly GUIView _view = new GUIView();

        /// <summary>範囲操作の開始フレーム。TimelineWindow が範囲選択表示に参照する</summary>
        public int selectStartFrameNo = 0;
        /// <summary>範囲操作の終了フレーム。TimelineWindow が範囲選択表示に参照する</summary>
        public int selectEndFrameNo = 0;

        private TimelineControlWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.timelineControlPosX;
            y = config.timelineControlPosY;
            width = config.timelineControlWidth;
            height = config.timelineControlHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.timelineControlPosX = x;
            config.timelineControlPosY = y;
            config.timelineControlWidth = width;
            config.timelineControlHeight = height;
        }

        public override bool savedVisible
        {
            get => config.timelineControlVisible;
            set => config.timelineControlVisible = value;
        }

        private enum FileMenuType
        {
            New,
            Unload,
            OutputAnm,
            OutputImage,
        }

        private readonly GUIComboBox<FileMenuType> fileMenuComboBox = new GUIComboBox<FileMenuType>
        {
            defaultName = "ファイル",
            items = new List<FileMenuType>
            {
                FileMenuType.New,
                FileMenuType.Unload,
                FileMenuType.OutputAnm,
                FileMenuType.OutputImage,
            },
            getName = (type, index) =>
            {
                switch (type)
                {
                    case FileMenuType.New:
                        return "新規作成";
                    case FileMenuType.Unload:
                        return "アンロード";
                    case FileMenuType.OutputAnm:
                        return "アニメ出力";
                    case FileMenuType.OutputImage:
                        return "連番画像出力";
                    default:
                        return "";
                }
            },
            getEnabled = (type, index) =>
            {
                switch (type)
                {
                    case FileMenuType.Unload:
                        return timelineManager.timeline != null;
                    case FileMenuType.OutputAnm:
                        return timelineManager.IsValidData();
                    case FileMenuType.OutputImage:
                        return timelineManager.IsValidData() && !timelineManager.isOutputtingImage;
                    default:
                        return true;
                }
            },
            onSelected = (type, index) =>
            {
                switch (type)
                {
                    case FileMenuType.New:
                        timelineManager.CreateNewTimeline();
                        break;
                    case FileMenuType.Unload:
                        MTEUtils.ShowConfirmDialog(
                            "タイムラインをアンロードしますか？\n未保存の変更は失われます",
                            () => timelineManager.UnloadTimeline());
                        break;
                    case FileMenuType.OutputAnm:
                        timelineManager.OutputAnm();
                        break;
                    case FileMenuType.OutputImage:
                        timelineManager.OutputImage();
                        break;
                }
            },
            showArrow = false,
            buttonSize = new Vector2(60, 20),
        };

        private readonly GUIComboBox<MTEP.MaidCache> _maidComboBox = new GUIComboBox<MTEP.MaidCache>
        {
            getName = (maidCache, _) => maidCache == null ? "未選択" : maidCache.fullName,
            onSelected = (maidCache, index) =>
            {
                maidManager.ChangeMaid(maidCache.maid);
            },
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        private const float ROW_HEIGHT = 20f;

        /// <summary>アイコンを読み込めなかったときの文字トグルの幅 (異常系のみなので最長ラベルに合わせた 1 種類で済ます)</summary>
        private const float TEXT_TOGGLE_WIDTH = 100f;

        /// <summary>アイコントグルの内側余白 (ボタン枠と絵の間)</summary>
        private const float ICON_TOGGLE_OFFSET = 4f;

        /// <summary>フレーム操作アイコンボタンの幅と内側余白</summary>
        private const float FRAME_BUTTON_WIDTH = 25f;
        private const float FRAME_ICON_OFFSET = 4f;

        /// <summary>現在フレーム欄のラベル幅と入力幅</summary>
        private const float FRAME_LABEL_WIDTH = 50f;
        private const float FRAME_FIELD_WIDTH = 50f;

        /// <summary>自動登録 ON の色。録画中を連想させる赤にして他トグルと区別する</summary>
        private static readonly Color AUTO_KEY_ON_COLOR = new Color(1f, 0.3f, 0.3f);

        /// <summary>外周の余白。既定値より詰めて 1 行に並ぶ要素数を稼ぐ</summary>
        private static readonly Vector2 CONTENT_PADDING = new Vector2(3, 3);

        /// <summary>グループ見出しラベルの幅</summary>
        private const float GROUP_LABEL_WIDTH = 80f;

        /// <summary>文字数が少ない見出しの幅 (範囲操作)</summary>
        private const float SHORT_LABEL_WIDTH = 55f;

        /// <summary>再生速度の既定値 (R ボタンで戻す)</summary>
        private const float DEFAULT_ANM_SPEED = 1f;

        /// <summary>S / E ドラッグラベルと入力欄の幅</summary>
        private const float RANGE_LABEL_WIDTH = 15f;
        private const float RANGE_FIELD_WIDTH = 50f;

        /// <summary>状態メッセージの最小幅 (これを確保できなければ折り返す) と最大幅</summary>
        private const float STATUS_MESSAGE_MIN_WIDTH = 200f;
        private const float STATUS_MESSAGE_MAX_WIDTH = 400f;

        /// <summary>
        /// 次の要素が右端を超える場合に折り返す
        /// (TimelineTemplateWindow のテンプレボタンと同じ流儀)
        /// </summary>
        private static void WrapIfNeeded(GUIView view, float width)
        {
            if (view.currentPos.x + width > view.viewRect.width - view.padding.x * 2)
            {
                BeginNewLine(view);
            }
        }

        /// <summary>右端に余裕があっても次の要素を新しい行から並べる</summary>
        private static void BeginNewLine(GUIView view)
        {
            view.EndLayout();
            view.BeginHorizontal();
        }

        /// <summary>
        /// グループの見出しラベル。見出しだけが行末に取り残されないよう、
        /// 直後の要素 (nextWidth) と一緒に折り返す
        /// </summary>
        private static void DrawGroupLabel(GUIView view, string label, float nextWidth, float labelWidth = GROUP_LABEL_WIDTH)
        {
            WrapIfNeeded(view, labelWidth + GUIView.defaultMargin + nextWidth);
            view.DrawLabel(label, labelWidth, ROW_HEIGHT);
        }

        protected override void DrawContent()
        {
            DrawBody();

            // 早期 return してもコンボのポップアップ処理を飛ばさないよう本体と分けて必ず呼ぶ
            ComboBoxPopupWindow.instance.ProcessFocus(_view, this);
        }

        private void DrawBody()
        {
            var local = ToLocalRect(contentRect);
            var view = _view;

            view.Init(local);

            // 要素数が多いウィンドウなので、旧レイアウトと同じく外周の余白を詰める
            view.padding = CONTENT_PADDING;

            if (studioHack == null)
            {
                view.DrawLabel("シーンが有効ではありません", -1, ROW_HEIGHT, Color.yellow);
                return;
            }

            view.SetEnabled(view.focusedComboBox == null);

            view.BeginScrollView();
            {
                // 全要素を 1 つの横並びに流し込み、右端で折り返させる
                view.BeginHorizontal();
                {
                    DrawControls(view);
                }
                view.EndLayout();
            }
            // 折り返しレイアウトは既定の末尾余白だと空行が余るため 0 にする
            view.EndScrollView(0f);
        }

        private void DrawControls(GUIView view)
        {
            // 履歴の適用でタイムラインやメイドが変わるため、有効状態の判定より先に処理する
            DrawHistoryControls(view);

            var isMaidValid = maidManager.IsValid();

            var editEnabled = isMaidValid
                            && timeline != null
                            && maidManager.maid != null;

            DrawFileMenu(view, editEnabled);
            DrawStatusMessage(view, isMaidValid);

            if (!editEnabled)
            {
                return;
            }

            DrawFrameControls(view);
            DrawKeyFrameControls(view);
            DrawRangeControls(view);
            DrawTargetMaidControls(view);
            DrawToggles(view);
        }

        private void DrawHistoryControls(GUIView view)
        {
            var history = HistoryManager.instance;
            WrapIfNeeded(view, (FRAME_BUTTON_WIDTH + view.margin) * 2);
            if (DrawFrameButton(view, ToolbarIcons.Kind.Undo, "戻る",
                "戻る (" + config.GetKeyName(KeyBindType.Undo) + ")", history.canUndo))
            {
                history.Undo();
            }
            if (DrawFrameButton(view, ToolbarIcons.Kind.Redo, "進む",
                "進む (" + config.GetKeyName(KeyBindType.Redo) + ")", history.canRedo))
            {
                history.Redo();
            }
        }

        private void DrawFileMenu(GUIView view, bool editEnabled)
        {
            WrapIfNeeded(view, 60);
            fileMenuComboBox.currentIndex = -1;
            fileMenuComboBox.DrawButton(view);

            WrapIfNeeded(view, 60);
            if (view.DrawButton("セーブ", 60, ROW_HEIGHT, editEnabled))
            {
                OnSaveClicked();
            }

            // MTE のロードボタンと同様に開く動作のみ (他ボタンと違いトグルしない)
            WrapIfNeeded(view, 60);
            if (view.DrawButton("ロード", 60, ROW_HEIGHT))
            {
                OnLoadClicked();
            }
        }

        /// <summary>
        /// スクロールビューの EndScrollView を飛ばさないよう、
        /// 中断する早期 return はハンドラ側に閉じ込める (ロードも同様)
        /// </summary>
        private void OnSaveClicked()
        {
            if (!timelineManager.IsValidData())
            {
                MTEUtils.ShowDialog(timelineManager.errorMessage);
                return;
            }

            // 同名の別タイムラインを気付かず潰さないよう、上書きになるときだけ確認する
            if (System.IO.File.Exists(timeline.timelinePath))
            {
                MTEUtils.ShowConfirmDialog(
                    $"タイムライン「{anmName}」は既にあります\n上書きしますか?",
                    SaveTimelineAndReload);
                return;
            }

            SaveTimelineAndReload();
        }

        private static void SaveTimelineAndReload()
        {
            timelineManager.SaveTimeline();

            // 保存したタイムラインをサムネ付きで一覧へ出す
            TimelineLoadManager.Reload();
        }

        private void OnLoadClicked()
        {
            var loadWindow = TimelineLoadWindow.instance;
            if (!loadWindow.isShowWnd)
            {
                WindowManager.ToggleWindowVisible(loadWindow);
            }

            // ドッキング中は表示済みでも背面タブのままになりうるので前面へ出す
            if (loadWindow.group != null)
            {
                loadWindow.group.SetActive(loadWindow);
            }
        }

        /// <summary>状態メッセージ。狭いウィンドウでもはみ出さないよう残り幅に収める</summary>
        private void DrawStatusMessage(GUIView view, bool isMaidValid)
        {
            WrapIfNeeded(view, STATUS_MESSAGE_MIN_WIDTH);

            var remainWidth = view.viewRect.width - view.currentPos.x - view.padding.x * 2;
            var width = Mathf.Min(STATUS_MESSAGE_MAX_WIDTH, remainWidth);

            if (!isMaidValid)
            {
                view.DrawLabel(maidManager.errorMessage, width, ROW_HEIGHT, Color.yellow);
            }
            else if (!timelineManager.IsValidData())
            {
                view.DrawLabel(timelineManager.errorMessage, width, ROW_HEIGHT, Color.yellow);
            }
            else if (MTEP.SceneEditorHack.isPoseEditing)
            {
                var keyName = config.GetKeyName(KeyBindType.AddKeyFrame);
                view.DrawLabel("[" + keyName + "]キーでキーフレームを登録します", width, ROW_HEIGHT, Color.white);
            }
            else
            {
                var keyName = config.GetKeyName(KeyBindType.EditModeToggle);
                view.DrawLabel("[" + keyName + "]キーで編集モードに切り替えます", width, ROW_HEIGHT, Color.white);
            }
        }

        private void DrawFrameControls(GUIView view)
        {
            WrapIfNeeded(view, 260);
            view.DrawTextField("アニメ名", 60, anmName, 260, ROW_HEIGHT, newText => anmName = newText);

            WrapIfNeeded(view, 75 + GUIView.defaultMargin + 60);
            view.DrawDragIntField(new GUIView.DragIntFieldOption
            {
                label = "最終フレーム",
                labelWidth = 75,
                value = timeline.maxFrameNo,
                fieldWidth = 60,
                height = ROW_HEIGHT,
                onChanged = value => timelineManager.SetMaxFrameNo(value),
            });

            var newFrameNo = timelineManager.currentFrameNo;

            // 現在フレームはラベルドラッグでも動かせる
            WrapIfNeeded(view, FRAME_LABEL_WIDTH + GUIView.defaultMargin + FRAME_FIELD_WIDTH);
            view.DrawDragIntField(new GUIView.DragIntFieldOption
            {
                label = "フレーム",
                labelWidth = FRAME_LABEL_WIDTH,
                value = newFrameNo,
                minValue = 0,
                maxValue = timeline.maxFrameNo,
                fieldWidth = FRAME_FIELD_WIDTH,
                height = ROW_HEIGHT,
                onChanged = value => newFrameNo = value,
            });

            // シークボタン群 (|< .< < ▶ > >. >|) は分断すると操作しにくいためまとめて折り返す
            WrapIfNeeded(view, FRAME_BUTTON_WIDTH * 7);
            view.margin = 0;
            {
                if (DrawFrameButton(view, ToolbarIcons.Kind.SkipStart, "|<", "先頭へ"))
                {
                    newFrameNo = 0;
                }
                if (DrawFrameRepeatButton(view, ToolbarIcons.Kind.PrevKey, ".<", "前のキーへ"))
                {
                    var prevFrame = timelineManager.GetPrevFrame(newFrameNo);
                    if (prevFrame != null)
                    {
                        newFrameNo = prevFrame.frameNo;
                    }
                }
                if (DrawFrameRepeatButton(view, ToolbarIcons.Kind.PrevFrame, "<", "前のフレームへ"))
                {
                    newFrameNo--;
                }

                // 再生/停止は列の中央
                if (currentLayer.isAnmPlaying)
                {
                    if (DrawFrameButton(view, ToolbarIcons.Kind.Pause, "■", "停止"))
                    {
                        timelineManager.Pause();
                    }
                }
                else
                {
                    if (DrawFrameButton(view, ToolbarIcons.Kind.Play, "▶", "再生"))
                    {
                        timelineManager.Play();
                    }
                }

                if (DrawFrameRepeatButton(view, ToolbarIcons.Kind.NextFrame, ">", "次のフレームへ"))
                {
                    newFrameNo++;
                }
                if (DrawFrameRepeatButton(view, ToolbarIcons.Kind.NextKey, ">.", "次のキーへ"))
                {
                    var nextFrame = timelineManager.GetNextFrame(newFrameNo);
                    if (nextFrame != null)
                    {
                        newFrameNo = nextFrame.frameNo;
                    }
                }
                if (DrawFrameButton(view, ToolbarIcons.Kind.SkipEnd, ">|", "最終へ"))
                {
                    newFrameNo = timeline.maxFrameNo;
                }
            }
            view.margin = GUIView.defaultMargin;

            if (newFrameNo != timelineManager.currentFrameNo)
            {
                TimelineKeyInput.SeekFrameWithScroll(newFrameNo);
            }

            WrapIfNeeded(view, 60 + GUIView.defaultMargin + 50 + GUIView.ResetButtonWidth);
            view.DrawDragFloatField(new GUIView.DragFloatFieldOption
            {
                label = "再生速度",
                labelWidth = 60,
                value = timelineManager.anmSpeed,
                minValue = 0f,
                maxValue = 2f,
                fieldWidth = 50,
                height = ROW_HEIGHT,
                onChanged = value => timelineManager.anmSpeed = value,
                onReset = () => timelineManager.anmSpeed = DEFAULT_ANM_SPEED,
            });
        }

        /// <summary>フレーム操作のアイコンボタン。アイコンを読み込めなければ文字ボタンで代替する</summary>
        private static bool DrawFrameButton(GUIView view, ToolbarIcons.Kind kind, string fallbackText, string tooltip, bool enabled = true)
        {
            var icon = ToolbarIcons.GetTexture(kind);
            if (icon == null)
            {
                return view.DrawButton(fallbackText, FRAME_BUTTON_WIDTH, ROW_HEIGHT, enabled);
            }
            return view.DrawTextureButton(icon, FRAME_BUTTON_WIDTH, ROW_HEIGHT, FRAME_ICON_OFFSET, enabled: enabled, tooltip: tooltip);
        }

        /// <summary>フレーム操作のアイコンリピートボタン (押し続けで連続移動)</summary>
        private static bool DrawFrameRepeatButton(GUIView view, ToolbarIcons.Kind kind, string fallbackText, string tooltip)
        {
            var icon = ToolbarIcons.GetTexture(kind);
            if (icon == null)
            {
                return view.DrawRepeatButton(fallbackText, FRAME_BUTTON_WIDTH, ROW_HEIGHT);
            }
            return view.DrawTextureRepeatButton(icon, FRAME_BUTTON_WIDTH, ROW_HEIGHT, FRAME_ICON_OFFSET, tooltip);
        }

        private void DrawKeyFrameControls(GUIView view)
        {
            DrawGroupLabel(view, "キーフレーム", 50);
            if (view.DrawButton("登録", 50, ROW_HEIGHT, MTEP.SceneEditorHack.isPoseEditing))
            {
                timelineManager.AddKeyFrameDiff();
            }

            WrapIfNeeded(view, 60);
            if (view.DrawButton("全登録", 60, ROW_HEIGHT))
            {
                currentLayer.AddKeyFrameAll();
            }

            WrapIfNeeded(view, 50);
            if (view.DrawButton("削除", 50, ROW_HEIGHT, timelineManager.HasSelected()))
            {
                timelineManager.RemoveSelectedFrame();
            }

            WrapIfNeeded(view, 60);
            if (view.DrawButton("コピー", 60, ROW_HEIGHT, timelineManager.HasSelected()))
            {
                timelineManager.CopyFramesToClipboard();
            }

            WrapIfNeeded(view, 60);
            if (view.DrawButton("ペースト", 60, ROW_HEIGHT))
            {
                timelineManager.PasteFramesFromClipboard(false);
            }

            WrapIfNeeded(view, 60);
            if (view.DrawButton("反転P", 60, ROW_HEIGHT))
            {
                timelineManager.PasteFramesFromClipboard(true);
            }

            WrapIfNeeded(view, 60);
            if (view.DrawButton("ポーズC", 60, ROW_HEIGHT))
            {
                timelineManager.CopyPoseToClipboard();
            }

            WrapIfNeeded(view, 60);
            if (view.DrawButton("ポーズP", 60, ROW_HEIGHT, MTEP.SceneEditorHack.isPoseEditing))
            {
                timelineManager.PastePoseFromClipboard();
            }
        }

        private void DrawRangeControls(GUIView view)
        {
            // 開始 (A) ～終了 (B) の入力欄とリセットは見出しごとまとめて折り返す (要素間 margin 4 つ分を含む)
            var rangeFieldWidth = RANGE_LABEL_WIDTH + GUIView.defaultMargin + RANGE_FIELD_WIDTH;
            DrawGroupLabel(view, "範囲操作",
                rangeFieldWidth * 2 + 20 + GUIView.defaultMargin * 4, SHORT_LABEL_WIDTH);

            // A / B はラベルドラッグでも増減できる
            view.DrawDragIntField(new GUIView.DragIntFieldOption
            {
                label = "A",
                labelWidth = RANGE_LABEL_WIDTH,
                value = selectStartFrameNo,
                fieldWidth = RANGE_FIELD_WIDTH,
                height = ROW_HEIGHT,
                onChanged = value => selectStartFrameNo = value,
            });

            view.DrawDragIntField(new GUIView.DragIntFieldOption
            {
                label = "B",
                labelWidth = RANGE_LABEL_WIDTH,
                value = selectEndFrameNo,
                fieldWidth = RANGE_FIELD_WIDTH,
                height = ROW_HEIGHT,
                onChanged = value => selectEndFrameNo = value,
            });

            if (view.DrawButton("R", 20, ROW_HEIGHT))
            {
                selectStartFrameNo = 0;
                selectEndFrameNo = 0;
            }

            var isValidRange = timelineManager.IsValidFrameRnage(selectStartFrameNo, selectEndFrameNo);

            WrapIfNeeded(view, 65);
            if (view.DrawButton("範囲選択", 65, ROW_HEIGHT))
            {
                timelineManager.SelectFramesRange(selectStartFrameNo, selectEndFrameNo);
            }

            WrapIfNeeded(view, 65);
            if (view.DrawButton("ﾌﾚｰﾑ挿入", 65, ROW_HEIGHT, isValidRange && selectStartFrameNo > 0))
            {
                timelineManager.InsertFrames(selectStartFrameNo, selectEndFrameNo);
            }

            WrapIfNeeded(view, 65);
            if (view.DrawButton("ﾌﾚｰﾑ削除", 65, ROW_HEIGHT, isValidRange && selectStartFrameNo > 0))
            {
                timelineManager.DeleteFrames(selectStartFrameNo, selectEndFrameNo);
            }

            WrapIfNeeded(view, 65);
            if (view.DrawButton("ﾌﾚｰﾑ複製", 65, ROW_HEIGHT, isValidRange))
            {
                timelineManager.DuplicateFrames(selectStartFrameNo, selectEndFrameNo);
            }

            WrapIfNeeded(view, 60);
            if (view.DrawButton("縦選択", 60, ROW_HEIGHT))
            {
                timelineManager.SelectVerticalBones();
            }
        }

        /// <summary>操作対象メイドの選択。レイヤー選択自体は TimelineWindow のボーンメニュー上部にある</summary>
        private void DrawTargetMaidControls(GUIView view)
        {
            // 上の行と混ざると見つけにくいので必ず行頭から並べる
            BeginNewLine(view);
            view.DrawLabel("操作対象", 60, ROW_HEIGHT);

            // スロットを持たないレイヤー (カメラ等) でも選択中のメイドを出す
            _maidComboBox.currentIndex = currentLayer.hasSlotNo
                ? currentLayer.slotNo
                : maidManager.maidSlotNo;
            _maidComboBox.items = maidManager.maidCaches;
            _maidComboBox.DrawButton(view);
        }

        private void DrawToggles(GUIView view)
        {
            DrawIconToggle(view, ToolbarIcons.Kind.EditMode, "編集モード", MTEP.SceneEditorHack.isPoseEditing, true, newValue =>
            {
                MTEP.SceneEditorHack.isPoseEditing = newValue;
            });

            DrawIconToggle(view, ToolbarIcons.Kind.AutoKey, "自動登録", timelineConfig.isAutoKeyFrame, true, newValue =>
            {
                timelineConfig.isAutoKeyFrame = newValue;
                timelineConfig.dirty = true;
            }, AUTO_KEY_ON_COLOR);

            DrawIconToggle(view, ToolbarIcons.Kind.Maid, "メイド表示", maidManager.maid.Visible, true, newValue =>
            {
                maidManager.maid.Visible = newValue;
            });

            DrawIconToggle(view, ToolbarIcons.Kind.Model, "モデル表示", modelManager.Visible, true, newValue =>
            {
                modelManager.Visible = newValue;
            });

            DrawIconToggle(view, ToolbarIcons.Kind.Bg, "背景表示", timeline.isBackgroundVisible, true, newValue =>
            {
                timeline.isBackgroundVisible = newValue;
            });

            if (timelineManager.hasCameraLayer)
            {
                var cameraUpdated = false;
                cameraUpdated |= DrawIconToggle(view, ToolbarIcons.Kind.Camera, "カメラ同期", timelineConfig.isCameraSync, !currentLayer.isCameraLayer, newValue =>
                {
                    timelineConfig.isCameraSync = newValue;
                    timelineConfig.dirty = true;
                });

                cameraUpdated |= DrawIconToggle(view, ToolbarIcons.Kind.FovLock, "視野角固定", timelineConfig.isFixedFoV, !currentLayer.isCameraLayer, newValue =>
                {
                    // 編集中のカメラ固定なので、切り替えたら編集モードへ入る
                    AutoEditMode.Enter();
                    timelineConfig.isFixedFoV = newValue;
                    timelineConfig.dirty = true;
                });

                cameraUpdated |= DrawIconToggle(view, ToolbarIcons.Kind.FocusLock, "フォーカス固定", timelineConfig.isFixedFocus, !currentLayer.isCameraLayer, newValue =>
                {
                    AutoEditMode.Enter();
                    timelineConfig.isFixedFocus = newValue;
                    timelineConfig.dirty = true;
                });

                if (cameraUpdated)
                {
                    var cameraLayer = timelineManager.GetLayer(typeof(MTEP.CameraTimelineLayer));
                    if (cameraLayer != null)
                    {
                        cameraLayer.ApplyCurrentFrame(false);
                    }
                }
            }

            if (timelineManager.hasPostEffectLayer)
            {
                DrawIconToggle(view, ToolbarIcons.Kind.PostEffect, "ポスプロ同期", timelineConfig.isPostEffectSync, !currentLayer.isPostEffectLayer, newValue =>
                {
                    timelineConfig.isPostEffectSync = newValue;
                    timelineConfig.dirty = true;
                });
            }
        }

        /// <summary>
        /// アイコン表示のトグル。折り返しも込みで描き、値が変わったら true を返す
        /// (カメラ系トグルの ApplyCurrentFrame 判定に使う)。
        /// SceneViewWindow.DrawToolbarToggle と同型だが、enabled 制御と折り返しが要るため独自に持つ
        /// </summary>
        private static bool DrawIconToggle(
            GUIView view, ToolbarIcons.Kind kind, string label, bool value, bool enabled, Action<bool> onChanged,
            Color? onColor = null)
        {
            var icon = ToolbarIcons.GetTexture(kind);
            if (icon == null)
            {
                WrapIfNeeded(view, TEXT_TOGGLE_WIDTH);
                return view.DrawToggle(label, value, TEXT_TOGGLE_WIDTH, ROW_HEIGHT, enabled, onChanged);
            }

            WrapIfNeeded(view, ROW_HEIGHT);
            view.BeginEnabled(enabled);
            var changed = view.DrawToggle(icon, value, ROW_HEIGHT, ROW_HEIGHT, onChanged, ICON_TOGGLE_OFFSET, label, onColor);
            view.EndEnabled();
            return changed;
        }
    }
}
