using System;
using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 外部プラグインのウィンドウをタブドッキングへ参加させる公開 API。
    /// MTEUtils の DockingClient からリフレクションで発見・呼び出しされるため、
    /// クラス名・メソッドシグネチャは公開後変更禁止 (変更時は Register2 等の別名で追加する)。
    /// 契約はプリミティブ + デリゲートのみ (型は DLL 間で共有できない。docs/window-framework-design.md 参照)
    /// </summary>
    public static class DockingHost
    {
        /// <summary>内部ウィンドウ用に予約している windowId の帯 (末尾は将来の新規ウィンドウ用の空き)</summary>
        private const int RESERVED_WINDOW_ID_MIN = 8903349;
        private const int RESERVED_WINDOW_ID_MAX = 8903377;

        private static readonly List<ExternalWindowAdapter> _externals
            = new List<ExternalWindowAdapter>();

        /// <summary>
        /// 外部ウィンドウを登録する。戻り値は Unregister / NotifyHeaderMouseDown へ渡すハンドル。
        /// 同じ windowId が登録済みの場合は古い方を解除して置き換える (プラグインのリロード対策)
        /// </summary>
        public static object Register(
            int windowId,
            string title,
            Func<Rect> getRect,
            Action<Rect> setRect,
            Func<bool> isVisible,
            Action<bool> setTabVisible)
        {
            if (getRect == null || setRect == null || isVisible == null || setTabVisible == null)
            {
                MTEUtils.LogError("DockingHost.Register: デリゲートに null は指定できません");
                return null;
            }

            // 内部ウィンドウとの ID 衝突は入力の奪い合い等の分かりにくい不具合を招くため警告する
            foreach (var dockable in EnumerateDockables())
            {
                if (!(dockable is ExternalWindowAdapter) && dockable.tabWindowId == windowId)
                {
                    MTEUtils.LogWarning(
                        "DockingHost.Register: windowId " + windowId + " は内部ウィンドウと衝突しています");
                    break;
                }
            }

            // 内部ウィンドウの予約帯との衝突も警告する
            if (windowId >= RESERVED_WINDOW_ID_MIN && windowId <= RESERVED_WINDOW_ID_MAX)
            {
                MTEUtils.LogWarning(
                    "DockingHost.Register: windowId " + windowId +
                    " はホストの予約帯 (" + RESERVED_WINDOW_ID_MIN +
                    "-" + RESERVED_WINDOW_ID_MAX + ") と衝突しています");
            }

            for (var i = _externals.Count - 1; i >= 0; i--)
            {
                if (_externals[i].tabWindowId == windowId)
                {
                    Unregister(_externals[i]);
                }
            }

            var adapter = new ExternalWindowAdapter(
                windowId, title ?? "", getRect, setRect, isVisible, setTabVisible);
            _externals.Add(adapter);
            return adapter;
        }

        public static void Unregister(object handle)
        {
            var adapter = handle as ExternalWindowAdapter;
            if (adapter == null)
            {
                return;
            }
            // コネクトグループとドラッグ追跡からも外す (解除済みアダプタへの参照を残さない)
            WindowConnectManager.instance.OnWindowHidden(adapter);
            // ドラッグ追跡中の参照が残ったままだと Update() で解除済みアダプタへ触れてしまうため先に消す
            TabGroupManager.instance.CancelDrag(adapter);
            TabGroupManager.instance.RemoveFromGroup(adapter);
            _externals.Remove(adapter);
        }

        /// <summary>ヘッダー左押下の通知。ドッキング判定 (マージ) の起点になる</summary>
        public static void NotifyHeaderMouseDown(object handle)
        {
            var adapter = handle as ExternalWindowAdapter;
            if (adapter == null)
            {
                return;
            }
            TabGroupManager.instance.OnHeaderMouseDown(adapter);
        }

        /// <summary>
        /// コネクト協調のオプトイン宣言。Register 直後に呼ぶ。
        /// 宣言したゲストだけがコネクト（連結移動）の候補になる契約
        /// (受け身スナップの相手になるだけなら宣言不要)
        /// </summary>
        public static void EnableConnect(object handle)
        {
            var adapter = handle as ExternalWindowAdapter;
            if (adapter == null)
            {
                return;
            }
            adapter.connectCapable = true;
        }

        /// <summary>
        /// タブバー描画のオプトイン宣言。Register 直後に呼ぶ。
        /// 宣言したゲストはグループ加入中にタブ状態 (タブ名一覧, アクティブindex) の push を受け、
        /// 自前ヘッダーへタブバーを描く契約。未宣言の旧ゲストはタブバー非表示になる
        /// (ドッキング動作自体は従来どおり)
        /// </summary>
        public static void EnableTabBar(object handle, Action<string[], int> onTabBarChanged)
        {
            var adapter = handle as ExternalWindowAdapter;
            if (adapter == null || onTabBarChanged == null)
            {
                return;
            }
            adapter.SetTabBarCallback(onTabBarChanged);
        }

        /// <summary>
        /// ゲストが描いたタブの押下通知。アクティブ切替とつまみドラッグ候補の処理はホスト側で行う。
        /// x/y はゲストウィンドウローカルの押下位置
        /// </summary>
        public static void NotifyTabMouseDown(object handle, int tabIndex, float x, float y)
        {
            var adapter = handle as ExternalWindowAdapter;
            if (adapter == null)
            {
                return;
            }
            TabGroupManager.instance.OnTabPressed(adapter, tabIndex, new Vector2(x, y));
        }

        /// <summary>
        /// ヘッダー/空き領域の左押下通知。ドラッグスナップ追跡の起点になる。
        /// これを呼ぶゲストだけがドラッグスナップ対象になる契約
        /// (呼ばない旧ゲストは GUI.DragWindow を抑止できず吸着位置と喧嘩するため巻き込まない)
        /// </summary>
        public static void NotifyDragMouseDown(object handle)
        {
            var adapter = handle as ExternalWindowAdapter;
            if (adapter == null)
            {
                return;
            }
            WindowConnectManager.instance.OnDragMouseDown(adapter);
        }

        /// <summary>
        /// このウィンドウをドラッグ中でいずれかの軸が吸着しているか。
        /// ゲストは true の間 GUI.DragWindow を呼ばず、ホストの絶対配置に任せる契約
        /// </summary>
        public static bool IsSnapDragging(object handle)
        {
            var adapter = handle as ExternalWindowAdapter;
            return adapter != null && WindowConnectManager.instance.IsSnapDragging(adapter);
        }

        /// <summary>
        /// リサイズ中の矩形へ辺スナップを適用して返す。
        /// edges は WindowResizeController.ResizeEdge のビット (Left=1, Right=2, Top=4, Bottom=8)。
        /// 型は DLL 間で共有できないため int で受ける契約
        /// </summary>
        public static Rect SnapResize(object handle, Rect rect, int edges)
        {
            var adapter = handle as ExternalWindowAdapter;
            if (adapter == null)
            {
                return rect;
            }
            return WindowConnectManager.instance.SnapResize(
                adapter, rect, (WindowResizeController.ResizeEdge)edges);
        }

        /// <summary>隣接（辺が密着）している表示中ウィンドウがあるか。コネクトボタンの表示条件</summary>
        public static bool HasAdjacent(object handle)
        {
            var adapter = handle as ExternalWindowAdapter;
            return adapter != null && WindowConnectManager.instance.HasAdjacent(adapter);
        }

        /// <summary>コネクトグループに所属しているか。コネクトボタンのトグル状態と個別クランプ抑止に使う</summary>
        public static bool IsConnected(object handle)
        {
            var adapter = handle as ExternalWindowAdapter;
            return adapter != null && WindowConnectManager.instance.IsConnected(adapter);
        }

        /// <summary>連結トグル。未連結なら隣接ウィンドウと連結し、連結中なら自分だけ外れる</summary>
        public static void ToggleConnect(object handle)
        {
            var adapter = handle as ExternalWindowAdapter;
            if (adapter == null)
            {
                return;
            }
            WindowConnectManager.instance.ToggleConnect(adapter);
        }

        /// <summary>内部ウィンドウ (WindowManager 登録済み) と外部アダプタを合わせて列挙する</summary>
        internal static IEnumerable<IDockableWindow> EnumerateDockables()
        {
            foreach (var w in WindowManager.instance.windows)
            {
                var dockable = w as IDockableWindow;
                if (dockable != null)
                {
                    yield return dockable;
                }
            }
            foreach (var adapter in _externals)
            {
                yield return adapter;
            }
        }

        /// <summary>
        /// 外部アダプタをスナップ/コネクト対象として列挙する。
        /// GameViewWindow は IDockableWindow ではないため、WindowConnectManager は
        /// EnumerateDockables ではなく WindowManager.windows とこの列挙の合成を使う
        /// </summary>
        internal static IEnumerable<IConnectableWindow> EnumerateExternalConnectables()
        {
            foreach (var adapter in _externals)
            {
                yield return adapter;
            }
        }

        /// <summary>
        /// 外部ウィンドウの状態監視。非表示になったものをグループから外す
        /// (内部ウィンドウは自分の閉じるボタンで RemoveFromGroup するが、
        /// 外部ウィンドウの表示状態はゲスト側の都合でいつでも変わるためポーリングする)
        /// </summary>
        internal static void UpdateExternals()
        {
            for (var i = _externals.Count - 1; i >= 0; i--)
            {
                var adapter = _externals[i];
                if (!adapter.isShowWnd)
                {
                    if (adapter.group != null)
                    {
                        TabGroupManager.instance.RemoveFromGroup(adapter);
                    }
                    // 非表示の外部窓が吸着相手やコネクトメンバーとして残らないようにする
                    WindowConnectManager.instance.OnWindowHidden(adapter);
                    // アダプタを作り直さず同じ登録のまま再表示するゲストにも自動再ドッキングを効かせる
                    adapter.ResetAutoDockRetry();
                }
                // 表示直後の一定フレーム、ヘッダー位置がほぼ一致する窓があれば自動再ドッキング。
                // 外部窓のドッキング構成は復元されないが位置はゲストが保持しているため、
                // 同じ位置に出てきたものはドッキングへ復帰させる (設計判断は autoDockRetryFrames 参照)
                else if (adapter.group == null && adapter.autoDockRetryFrames > 0)
                {
                    adapter.autoDockRetryFrames--;
                    TabGroupManager.instance.MergeIfHeaderMatches(adapter);
                    if (adapter.group != null)
                    {
                        // 成立したら残りフレームを捨てる。残したままだと直後に手動で
                        // タブ分離したとき即座に再マージされて剥がせなくなる
                        adapter.autoDockRetryFrames = 0;
                    }
                }
                // 外部窓がアクティブタブの間のグループ移動同期。
                // 内部窓は自分の OnGUI で group.SyncRect するが、外部窓の矩形は
                // ゲスト側の GUI.DragWindow で動くためホストがポーリングで追従させる
                else if (adapter.group != null && adapter.group.activeWindow == adapter)
                {
                    var rect = adapter.windowRect;
                    if (rect != adapter.lastSyncedRect)
                    {
                        adapter.group.SyncRect(rect);
                        adapter.lastSyncedRect = rect;
                    }
                }
            }
        }

        /// <summary>
        /// ホスト (SceneEditor) 側のプラグイン無効化時に呼ばれる。
        /// 登録 (_externals) 自体は維持したまま、全外部アダプタをグループから外して
        /// ゲスト側が独立ウィンドウとして描画を再開できるようにする
        /// (NotifyTabVisibleChanged が発火し setTabVisible(true) が呼ばれる)
        /// </summary>
        internal static void OnHostDisabled()
        {
            foreach (var adapter in _externals)
            {
                var wasGrouped = adapter.group != null;
                TabGroupManager.instance.RemoveFromGroup(adapter, save: false);
                // タブと同様、終了時の片付けなので config へは書き戻さない
                WindowConnectManager.instance.OnWindowHidden(adapter, save: false);

                // ここで外したぶんの再ドッキング機会を作り直す。猶予はドッキング成立時に
                // 0 にされるため、リセットしないと再有効化時に一度も判定が走らない
                // (無効化中は UpdateExternals が回らないので猶予は消費されない)。
                // 元から独立していた窓は対象外。手動で剥がした窓まで再マージされてしまう
                if (wasGrouped)
                {
                    adapter.ResetAutoDockRetry();
                }
            }
        }
    }

    /// <summary>
    /// Register のデリゲート束を IDockableWindow として振る舞わせるアダプタ。
    /// SceneEditor 内部からは内部ウィンドウと同一に扱える
    /// </summary>
    internal class ExternalWindowAdapter : IDockableWindow
    {
        private readonly int _windowId;
        private readonly string _title;
        private readonly Func<Rect> _getRect;
        private readonly Action<Rect> _setRect;
        private readonly Func<bool> _isVisible;
        private readonly Action<bool> _setTabVisible;

        private bool _lastTabVisible = true;

        /// <summary>
        /// コネクト協調 (EnableConnect) を宣言済みか。
        /// 宣言が要る理由は WindowConnectManager.IsConnectCapable 参照
        /// </summary>
        internal bool connectCapable;

        /// <summary>
        /// UpdateExternals の矩形同期で最後にグループへ反映した矩形。
        /// 完全一致で差分判定するため、ゲストの getRect/setRect は冪等 (設定した値が
        /// そのまま返る) であることを前提にする。丸め・スケールを挟むと毎フレーム同期し続ける
        /// </summary>
        internal Rect lastSyncedRect;

        /// <summary>
        /// 自動再ドッキング判定を行う残りフレーム数。登録直後・非表示中・
        /// ホスト無効化でグループから外されたときにリセットされ、
        /// 表示中に未所属の間だけ毎フレーム消費する。
        /// エッジ検出でなくカウンタなのは、ゲスト (MTEUtils DockableWindowBase) が
        /// 表示トグルごとに Unregister/Register でアダプタを作り直し、
        /// 非表示→表示のエッジをホストから観測できないため。
        /// ワンショットでなく猶予を持つのは、起動直後はドッキング相手の窓が
        /// まだ登録・表示されていないことがあり、1 回きりだと空振り消費するため
        /// </summary>
        internal int autoDockRetryFrames = AUTO_DOCK_RETRY_FRAMES;

        /// <summary>自動再ドッキングを諦めるまでの猶予 (フレーム)。60fps でおよそ 1 秒</summary>
        private const int AUTO_DOCK_RETRY_FRAMES = 60;

        internal void ResetAutoDockRetry()
        {
            autoDockRetryFrames = AUTO_DOCK_RETRY_FRAMES;
        }

        /// <summary>EnableTabBar で登録されたタブ状態の通知先。null は未対応の旧ゲスト</summary>
        private Action<string[], int> _onTabBarChanged;

        /// <summary>コールバック例外で push を止めたか。壊れたゲストへ投げ続けない</summary>
        private bool _tabBarPushBroken;

        public ExternalWindowAdapter(
            int windowId, string title,
            Func<Rect> getRect, Action<Rect> setRect,
            Func<bool> isVisible, Action<bool> setTabVisible)
        {
            _windowId = windowId;
            _title = title;
            _getRect = getRect;
            _setRect = setRect;
            _isVisible = isVisible;
            _setTabVisible = setTabVisible;
        }

        public int tabWindowId => _windowId;
        public string windowTitleForTab => _title;
        public TabGroup group { get; set; }
        public bool isTabVisible => group == null || group.activeWindow == this;

        /// <summary>直近の正常な矩形。ゲスト側デリゲートが例外を投げた場合のフォールバック</summary>
        private Rect _lastRect;

        public Rect windowRect
        {
            get
            {
                // ゲスト側の例外でホストのフレーム処理を巻き込まないよう防御する
                try
                {
                    _lastRect = _getRect();
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
                return _lastRect;
            }
            set
            {
                try
                {
                    _setRect(value);
                    _lastRect = value;
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                }
            }
        }

        public bool isShowWnd
        {
            get
            {
                try
                {
                    return _isVisible();
                }
                catch (Exception e)
                {
                    MTEUtils.LogException(e);
                    return false;
                }
            }
        }

        public Rect headerRect
        {
            get
            {
                var rect = windowRect;
                return new Rect(rect.x, rect.y, rect.width, EditorSubWindow.HEADER_HEIGHT);
            }
        }

        public void NotifyTabVisibleChanged()
        {
            if (_lastTabVisible == isTabVisible)
            {
                return;
            }
            _lastTabVisible = isTabVisible;
            try
            {
                _setTabVisible(_lastTabVisible);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
            }
        }

        /// <summary>矩形の永続化はゲスト側の責務のため何もしない</summary>
        public void SavePlacement()
        {
        }

        internal void SetTabBarCallback(Action<string[], int> onTabBarChanged)
        {
            _onTabBarChanged = onTabBarChanged;
            _tabBarPushBroken = false;
        }

        public void SetTabBarState(string[] titles, int activeIndex)
        {
            if (_onTabBarChanged == null || _tabBarPushBroken)
            {
                return;
            }
            // ゲスト側の例外でホストのタブ処理を巻き込まないよう防御する
            try
            {
                _onTabBarChanged(titles, activeIndex);
            }
            catch (Exception e)
            {
                _tabBarPushBroken = true;
                MTEUtils.LogWarning(
                    "ExternalWindowAdapter: タブ状態の通知でゲスト側が例外を投げたため push を停止します: " + _title);
                MTEUtils.LogException(e);
            }
        }
    }
}
