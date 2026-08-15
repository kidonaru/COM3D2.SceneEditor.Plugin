using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タブグループの生成・マージ・分離・解体を管理する。
    ///
    /// マージ: ヘッダー左ドラッグ中に他ウィンドウのヘッダーと重ねて離すと統合。
    /// 分離: タブをつまんでヘッダー外へ一定距離ドラッグすると独立ウィンドウへ戻す。
    /// GUI.DragWindow はドラッグの開始・終了イベントをくれないため、
    /// DrawWindow からの押下通知と Input のボタン状態を組み合わせて追跡する
    /// </summary>
    public class TabGroupManager : ManagerBase
    {
        /// <summary>タブをヘッダーからこれだけ離したら分離する (px)</summary>
        private static readonly float DETACH_DISTANCE = 20f;
        /// <summary>これ以上動かしていなければドロップとみなさない (クリックとの区別)</summary>
        private static readonly float DRAG_THRESHOLD = 5f;
        /// <summary>再表示時の自動ドッキングでヘッダー位置の一致とみなす許容誤差 (px)</summary>
        private static readonly float HEADER_MATCH_TOLERANCE = 3f;

        public readonly List<TabGroup> groups = new List<TabGroup>();

        // ヘッダー移動ドラッグの追跡。GUI.DragWindow に移動を任せ、離した位置だけ見る
        private IDockableWindow _headerDragWindow;
        private Vector2 _headerDragStartPos;

        // タブつまみドラッグの追跡。分離後は手動でウィンドウを追従させる
        private IDockableWindow _tabDragWindow;
        private Vector2 _tabGrabOffset;
        private bool _tabDetached;

        private static TabGroupManager _instance = null;
        public static TabGroupManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TabGroupManager();
                }
                return _instance;
            }
        }

        private TabGroupManager()
        {
        }

        /// <summary>移動ドラッグ (ヘッダー / コンテンツ空き領域) の開始候補を記録する</summary>
        public void OnHeaderMouseDown(IDockableWindow window)
        {
            _headerDragWindow = window;
            _headerDragStartPos = InputRemapper.rawGuiPosition;
        }

        /// <summary>タブつまみドラッグの開始候補を記録する。grabOffset はウィンドウローカルの押下位置</summary>
        public void OnTabMouseDown(IDockableWindow window, Vector2 grabOffset)
        {
            _tabDragWindow = window;
            _tabGrabOffset = grabOffset;
            _tabDetached = false;
        }

        /// <summary>
        /// タブ押下の集約処理。内部窓は直接、外部窓は DockingHost.NotifyTabMouseDown 経由で呼ぶ。
        /// index はタブ並び順 (TabGroup.windows のリスト順)、grabOffset はウィンドウローカルの押下位置
        /// </summary>
        public void OnTabPressed(IDockableWindow member, int tabIndex, Vector2 grabOffset)
        {
            var group = member.group;
            if (group == null || tabIndex < 0 || tabIndex >= group.windows.Count)
            {
                return;
            }
            var target = group.windows[tabIndex];
            group.SetActive(target);
            OnTabMouseDown(target, grabOffset);
        }

        public bool IsDropTarget(IDockableWindow window)
        {
            var dragged = draggedWindow;
            if (dragged == null)
            {
                return false;
            }
            return FindDropTarget(dragged) == window;
        }

        /// <summary>現在ドラッグで運ばれているウィンドウ (ヘッダー移動 or 分離済みタブ)</summary>
        private IDockableWindow draggedWindow
        {
            get
            {
                if (_tabDragWindow != null && _tabDetached)
                {
                    return _tabDragWindow;
                }
                if (_headerDragWindow != null &&
                    Vector2.Distance(InputRemapper.rawGuiPosition, _headerDragStartPos) > DRAG_THRESHOLD)
                {
                    return _headerDragWindow;
                }
                return null;
            }
        }

        public override void Update()
        {
            DockingHost.UpdateExternals();
            UpdateTabDrag();
            UpdateHeaderDrag();
        }

        /// <summary>タブつまみ: ヘッダー外へ出たら分離し、以降はマウスへ手動追従させる</summary>
        private void UpdateTabDrag()
        {
            if (_tabDragWindow == null)
            {
                return;
            }

            if (!_tabDragWindow.isShowWnd)
            {
                _tabDragWindow = null;
                _tabDetached = false;
                return;
            }

            var guiPos = InputRemapper.rawGuiPosition;

            if (!_tabDetached)
            {
                // グループが解体済みならつまみ追跡も終了 (通常のヘッダードラッグに任せる)
                if (_tabDragWindow.group == null)
                {
                    _tabDragWindow = null;
                    return;
                }

                var header = _tabDragWindow.group.activeWindow.headerRect;
                var expanded = new Rect(
                    header.x - DETACH_DISTANCE, header.y - DETACH_DISTANCE,
                    header.width + DETACH_DISTANCE * 2, header.height + DETACH_DISTANCE * 2);
                if (!expanded.Contains(guiPos))
                {
                    Detach(_tabDragWindow);
                }
            }

            if (_tabDetached)
            {
                // つまんだ位置がヘッダー上に来るよう追従させる
                var rect = _tabDragWindow.windowRect;
                rect.x = guiPos.x - _tabGrabOffset.x;
                rect.y = guiPos.y - _tabGrabOffset.y;
                _tabDragWindow.windowRect = rect;
            }

            if (!Input.GetMouseButton(0))
            {
                if (_tabDetached)
                {
                    TryMergeAt(_tabDragWindow);
                    // 復元は先頭メンバーの保存位置を基準に揃えるため、ドラッグした
                    // 1 枚だけでなくグループ全員の位置を保存する (矩形は SyncRect 済み)
                    if (_tabDragWindow.group != null)
                    {
                        foreach (var member in _tabDragWindow.group.windows)
                        {
                            member.SavePlacement();
                        }
                    }
                    else
                    {
                        _tabDragWindow.SavePlacement();
                    }
                }
                _tabDragWindow = null;
                _tabDetached = false;
            }
        }

        /// <summary>ヘッダー移動: GUI.DragWindow に移動を任せ、離したときだけマージ判定する</summary>
        private void UpdateHeaderDrag()
        {
            if (_headerDragWindow == null || Input.GetMouseButton(0))
            {
                return;
            }

            var window = _headerDragWindow;
            _headerDragWindow = null;

            if (!window.isShowWnd)
            {
                return;
            }

            if (Vector2.Distance(InputRemapper.rawGuiPosition, _headerDragStartPos) > DRAG_THRESHOLD)
            {
                TryMergeAt(window);
            }
        }

        /// <summary>タブをグループから外して独立ウィンドウへ戻す。座標は呼び出し元の追従処理が続けて設定する</summary>
        private void Detach(IDockableWindow window)
        {
            RemoveFromGroup(window);
            _tabDetached = true;
        }

        /// <summary>
        /// ドラッグ中ウィンドウのドロップ先を探す。マウスカーソルが乗っているヘッダーで判定する。
        /// 非アクティブタブは描画されず矩形も持たないため対象外
        /// </summary>
        private IDockableWindow FindDropTarget(IDockableWindow dragged)
        {
            var guiPos = InputRemapper.rawGuiPosition;

            foreach (var target in DockingHost.EnumerateDockables())
            {
                if (target == dragged || !target.isShowWnd || !target.isTabVisible)
                {
                    continue;
                }
                // 同じグループ内 (ドラッグ元がグループごと動いている場合の同僚) は除外
                if (dragged.group != null && dragged.group.Contains(target))
                {
                    continue;
                }
                if (target.headerRect.Contains(guiPos))
                {
                    return target;
                }
            }
            return null;
        }

        /// <summary>
        /// ヘッダーが重なっている表示中ウィンドウがあればそのグループへ統合する。
        /// メニューからの再表示時に、同じ位置のウィンドウへ自動ドッキングさせる用
        /// </summary>
        public void MergeIfHeaderOverlaps(IDockableWindow window)
        {
            MergeIfHeaderMatch(window, (target, header) => target.Overlaps(header));
        }

        /// <summary>
        /// ヘッダー位置がほぼ一致 (数 px 以内) する表示中ウィンドウがあればそのグループへ統合する。
        /// 外部ウィンドウの再表示時に、ドッキングしていた位置のまま出てきたものだけを
        /// 自動で再ドッキングさせる用。重なり判定 (MergeIfHeaderOverlaps) だと
        /// たまたま近接していただけの窓まで吸い込むため、一致判定に絞っている
        /// </summary>
        public void MergeIfHeaderMatches(IDockableWindow window)
        {
            MergeIfHeaderMatch(window, (target, header) =>
                Mathf.Abs(target.x - header.x) <= HEADER_MATCH_TOLERANCE
                && Mathf.Abs(target.y - header.y) <= HEADER_MATCH_TOLERANCE);
        }

        /// <summary>ヘッダー矩形が条件を満たす最初の表示中ウィンドウへ統合する共通処理</summary>
        private void MergeIfHeaderMatch(
            IDockableWindow window, System.Func<Rect, Rect, bool> match)
        {
            var header = window.headerRect;
            foreach (var target in DockingHost.EnumerateDockables())
            {
                if (target == window || !target.isShowWnd || !target.isTabVisible)
                {
                    continue;
                }
                if (match(target.headerRect, header))
                {
                    Merge(window, target);
                    return;
                }
            }
        }

        private void TryMergeAt(IDockableWindow dragged)
        {
            var target = FindDropTarget(dragged);
            if (target != null)
            {
                Merge(dragged, target);
            }
        }

        /// <summary>
        /// source (グループなら全員) を target のグループへ統合する。
        /// target が独立窓なら新しいグループを作る
        /// </summary>
        public void Merge(IDockableWindow source, IDockableWindow target)
        {
            // 同一グループ同士だと Remove と Add が同じリストへ交互に働いて並びが壊れる
            if (source.group != null && source.group == target.group)
            {
                return;
            }

            // タブ構成を組み替える前に通知する。組み替え後は所属タブグループが
            // 変わってしまい、統合前のコネクトグループを解決できなくなる
            WindowConnectManager.instance.OnTabMerged(source, target);

            var targetGroup = target.group;
            if (targetGroup == null)
            {
                targetGroup = new TabGroup();
                groups.Add(targetGroup);
                targetGroup.Add(target);
            }

            var sourceGroup = source.group;
            if (sourceGroup != null)
            {
                // グループごとのマージ。元グループは空になるので破棄する
                var members = new List<IDockableWindow>(sourceGroup.windows);
                foreach (var member in members)
                {
                    sourceGroup.Remove(member);
                    // activate: false で並びだけ作る。1 件ずつアクティブ化すると SceneView の
                    // Activate/Deactivate が連鎖してカメラ・RT を無駄に作り直す
                    targetGroup.Add(member, activate: false);
                }
                groups.Remove(sourceGroup);
                // ドラッグしていたタブをアクティブにする
                targetGroup.SetActive(source);
            }
            else
            {
                targetGroup.Add(source);
            }

            targetGroup.SyncRect(target.windowRect);
            MarkGroupsDirty();
        }

        /// <summary>
        /// ウィンドウをグループから外し、1 枚になったグループは解体する。
        /// save=false はプラグイン終了時の片付け用。構成変更ではないので config へ書き戻さない
        /// (保存済みの構成を空で上書きしてしまい、次回の復元が効かなくなる)
        /// </summary>
        public void RemoveFromGroup(IDockableWindow window, bool save = true)
        {
            var group = window.group;
            if (group == null)
            {
                return;
            }

            group.Remove(window);

            if (group.windows.Count <= 1)
            {
                DissolveGroup(group);
            }

            if (save)
            {
                MarkGroupsDirty();
            }
        }

        private void DissolveGroup(TabGroup group)
        {
            // 残っているウィンドウを独立に戻してからリストから消す
            for (var i = group.windows.Count - 1; i >= 0; i--)
            {
                group.Remove(group.windows[i]);
            }
            groups.Remove(group);
        }

        /// <summary>全タブグループを解体する (レイアウト適用時のリセット用)</summary>
        public void DissolveAllGroups()
        {
            for (var i = groups.Count - 1; i >= 0; i--)
            {
                DissolveGroup(groups[i]);
            }
        }

        /// <summary>
        /// グループ構成の変更を config へ反映して保存を予約する。
        /// ConfigManager はマウスアップ時に dirty を見て即保存するため、
        /// 先に SaveGroups しておかないと変更前の構成が書き込まれる
        /// </summary>
        private void MarkGroupsDirty()
        {
            SaveGroups();
            config.dirty = true;
        }

        /// <summary>グループ構成を config へ書き出す</summary>
        public void SaveGroups()
        {
            config.tabGroups.Clear();
            foreach (var group in groups)
            {
                var ids = new List<string>(group.windows.Count);
                foreach (var window in group.windows)
                {
                    ids.Add(window.tabWindowId.ToString());
                }
                config.tabGroups.Add(
                    group.activeWindow.tabWindowId + ":" + string.Join(",", ids.ToArray()));
            }
        }

        /// <summary>config のグループ構成を復元する。不明な ID や非表示ウィンドウは無視する</summary>
        public void RestoreGroups()
        {
            foreach (var entry in config.tabGroups)
            {
                var parts = entry.Split(':');
                if (parts.Length != 2)
                {
                    continue;
                }

                int activeId;
                if (!int.TryParse(parts[0], out activeId))
                {
                    continue;
                }

                var members = new List<IDockableWindow>();
                foreach (var idText in parts[1].Split(','))
                {
                    int id;
                    if (!int.TryParse(idText, out id))
                    {
                        continue;
                    }
                    var window = FindWindow(id);
                    if (window != null && window.isShowWnd && window.group == null)
                    {
                        members.Add(window);
                    }
                }

                if (members.Count < 2)
                {
                    continue;
                }

                var group = new TabGroup();
                groups.Add(group);
                foreach (var member in members)
                {
                    // activate: false で並びだけ作る。毎回アクティブ化すると SceneView の
                    // Activate/Deactivate が復元中に連鎖してカメラ・RT を無駄に作り直す
                    group.Add(member, activate: false);
                }

                var active = FindWindow(activeId);
                if (active != null && group.Contains(active))
                {
                    group.SetActive(active);
                }

                // 矩形は先頭メンバー基準で揃える (各窓の保存位置はバラバラのため)
                group.SyncRect(members[0].windowRect);
            }
        }

        private IDockableWindow FindWindow(int tabWindowId)
        {
            foreach (var dockable in DockingHost.EnumerateDockables())
            {
                if (dockable.tabWindowId == tabWindowId)
                {
                    return dockable;
                }
            }
            return null;
        }

        /// <summary>
        /// 指定ウィンドウをドラッグ追跡中の参照から取り除く。
        /// Unregister 後もドラッグ変数が古いアダプタを指したままだと、
        /// 次フレームの Update() で解除済みインスタンスへ触れてしまう
        /// </summary>
        public void CancelDrag(IDockableWindow window)
        {
            if (_headerDragWindow == window)
            {
                _headerDragWindow = null;
            }
            if (_tabDragWindow == window)
            {
                _tabDragWindow = null;
                _tabDetached = false;
            }
        }

        public override void OnPluginDisable()
        {
            _headerDragWindow = null;
            _tabDragWindow = null;
            _tabDetached = false;

            // 外部ゲスト窓がグループへ加入したまま無効化されると復帰不能になるため解除する
            DockingHost.OnHostDisabled();
        }
    }
}
