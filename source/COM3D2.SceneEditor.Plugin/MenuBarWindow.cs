using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// サブウィンドウの開閉をまとめたメニューバー。
    /// タイトルバーは持たず、左端のグリップ部分だけでドラッグ移動する。
    /// メニュー展開中は項目リストを別の GUI.Window として直下に描く
    /// </summary>
    public class MenuBarWindow : IGUIWindow, IScreenScalableWindow
    {
        public static readonly int WINDOW_ID = 8903357;
        public static readonly int POPUP_WINDOW_ID = 8903358;
        public static readonly int SUB_POPUP_WINDOW_ID = 8903378;
        public static readonly int BAR_HEIGHT = 24;
        public static readonly int GRIP_WIDTH = 16;
        public static readonly int MENU_BUTTON_WIDTH = 80;
        public static readonly int MENU_BUTTON_MARGIN = 2;
        // 最長ラベル (「ウィンドウ表示」) が収まる幅
        public static readonly int TOGGLE_BUTTON_WIDTH = 110;
        public static readonly int ITEM_HEIGHT = 22;
        public static readonly int POPUP_WIDTH = 140;
        public static readonly int FRAME = 2;
        /// <summary>IMGUI 既定の縦スクロールバー幅。項目幅の差し引きに使う</summary>
        public static readonly int SCROLLBAR_WIDTH = 16;
        // ポップアップ項目のホバー色。label スタイルはホバー反応を持たないため自前で塗る
        private static readonly Color ITEM_HOVER_COLOR = new Color(1f, 1f, 1f, 0.15f);

        private static Config config => ConfigManager.instance.config;

        /// <summary>
        /// 切り替えられる 1 項目。チェック状態の取得と切替をデリゲートで持つ。
        /// ポップアップ内の項目とバー直置きのトグルで共用する
        /// </summary>
        private class MenuItem
        {
            public string label;
            public Func<bool> isOn;
            public Action toggle;
            /// <summary>表示条件。null は常時表示</summary>
            public Func<bool> visible;
            /// <summary>
            /// サブメニューの項目構築。設定した項目はクリックで横にサブポップアップを開く。
            /// 設定した場合 toggle は呼ばれない（クリックは開閉専用になる）
            /// </summary>
            public Func<MenuItem[]> buildSubItems;
        }

        private class MenuDef
        {
            public string title;
            public MenuItem[] items;
        }

        private MenuDef[] _menus;

        /// <summary>ドロップダウンを持たず、バー上で直接切り替えるトグル</summary>
        private MenuItem[] _barToggles;

        /// <summary>展開中のメニュー index。-1 は全て閉じている状態</summary>
        private int _openMenuIndex = -1;

        /// <summary>展開中のサブメニューの親項目の表示行 index。-1 は閉じている状態</summary>
        private int _openSubItemIndex = -1;

        /// <summary>展開中のサブメニュー項目。開いたときに buildSubItems で構築する</summary>
        private MenuItem[] _subItems;

        /// <summary>バー本体の描画用ビュー。項目を横並びのフローレイアウトで置く</summary>
        private readonly GUIView _barView = new GUIView
        {
            padding = new Vector2(FRAME, (BAR_HEIGHT - ITEM_HEIGHT) * 0.5f),
            margin = MENU_BUTTON_MARGIN,
        };

        /// <summary>ポップアップの描画用ビュー。項目高と一致させるため margin は持たない</summary>
        private readonly GUIView _popupView = new GUIView
        {
            padding = Vector2.zero,
            margin = 0,
        };

        /// <summary>サブメニュー用ポップアップの描画用ビュー</summary>
        private readonly GUIView _subPopupView = new GUIView
        {
            padding = Vector2.zero,
            margin = 0,
        };

        /// <summary>
        /// 復元・保存で最後に適用した位置。ドラッグ移動の検知はこれとの差分で行う。
        /// config との比較だと、画面サイズスケールで動いた位置を
        /// ドラッグと誤認して config を上書きしてしまう
        /// </summary>
        private Vector2 _lastAppliedPos;

        public int windowIndex { get; set; }
        public bool isShowWnd { get; set; }

        private Rect _windowRect;
        public Rect windowRect
        {
            get => _windowRect;
            set => _windowRect = value;
        }

        private static MenuBarWindow _instance = null;
        public static MenuBarWindow instance
            => _instance ?? (_instance = new MenuBarWindow());

        public void Init()
        {
            _menus = new MenuDef[]
            {
                new MenuDef
                {
                    title = "Window",
                    items = new MenuItem[]
                    {
                        CreateWindowItem("Scene", SceneViewWindow.instance),
                        CreateWindowItem("Hierarchy", HierarchyWindow.instance),
                        CreateWindowItem("Inspector", InspectorWindow.instance),
                        CreateWindowItem("Camera", CameraWindow.instance),
                        CreateWindowItem("背景", BackgroundWindow.instance),
                        CreateWindowItem("BGM", BgmWindow.instance),
                        CreateWindowItem("ライト", LightWindow.instance),
                        CreateWindowItem("PNG配置", PngPlacementWindow.instance),
                        CreateWindowItem("プリセット", PresetWindow.instance),
                        CreateWindowItem("操作履歴", HistoryWindow.instance),
                        CreateWindowItem("設定", SettingWindow.instance),
                        new MenuItem
                        {
                            label = "Editor終了",
                            isOn = () => false,
                            toggle = () => SceneEditorPlugin.instance.isEnable = false,
                        },
                    },
                },
                new MenuDef
                {
                    title = "メイド",
                    items = new MenuItem[]
                    {
                        CreateWindowItem("呼出", MaidCallWindow.instance),
                        CreateWindowItem("モーション", MaidPoseWindow.instance),
                        CreateWindowItem("表情", MaidFaceWindow.instance),
                        CreateWindowItem("指", MaidFingerWindow.instance),
                        CreateWindowItem("IK", MaidIKWindow.instance),
                        CreateWindowItem("脱衣", MaidUndressWindow.instance),
                        CreateWindowItem("重力", MaidGravityWindow.instance),
                        CreateWindowItem("ボーン", BoneEditWindow.instance),
                    },
                },
                new MenuDef
                {
                    title = "その他",
                    items = new MenuItem[]
                    {
                        new MenuItem
                        {
                            label = "撮影",
                            isOn = () => false,
                            toggle = () => ScreenshotManager.Capture(),
                        },
                        CreateGameViewItem(),
                        new MenuItem
                        {
                            label = "ゲームUI表示",
                            isOn = () => GameViewManager.instance.isUIVisible,
                            toggle = () => GameViewManager.instance.SetUIVisible(
                                !GameViewManager.instance.isUIVisible),
                            // NGUIの表示切替は直接描画中しか意味を持たないため最大化中のみ出す
                            visible = () => GameViewManager.instance.isMaximized,
                        },
                        new MenuItem
                        {
                            label = "レイアウト",
                            isOn = () => false,
                            buildSubItems = BuildLayoutItems,
                        },
                    },
                },
            };

            _barToggles = new MenuItem[]
            {
                new MenuItem
                {
                    label = "編集モード",
                    isOn = () => MaidManipulateManager.instance.isEditMode,
                    toggle = () =>
                    {
                        var manager = MaidManipulateManager.instance;
                        manager.isEditMode = !manager.isEditMode;
                    },
                },
                new MenuItem
                {
                    label = "ボーン表示",
                    isOn = () => MaidManipulateManager.instance.isBoneVisible,
                    toggle = () =>
                    {
                        var manager = MaidManipulateManager.instance;
                        manager.isBoneVisible = !manager.isBoneVisible;
                    },
                },
                new MenuItem
                {
                    label = "ウィンドウ表示",
                    isOn = () => !WindowManager.instance.isWindowsHidden,
                    toggle = () =>
                    {
                        var manager = WindowManager.instance;
                        manager.isWindowsHidden = !manager.isWindowsHidden;
                    },
                    // ゲーム画面を丸ごと見たいときの機能なので最大化中のみ出す
                    visible = () => GameViewManager.instance.isMaximized,
                },
            };

            var width = CalcBarWidth();
            RestorePlacement(width);
        }

        /// <summary>
        /// config の保存済み位置から現在の画面サイズ向けの矩形を組み立てる。
        /// バー幅は内容から決まるため位置のみスケールする
        /// (方式の理由は EditorSubWindow.RestorePlacement 参照)
        /// </summary>
        private void RestorePlacement(float width)
        {
            var x = config.menuBarPosX >= 0 ? config.menuBarPosX : 10;
            var y = config.menuBarPosY >= 0 ? config.menuBarPosY : 10;
            _windowRect = new Rect(x, y, width, BAR_HEIGHT);

            int baseW, baseH;
            if (config.menuBarPosX >= 0 &&
                config.TryGetWindowScreenSize(WINDOW_ID, out baseW, out baseH))
            {
                _windowRect.position = WindowPlacementScaler.ScalePosition(
                    _windowRect.position, baseW, baseH, Screen.width, Screen.height);
            }

            _lastAppliedPos = _windowRect.position;
        }

        public void OnScreenSizeScaled(bool settled)
        {
            // 位置の再計算のみで後処理が不要なため settled は使わない
            RestorePlacement(_windowRect.width);
        }

        private int CalcBarWidth()
        {
            return FRAME * 2 + GRIP_WIDTH + MENU_BUTTON_MARGIN
                + (MENU_BUTTON_WIDTH + MENU_BUTTON_MARGIN) * _menus.Length
                + (TOGGLE_BUTTON_WIDTH + MENU_BUTTON_MARGIN) * CountVisibleToggles();
        }

        private int CountVisibleToggles()
        {
            return CountVisibleItems(_barToggles);
        }

        private static bool IsItemVisible(MenuItem item)
        {
            return item.visible == null || item.visible();
        }

        private static int CountVisibleItems(MenuItem[] items)
        {
            var count = 0;
            foreach (var item in items)
            {
                if (IsItemVisible(item))
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// GameView の最大化切替。GameView 自体は常時表示のため表示切替は持たず、
        /// ウィンドウ表示 (RT 描画) と最大化 (直接描画) の切り替えだけを担う
        /// </summary>
        private static MenuItem CreateGameViewItem()
        {
            return new MenuItem
            {
                label = "Game最大化",
                isOn = () => GameViewManager.instance.isMaximized,
                toggle = () =>
                {
                    var manager = GameViewManager.instance;
                    manager.SetMaximized(!manager.isMaximized);
                },
            };
        }

        private static MenuItem CreateWindowItem(string label, EditorSubWindow window)
        {
            return new MenuItem
            {
                label = label,
                isOn = () => window.isShowWnd,
                toggle = () =>
                {
                    window.isShowWnd = !window.isShowWnd;

                    if (window.isShowWnd)
                    {
                        // 表示位置のヘッダーが他ウィンドウと重なっていればそのままドッキングする
                        TabGroupManager.instance.MergeIfHeaderOverlaps(window);
                    }
                    else
                    {
                        // 非表示にしたウィンドウをグループへ残すとタブバーに出続けるため、
                        // ウィンドウ自身の x ボタンと同様にグループからも外す
                        TabGroupManager.instance.RemoveFromGroup(window);
                        WindowConnectManager.instance.OnWindowHidden(window);
                    }
                },
            };
        }

        /// <summary>
        /// レイアウトサブメニューの項目を組み立てる。
        /// 保存でファイルが増えるため、展開のたびに一覧から再構築する
        /// </summary>
        private MenuItem[] BuildLayoutItems()
        {
            var names = WindowLayoutManager.GetLayoutNames();
            var items = new MenuItem[names.Count + 1];

            for (var i = 0; i < names.Count; i++)
            {
                var name = names[i];
                items[i] = new MenuItem
                {
                    label = name,
                    isOn = () => false,
                    toggle = () => WindowLayoutManager.ApplyLayout(name),
                };
            }

            items[names.Count] = new MenuItem
            {
                label = "保存...",
                isOn = () => false,
                toggle = () =>
                {
                    // モーダルと重ならないようメニューを閉じてから開く
                    _openMenuIndex = -1;
                    _openSubItemIndex = -1;
                    SaveLayoutPopupWindow.Show();
                },
            };

            return items;
        }

        public void OnGUI()
        {
            if (!isShowWnd)
            {
                return;
            }

            // 「ウィンドウ表示」など表示条件付きトグルがあるため、バー幅は毎フレーム計算する
            _windowRect.width = CalcBarWidth();

            _windowRect = GUI.Window(WINDOW_ID, _windowRect, DrawBar, "", GUIView.gsWin);

            // 画面外へ出ないようクランプ
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0, Screen.width - _windowRect.width);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Screen.height - BAR_HEIGHT);

            if (_openMenuIndex >= 0)
            {
                var popupRect = GetPopupRect(_openMenuIndex);
                GUI.Window(POPUP_WINDOW_ID, popupRect, DrawPopup, "", GUIView.gsWin);
                // GameView 以外のウィンドウにも隠されないよう最前面へ
                GUI.BringWindowToFront(POPUP_WINDOW_ID);

                if (_openSubItemIndex >= 0)
                {
                    GUI.Window(SUB_POPUP_WINDOW_ID, GetSubPopupRect(), DrawSubPopup, "", GUIView.gsWin);
                    GUI.BringWindowToFront(SUB_POPUP_WINDOW_ID);
                }
            }
        }

        private void DrawBar(int id)
        {
            var view = _barView;
            view.Init(0, 0, _windowRect.width, BAR_HEIGHT);
            view.BeginHorizontal();

            // グリップ。見た目のみで、ドラッグ判定は末尾の GUI.DragWindow が担う
            view.DrawLabel("≡", GRIP_WIDTH, ITEM_HEIGHT);

            for (var i = 0; i < _menus.Length; i++)
            {
                // 展開中の見出しはアクセント色で示す。再クリックで閉じる
                var color = _openMenuIndex == i ? GUIView.option.accentColor : (Color?)null;
                if (view.DrawButton(_menus[i].title + " ▾", MENU_BUTTON_WIDTH, ITEM_HEIGHT, true, color))
                {
                    _openMenuIndex = _openMenuIndex == i ? -1 : i;
                    _openSubItemIndex = -1;
                    // 前回のスクロール位置が別メニューへ持ち越されないよう先頭へ戻す
                    _popupView.scrollPosition = Vector2.zero;
                }
            }

            foreach (var toggle in _barToggles)
            {
                if (!IsItemVisible(toggle))
                {
                    continue;
                }

                view.DrawToggle(toggle.label, toggle.isOn(), TOGGLE_BUTTON_WIDTH, ITEM_HEIGHT,
                    _ => toggle.toggle());
            }

            view.EndLayout();

            // グリップ部分だけでドラッグ移動する
            GUI.DragWindow(new Rect(0, 0, FRAME + GRIP_WIDTH, BAR_HEIGHT));
        }

        private Rect GetPopupRect(int menuIndex)
        {
            var x = _windowRect.x + FRAME + GRIP_WIDTH + MENU_BUTTON_MARGIN
                + (MENU_BUTTON_WIDTH + MENU_BUTTON_MARGIN) * menuIndex;
            var y = _windowRect.y + BAR_HEIGHT;

            // 項目数が画面高を超えた分はスクロールで辿れるため、ここでは画面内に収める
            var height = Mathf.Min(GetPopupContentHeight(menuIndex) + FRAME * 2, Screen.height);

            // バーが画面端にあってもポップアップが画面外へ出ないようクランプ
            x = Mathf.Clamp(x, 0, Screen.width - POPUP_WIDTH);
            y = Mathf.Clamp(y, 0, Screen.height - height);

            return new Rect(x, y, POPUP_WIDTH, height);
        }

        /// <summary>枠を除いた項目リスト全体の高さ</summary>
        private float GetPopupContentHeight(int menuIndex)
        {
            return GetContentHeight(_menus[menuIndex].items);
        }

        /// <summary>枠を除いた項目リスト全体の高さ (ポップアップ・サブポップアップ共通)</summary>
        private static float GetContentHeight(MenuItem[] items)
        {
            return ITEM_HEIGHT * CountVisibleItems(items);
        }

        private void DrawPopup(int id)
        {
            var items = _menus[_openMenuIndex].items;
            DrawItemList(_popupView, items, GetPopupRect(_openMenuIndex).height, (index, item) =>
            {
                if (item.buildSubItems != null)
                {
                    // 再クリックで閉じる。開くときに項目を構築する
                    if (_openSubItemIndex == index)
                    {
                        _openSubItemIndex = -1;
                    }
                    else
                    {
                        _openSubItemIndex = index;
                        _subItems = item.buildSubItems();
                        _subPopupView.scrollPosition = Vector2.zero;
                    }
                }
                else
                {
                    _openSubItemIndex = -1;
                    item.toggle();
                }
            });
        }

        private void DrawSubPopup(int id)
        {
            DrawItemList(_subPopupView, _subItems, GetSubPopupRect().height,
                (index, item) => item.toggle());
        }

        /// <summary>
        /// ポップアップ・サブポップアップ共通の項目リスト描画。
        /// onClick へ渡すのは配列 index ではなく表示行 index。
        /// 非表示項目を詰めて描くため、サブポップアップの位置合わせには表示行が要る
        /// </summary>
        private void DrawItemList(GUIView view, MenuItem[] items, float windowHeight,
            Action<int, MenuItem> onClick)
        {
            view.Init(0, 0, POPUP_WIDTH, windowHeight);
            // 枠の内側からスクロール領域を始める。padding だとスクロール内の
            // 項目座標にも加算されてずれるため、currentPos で位置だけ寄せる
            view.currentPos = new Vector2(FRAME, FRAME);

            var viewWidth = POPUP_WIDTH - FRAME * 2;
            var viewHeight = view.viewRect.height - FRAME * 2;
            var contentHeight = GetContentHeight(items);

            // 収まらないときはスクロールバーが出る分だけ項目を狭め、横スクロールを出さない
            var itemWidth = contentHeight > viewHeight
                ? viewWidth - SCROLLBAR_WIDTH
                : viewWidth;

            view.BeginScrollView(viewWidth, viewHeight,
                new Rect(0, 0, itemWidth, contentHeight), false, false);
            {
                var row = 0;
                foreach (var item in items)
                {
                    if (!IsItemVisible(item))
                    {
                        continue;
                    }

                    // label スタイルはホバー反応を持たないため自前で塗る。
                    // GetDrawRect は currentPos を進めないので直後のボタンと同じ矩形になる
                    var rect = view.GetDrawRect(itemWidth, ITEM_HEIGHT);
                    if (rect.Contains(Event.current.mousePosition))
                    {
                        view.BeginColor(ITEM_HOVER_COLOR);
                        GUI.DrawTexture(rect, Texture2D.whiteTexture);
                        view.EndColor();
                    }

                    // 連続で切り替えられるよう、クリックしてもメニューは閉じない
                    var label = (item.isOn() ? "✓ " : "    ") + item.label
                        + (item.buildSubItems != null ? " ▸" : "");
                    if (view.DrawButton(label, itemWidth, ITEM_HEIGHT, true, null, GUIView.gsLabel))
                    {
                        onClick(row, item);
                    }

                    row++;
                }
            }
            view.EndScrollView();
        }

        /// <summary>サブポップアップの矩形。親項目の右横に出し、入り切らなければ左側へ出す</summary>
        private Rect GetSubPopupRect()
        {
            var popupRect = GetPopupRect(_openMenuIndex);
            var height = Mathf.Min(GetContentHeight(_subItems) + FRAME * 2, Screen.height);

            var x = popupRect.x + POPUP_WIDTH;
            var y = popupRect.y + FRAME
                + ITEM_HEIGHT * _openSubItemIndex - _popupView.scrollPosition.y;

            if (x + POPUP_WIDTH > Screen.width)
            {
                x = popupRect.x - POPUP_WIDTH;
            }
            x = Mathf.Clamp(x, 0, Screen.width - POPUP_WIDTH);
            y = Mathf.Clamp(y, 0, Screen.height - height);

            return new Rect(x, y, POPUP_WIDTH, height);
        }

        public void Update()
        {
            // バー・ポップアップの外をクリックしたらメニューを閉じる
            if (_openMenuIndex >= 0 && Input.GetMouseButtonDown(0))
            {
                var pos = InputRemapper.rawGuiPosition;
                if (!_windowRect.Contains(pos) && !GetPopupRect(_openMenuIndex).Contains(pos)
                    && (_openSubItemIndex < 0 || !GetSubPopupRect().Contains(pos)))
                {
                    _openMenuIndex = -1;
                    _openSubItemIndex = -1;
                }
            }

            // ドラッグ移動を追従保存する
            if (_windowRect.position != _lastAppliedPos)
            {
                SavePlacement();
            }
        }

        public void SavePlacement()
        {
            config.menuBarPosX = (int)_windowRect.x;
            config.menuBarPosY = (int)_windowRect.y;
            config.SetWindowScreenSize(WINDOW_ID, Screen.width, Screen.height);
            config.dirty = true;
            _lastAppliedPos = _windowRect.position;
        }

        /// <summary>レイアウト適用用。保存時の画面サイズとの比率で位置をスケールして適用する</summary>
        public void ApplyPosition(int x, int y, int baseScreenWidth, int baseScreenHeight)
        {
            _windowRect.position = WindowPlacementScaler.ScalePosition(
                new Vector2(x, y), baseScreenWidth, baseScreenHeight, Screen.width, Screen.height);
            SavePlacement();
        }

        public void Close()
        {
            isShowWnd = false;
            _openMenuIndex = -1;
            _openSubItemIndex = -1;
        }

        public void OnLoad()
        {
        }

        public void OnScreenSizeChanged()
        {
        }

        public void OnChangedSceneLevel(Scene scene, LoadSceneMode sceneMode)
        {
        }
    }
}
