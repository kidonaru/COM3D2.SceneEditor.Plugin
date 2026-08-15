using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ウィンドウのスナップ吸着と、コネクト（隣接連結移動）グループを管理する。
    ///
    /// スナップ: ドラッグ中に画面枠や他ウィンドウの辺へ近づくと吸着する（連結はしない）。
    /// コネクト: ヘッダーのボタンで明示的に連結し、以降はどのメンバーを
    /// ドラッグしても群全体が同じ移動量で動く。
    /// GUI.DragWindow はドラッグイベントをくれないため、TabGroupManager と同様に
    /// 押下通知 + Input のボタン状態 + 毎フレームの矩形差分で追跡する
    /// </summary>
    /// <summary>
    /// スナップ・コネクトの対象になれるウィンドウ。
    /// EditorSubWindow に加え、タブドッキング非対応の GameViewWindow も実装する
    /// (タブグループ非所属なら group は null、isTabVisible は true を返せばよい)
    /// </summary>
    public interface IConnectableWindow
    {
        Rect windowRect { get; set; }
        bool isShowWnd { get; }
        bool isTabVisible { get; }
        TabGroup group { get; }
        int tabWindowId { get; }
    }

    public class WindowConnectManager : ManagerBase
    {
        /// <summary>スナップ吸着を開始する辺同士の距離 (px)</summary>
        private static readonly float SNAP_DISTANCE = 10f;
        /// <summary>
        /// 吸着済みの軸を解除するまでの距離 (px)。
        /// 吸着開始より大きくしてヒステリシスを作らないと、吸着境界付近で
        /// 吸着と解除がフレームごとに切り替わってドラッグがばたつく
        /// </summary>
        private static readonly float SNAP_RELEASE_DISTANCE = 24f;
        /// <summary>コネクトボタン表示に使う隣接（密着）判定の許容誤差 (px)</summary>
        private static readonly float ADJACENT_EPSILON = 2f;
        /// <summary>これ以上動かしていなければドラッグとみなさない (クリックとの区別)</summary>
        private static readonly float DRAG_THRESHOLD = 5f;

        /// <summary>コネクトグループ。1 グループ = 一緒に動くウィンドウの集合</summary>
        public readonly List<List<IConnectableWindow>> groups = new List<List<IConnectableWindow>>();

        // ドラッグ追跡。押下通知で起点を覚え、以降はマウスの総移動量から
        // 「吸着していなければあるはずの位置」(フリー位置) を毎フレーム組み立てる。
        // GUI.DragWindow はウィンドウローカルのマウス位置を基準に動くため、
        // 前フレームとの矩形差分で追う方式だと吸着による横ずれを次フレームで
        // 打ち消してしまい、ドラッグ自体が破綻する
        private IConnectableWindow _dragWindow;
        private Vector2 _dragStartPos;
        private Rect _dragStartRect;
        /// <summary>閾値を超えて実際に動かしたか。終了時の位置保存の要否判定</summary>
        private bool _dragMoved;

        /// <summary>ドラッグ対象から見た連結メンバーの相対オフセット。ドラッグ開始時に固定する</summary>
        private readonly Dictionary<IConnectableWindow, Vector2> _memberOffsets
            = new Dictionary<IConnectableWindow, Vector2>();

        /// <summary>ClampGroups の作業用。毎フレーム呼ぶためリストを使い回す</summary>
        private readonly List<IConnectableWindow> _clampTargets = new List<IConnectableWindow>();

        // 軸ごとの吸着状態。解除距離まで離れるまで吸着座標を保持し続ける
        private bool _snappedX;
        private float _snapX;
        private bool _snappedY;
        private float _snapY;

        private static WindowConnectManager _instance = null;
        public static WindowConnectManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new WindowConnectManager();
                }
                return _instance;
            }
        }

        private WindowConnectManager()
        {
        }

        /// <summary>ヘッダー/空き領域の左押下通知。ドラッグ追跡の起点</summary>
        public void OnDragMouseDown(IConnectableWindow window)
        {
            _dragWindow = window;
            _dragStartPos = InputRemapper.rawGuiPosition;
            _dragStartRect = window.windowRect;
            _dragMoved = false;
            _snappedX = false;
            _snappedY = false;

            // メンバーの相対位置を固定する。ドラッグ中にクランプ・スナップで
            // 座標補正が入っても、毎フレームこのオフセットで絶対配置し直すためずれない
            _memberOffsets.Clear();
            var group = FindGroup(window);
            if (group != null)
            {
                foreach (var member in group)
                {
                    if (member == window || IsTabCompanion(member, window))
                    {
                        continue;
                    }
                    _memberOffsets[member] = new Vector2(
                        member.windowRect.x - window.windowRect.x,
                        member.windowRect.y - window.windowRect.y);
                }
            }
        }

        /// <summary>
        /// ドラッグ追跡を外部から中断する。
        /// アプリウィンドウのリサイズ中は左ボタン押下が継続したままウィンドウ配置が
        /// スケールで動くため、追跡を続けるとドラッグしていないのにスナップが発動する。
        /// スナップは UI ウィンドウを実際にドラッグしている間だけ有効にする。
        /// 通常のドラッグ終了と異なり、中断時点の未保存移動は保存せず破棄する
        /// (現状は内部の EndDrag と同一実装。外部公開用に名前を分けている)
        /// </summary>
        public void CancelDrag()
        {
            EndDrag();
        }

        /// <summary>ドラッグ追跡を終了する</summary>
        private void EndDrag()
        {
            _dragWindow = null;
            _dragMoved = false;
            _snappedX = false;
            _snappedY = false;
            _memberOffsets.Clear();
        }

        public override void Update()
        {
            if (_dragWindow == null)
            {
                // 非ドラッグ時も画面リサイズ等でグループが画面外へ出ることがあるためクランプする
                // （ドラッグ中は絶対配置の直後にクランプするので、ここでは呼ばない）
                ClampGroups();
                return;
            }

            if (!Input.GetMouseButton(0) || !_dragWindow.isShowWnd)
            {
                // 移動ドラッグはここでしか位置が確定しないため、動かしていたら保存する
                // (保存しないとプラグイン無効化まで config に反映されず、
                //  グループ構成だけ先に保存されて再起動時に位置がずれる)
                if (_dragMoved)
                {
                    WindowManager.instance.SavePlacements();
                }
                EndDrag();
                return;
            }

            var mousePos = InputRemapper.rawGuiPosition;

            // クリック（微小移動）ではスナップも連動もさせない
            if (Vector2.Distance(mousePos, _dragStartPos) <= DRAG_THRESHOLD)
            {
                return;
            }
            _dragMoved = true;

            // 吸着していなければあるはずの位置。GUI.DragWindow の出力ではなく
            // マウスの総移動量から組み立てることで、吸着による横ずれが次フレームの
            // ドラッグ量へ混入するのを防ぐ
            var free = _dragStartRect;
            free.x += mousePos.x - _dragStartPos.x;
            free.y += mousePos.y - _dragStartPos.y;

            var rect = ApplySnap(_dragWindow, free);
            SetRect(_dragWindow, rect);

            // メンバーは差分伝播ではなく、開始時オフセットからの絶対配置で追従させる
            foreach (var pair in _memberOffsets)
            {
                var r = pair.Key.windowRect;
                r.x = rect.x + pair.Value.x;
                r.y = rect.y + pair.Value.y;
                SetRect(pair.Key, r);
            }

            ClampGroups();
        }

        /// <summary>
        /// 連結グループ全体のバウンディングボックスで画面内クランプする。
        /// 個別クランプはメンバー間のオフセットを壊すため、ウィンドウ側では
        /// 連結中ウィンドウをクランプせず、ここで群として同量だけ押し戻す
        /// </summary>
        public void ClampGroups()
        {
            foreach (var group in groups)
            {
                // 描画対象のメンバーだけを対象にする。非アクティブタブまで動かすと
                // SetRect の SyncRect がアクティブタブへ二重にシフトを返してしまう
                _clampTargets.Clear();
                var min = Vector2.zero;
                var max = Vector2.zero;
                foreach (var member in group)
                {
                    if (!member.isShowWnd || !member.isTabVisible)
                    {
                        continue;
                    }
                    var r = member.windowRect;
                    if (_clampTargets.Count == 0)
                    {
                        min = new Vector2(r.xMin, r.yMin);
                        max = new Vector2(r.xMax, r.yMax);
                    }
                    else
                    {
                        min = Vector2.Min(min, new Vector2(r.xMin, r.yMin));
                        max = Vector2.Max(max, new Vector2(r.xMax, r.yMax));
                    }
                    _clampTargets.Add(member);
                }
                if (_clampTargets.Count == 0)
                {
                    continue;
                }

                // EditorSubWindow.OnGUI の個別クランプと同じ余白ルールを群単位で適用する
                var width = max.x - min.x;
                var shiftX = Mathf.Clamp(min.x, -width + 100f, Screen.width - 100f) - min.x;
                var shiftY = Mathf.Clamp(min.y, 0f, Screen.height - EditorSubWindow.HEADER_HEIGHT) - min.y;
                if (shiftX == 0f && shiftY == 0f)
                {
                    continue;
                }

                foreach (var member in _clampTargets)
                {
                    var r = member.windowRect;
                    r.x += shiftX;
                    r.y += shiftY;
                    SetRect(member, r);
                }
            }
        }

        /// <summary>
        /// 矩形を反映する。タブグループ所属なら SyncRect で同僚タブへも同期する
        /// (非アクティブタブの矩形がずれると分離時に古い位置へ飛ぶ)
        /// </summary>
        private static void SetRect(IConnectableWindow window, Rect rect)
        {
            window.windowRect = rect;
            if (window.group != null)
            {
                window.group.SyncRect(rect);
            }
        }

        /// <summary>同じタブグループの同僚か (矩形が SyncRect 済みなので二重移動を防ぐ)</summary>
        private static bool IsTabCompanion(IConnectableWindow a, IConnectableWindow b)
        {
            return a.group != null && a.group == b.group;
        }

        /// <summary>
        /// フリー位置 free に軸ごとの吸着を反映した矩形を返す。
        /// 吸着済みの軸は SNAP_RELEASE_DISTANCE まで離れるまで座標を保持し、
        /// 離れて初めて解除する（吸着した途端に外れて戻るのを防ぐ）
        /// </summary>
        private Rect ApplySnap(IConnectableWindow dragged, Rect free)
        {
            // 解除判定はフリー位置との差で行う。吸着中は実際の矩形が
            // 吸着座標に貼り付いたままなので、そちらで測ると永久に離れない
            if (_snappedX && Mathf.Abs(free.x - _snapX) > SNAP_RELEASE_DISTANCE)
            {
                _snappedX = false;
            }
            if (_snappedY && Mathf.Abs(free.y - _snapY) > SNAP_RELEASE_DISTANCE)
            {
                _snappedY = false;
            }

            if (!_snappedX || !_snappedY)
            {
                AcquireSnap(dragged, free);
            }

            var rect = free;
            if (_snappedX)
            {
                rect.x = _snapX;
            }
            if (_snappedY)
            {
                rect.y = _snapY;
            }
            return rect;
        }

        /// <summary>
        /// 未吸着の軸について、画面枠または他ウィンドウの辺に近ければ吸着座標を確定する。
        /// 接する軸が吸着した相手にだけ、もう一方の軸の辺位置も近ければ揃える（角合わせ）
        /// </summary>
        private void AcquireSnap(IConnectableWindow dragged, Rect free)
        {
            AcquireScreenSnap(free);
            if (_snappedX && _snappedY)
            {
                return;
            }

            var group = FindGroup(dragged);

            foreach (var target in EnumerateOtherWindows(dragged))
            {
                // 一緒に動く相手には吸着しない（自分に吸い付いて離れなくなる）
                if (group != null && group.Contains(target))
                {
                    continue;
                }

                var t = target.windowRect;

                // 左右方向: 縦の範囲が重なっている相手にだけ吸着する
                if (!_snappedX && free.yMin < t.yMax && t.yMin < free.yMax)
                {
                    if (Mathf.Abs(free.xMax - t.xMin) <= SNAP_DISTANCE)
                    {
                        SetSnapX(t.xMin - free.width);
                    }
                    else if (Mathf.Abs(free.xMin - t.xMax) <= SNAP_DISTANCE)
                    {
                        SetSnapX(t.xMax);
                    }
                    // 角合わせ (上辺同士)
                    if (_snappedX && !_snappedY &&
                        Mathf.Abs(free.yMin - t.yMin) <= SNAP_DISTANCE)
                    {
                        SetSnapY(t.yMin);
                    }
                }

                // 上下方向: 横の範囲が重なっている相手にだけ吸着する
                if (!_snappedY && free.xMin < t.xMax && t.xMin < free.xMax)
                {
                    if (Mathf.Abs(free.yMax - t.yMin) <= SNAP_DISTANCE)
                    {
                        SetSnapY(t.yMin - free.height);
                    }
                    else if (Mathf.Abs(free.yMin - t.yMax) <= SNAP_DISTANCE)
                    {
                        SetSnapY(t.yMax);
                    }
                    // 角合わせ (左辺同士)
                    if (_snappedY && !_snappedX &&
                        Mathf.Abs(free.xMin - t.xMin) <= SNAP_DISTANCE)
                    {
                        SetSnapX(t.xMin);
                    }
                }

                if (_snappedX && _snappedY)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// 画面（アプリのウィンドウ枠）の四辺へ吸着させる。
        /// ウィンドウ同士の吸着より先に判定し、枠へ合わせる意図を優先する
        /// </summary>
        private void AcquireScreenSnap(Rect free)
        {
            if (!_snappedX)
            {
                if (Mathf.Abs(free.xMin) <= SNAP_DISTANCE)
                {
                    SetSnapX(0f);
                }
                else if (Mathf.Abs(free.xMax - Screen.width) <= SNAP_DISTANCE)
                {
                    SetSnapX(Screen.width - free.width);
                }
            }

            if (!_snappedY)
            {
                if (Mathf.Abs(free.yMin) <= SNAP_DISTANCE)
                {
                    SetSnapY(0f);
                }
                else if (Mathf.Abs(free.yMax - Screen.height) <= SNAP_DISTANCE)
                {
                    SetSnapY(Screen.height - free.height);
                }
            }
        }

        private void SetSnapX(float x)
        {
            _snappedX = true;
            _snapX = x;
        }

        private void SetSnapY(float y)
        {
            _snappedY = true;
            _snapY = y;
        }

        /// <summary>
        /// リサイズ中の矩形へ辺スナップを適用して返す。
        /// つかんでいる辺だけを画面枠・他ウィンドウの辺へ吸着させ、対辺は動かさない。
        /// 移動ドラッグと違い矩形はマウス位置から毎フレーム組み直されるため、
        /// 吸着結果が次フレームの入力へ混入せず、ヒステリシスなしでもばたつかない。
        /// 連結グループのメンバーも吸着候補に含める。リサイズでは相手が動かないので、
        /// 移動ドラッグ (AcquireSnap) のように自分へ吸い付いて離れなくなることはない
        /// </summary>
        public Rect SnapResize(IConnectableWindow window, Rect free, WindowResizeController.ResizeEdge edges)
        {
            var rect = free;
            float snap;

            // Rect の xMin/xMax 等のセッターは対辺を固定したまま幅・高さを更新する
            if ((edges & WindowResizeController.ResizeEdge.Left) != 0 &&
                TryGetSnapX(window, free, free.xMin, out snap))
            {
                rect.xMin = snap;
            }
            if ((edges & WindowResizeController.ResizeEdge.Right) != 0 &&
                TryGetSnapX(window, free, free.xMax, out snap))
            {
                rect.xMax = snap;
            }
            if ((edges & WindowResizeController.ResizeEdge.Top) != 0 &&
                TryGetSnapY(window, free, free.yMin, out snap))
            {
                rect.yMin = snap;
            }
            if ((edges & WindowResizeController.ResizeEdge.Bottom) != 0 &&
                TryGetSnapY(window, free, free.yMax, out snap))
            {
                rect.yMax = snap;
            }
            return rect;
        }

        /// <summary>
        /// 縦辺 (左右) の吸着先を探す。画面の左右端と、縦の範囲が重なっている
        /// 他ウィンドウの左右辺が候補。複数近い場合は最も近いものを採る。
        /// 移動ドラッグ (画面枠を先に判定して優先する) と違い最近傍優先なのは、
        /// リサイズは掴んだ辺を狙った位置へ合わせる操作で、近い候補ほど狙いに近いため
        /// </summary>
        private bool TryGetSnapX(IConnectableWindow self, Rect free, float edgeX, out float snapX)
        {
            var best = 0f;
            var bestDistance = SNAP_DISTANCE;
            var found = false;

            ConsiderSnap(edgeX, 0f, ref best, ref bestDistance, ref found);
            ConsiderSnap(edgeX, Screen.width, ref best, ref bestDistance, ref found);

            foreach (var target in EnumerateOtherWindows(self))
            {
                var t = target.windowRect;
                if (free.yMin >= t.yMax || t.yMin >= free.yMax)
                {
                    continue;
                }
                ConsiderSnap(edgeX, t.xMin, ref best, ref bestDistance, ref found);
                ConsiderSnap(edgeX, t.xMax, ref best, ref bestDistance, ref found);
            }

            snapX = best;
            return found;
        }

        /// <summary>横辺 (上下) の吸着先を探す。TryGetSnapX の縦横を入れ替えたもの</summary>
        private bool TryGetSnapY(IConnectableWindow self, Rect free, float edgeY, out float snapY)
        {
            var best = 0f;
            var bestDistance = SNAP_DISTANCE;
            var found = false;

            ConsiderSnap(edgeY, 0f, ref best, ref bestDistance, ref found);
            ConsiderSnap(edgeY, Screen.height, ref best, ref bestDistance, ref found);

            foreach (var target in EnumerateOtherWindows(self))
            {
                var t = target.windowRect;
                if (free.xMin >= t.xMax || t.xMin >= free.xMax)
                {
                    continue;
                }
                ConsiderSnap(edgeY, t.yMin, ref best, ref bestDistance, ref found);
                ConsiderSnap(edgeY, t.yMax, ref best, ref bestDistance, ref found);
            }

            snapY = best;
            return found;
        }

        /// <summary>候補が吸着距離内かつこれまでで最も近ければ採用する</summary>
        private static void ConsiderSnap(
            float edge, float candidate, ref float best, ref float bestDistance, ref bool found)
        {
            var distance = Mathf.Abs(edge - candidate);
            if (distance > bestDistance)
            {
                return;
            }
            best = candidate;
            bestDistance = distance;
            found = true;
        }

        /// <summary>
        /// このウィンドウをドラッグ中でいずれかの軸が吸着しているか。
        /// 吸着中に GUI.DragWindow を併用するとマウス追従位置と吸着位置を
        /// フレームごとに行き来して表示がばたつくため、ウィンドウ側は
        /// これが true の間 GUI.DragWindow を呼ばず、当マネージャーの配置だけに任せる
        /// </summary>
        public bool IsSnapDragging(IConnectableWindow window)
        {
            return _dragWindow == window && (_snappedX || _snappedY);
        }

        /// <summary>隣接（辺が密着）している表示中ウィンドウがあるか。コネクトボタンの表示条件</summary>
        public bool HasAdjacent(IConnectableWindow window)
        {
            foreach (var target in EnumerateOtherWindows(window))
            {
                // 連結できない相手が隣接していてもボタンを出さない (押しても連結されない)
                if (!IsConnectCapable(target))
                {
                    continue;
                }
                if (AreAdjacent(window.windowRect, target.windowRect))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>辺同士が ADJACENT_EPSILON 以内で密着しているか（四方向）</summary>
        private static bool AreAdjacent(Rect a, Rect b)
        {
            var overlapV = a.yMin < b.yMax - ADJACENT_EPSILON && b.yMin < a.yMax - ADJACENT_EPSILON;
            var overlapH = a.xMin < b.xMax - ADJACENT_EPSILON && b.xMin < a.xMax - ADJACENT_EPSILON;

            if (overlapV &&
                (Mathf.Abs(a.xMax - b.xMin) <= ADJACENT_EPSILON ||
                 Mathf.Abs(b.xMax - a.xMin) <= ADJACENT_EPSILON))
            {
                return true;
            }
            if (overlapH &&
                (Mathf.Abs(a.yMax - b.yMin) <= ADJACENT_EPSILON ||
                 Mathf.Abs(b.yMax - a.yMin) <= ADJACENT_EPSILON))
            {
                return true;
            }
            return false;
        }

        public bool IsConnected(IConnectableWindow window)
        {
            return FindGroup(window) != null;
        }

        /// <summary>
        /// 連結トグル。未連結なら隣接する全ウィンドウ（の所属グループ含む）と連結し、
        /// 連結中なら自分だけグループから外す
        /// </summary>
        public void ToggleConnect(IConnectableWindow window)
        {
            if (IsConnected(window))
            {
                Disconnect(window);
            }
            else
            {
                ConnectToAdjacent(window);
            }
            MarkGroupsDirty();
        }

        private void ConnectToAdjacent(IConnectableWindow window)
        {
            var merged = new List<IConnectableWindow> { window };

            foreach (var target in EnumerateOtherWindows(window))
            {
                if (!IsConnectCapable(target))
                {
                    continue;
                }
                if (!AreAdjacent(window.windowRect, target.windowRect))
                {
                    continue;
                }
                var targetGroup = FindGroup(target);
                if (targetGroup != null)
                {
                    // 相手が既存グループ所属なら丸ごと取り込む
                    foreach (var member in targetGroup)
                    {
                        if (!merged.Contains(member))
                        {
                            merged.Add(member);
                        }
                    }
                    groups.Remove(targetGroup);
                }
                else if (!merged.Contains(target))
                {
                    merged.Add(target);
                }
            }

            if (merged.Count >= 2)
            {
                groups.Add(merged);
            }
        }

        /// <summary>
        /// タブドッキングで 2 ウィンドウが 1 枚へ統合されたときの整合取り。
        /// 双方が別々のコネクトグループに属していると「1 ウィンドウは高々 1 グループ」
        /// という FindGroup の前提が崩れ、片方のグループが参照されなくなって
        /// メンバーが取り残される。統合後は必ず一緒に動く以上、グループも 1 つへまとめる
        /// </summary>
        public void OnTabMerged(IConnectableWindow source, IConnectableWindow target)
        {
            var sourceGroup = FindGroup(source);
            var targetGroup = FindGroup(target);
            if (sourceGroup == null || targetGroup == null || sourceGroup == targetGroup)
            {
                return;
            }

            foreach (var member in sourceGroup)
            {
                if (!targetGroup.Contains(member))
                {
                    targetGroup.Add(member);
                }
            }
            groups.Remove(sourceGroup);
            MarkGroupsDirty();
        }

        private void Disconnect(IConnectableWindow window)
        {
            var group = FindGroup(window);
            if (group == null)
            {
                return;
            }

            // タブグループ所属なら同僚ごと外す（ノードはタブグループ丸ごと 1 ウィンドウ扱い）
            group.Remove(window);
            if (window.group != null)
            {
                foreach (var companion in window.group.windows)
                {
                    group.Remove(companion);
                }
            }

            if (group.Count <= 1)
            {
                groups.Remove(group);
            }
        }

        /// <summary>
        /// 非表示になったウィンドウをグループから外す。
        /// save=false はプラグイン終了時の片付け用。構成変更ではないので config へ書き戻さない
        /// (保存済みの構成を空で上書きしてしまい、次回の復元が効かなくなる。TabGroupManager と同じ流儀)
        /// </summary>
        public void OnWindowHidden(IConnectableWindow window, bool save = true)
        {
            if (IsConnected(window))
            {
                Disconnect(window);
                if (save)
                {
                    MarkGroupsDirty();
                }
            }
            if (_dragWindow == window)
            {
                EndDrag();
            }
        }

        /// <summary>
        /// コネクト（連結移動）の候補にできるか。内部ウィンドウは常に可。
        /// 外部ウィンドウは EnableConnect 宣言済みのみ可
        /// (未宣言ゲストは連結中の個別クランプ抑止ができず群クランプと競合する)。
        /// スナップ吸着の相手になるだけならこの判定は不要
        /// </summary>
        private static bool IsConnectCapable(IConnectableWindow window)
        {
            var adapter = window as ExternalWindowAdapter;
            return adapter == null || adapter.connectCapable;
        }

        /// <summary>
        /// 所属グループを探す。タブグループ所属ウィンドウは同僚がメンバーでもヒットさせる
        /// (連結後にアクティブタブが切り替わると別インスタンスが操作対象になるため、
        /// インスタンス一致だけでは連動・ボタン表示が途切れる)
        /// </summary>
        private List<IConnectableWindow> FindGroup(IConnectableWindow window)
        {
            foreach (var group in groups)
            {
                if (group.Contains(window))
                {
                    return group;
                }
                if (window.group != null)
                {
                    foreach (var companion in window.group.windows)
                    {
                        if (group.Contains(companion))
                        {
                            return group;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// スナップ/コネクトの候補になる全ウィンドウ (内部 + 外部プラグイン登録窓) を列挙する。
        /// フィルタ条件を一箇所にまとめるため、列挙源の合成はここだけで行う
        /// </summary>
        private IEnumerable<IConnectableWindow> EnumerateAllConnectables()
        {
            foreach (var w in WindowManager.instance.windows)
            {
                var target = w as IConnectableWindow;
                if (target != null)
                {
                    yield return target;
                }
            }
            foreach (var target in DockingHost.EnumerateExternalConnectables())
            {
                yield return target;
            }
        }

        /// <summary>表示中かつ描画対象（独立 or アクティブタブ）の他ウィンドウを列挙する</summary>
        private IEnumerable<IConnectableWindow> EnumerateOtherWindows(IConnectableWindow self)
        {
            foreach (var target in EnumerateAllConnectables())
            {
                if (target == self || !target.isShowWnd || !target.isTabVisible)
                {
                    continue;
                }
                yield return target;
            }
        }

        /// <summary>グループ構成の変更を config へ反映して保存を予約する (TabGroupManager と同じ流儀)</summary>
        private void MarkGroupsDirty()
        {
            SaveGroups();
            config.dirty = true;
        }

        /// <summary>全コネクトグループを解除する (レイアウト適用時のリセット用)</summary>
        public void DisconnectAll()
        {
            // ドラッグ追跡が古いグループ構成を参照し続けないよう先に中断する
            CancelDrag();
            groups.Clear();
        }

        /// <summary>グループ構成を config へ書き出す</summary>
        public void SaveGroups()
        {
            config.connectGroups.Clear();
            foreach (var group in groups)
            {
                var ids = new List<string>(group.Count);
                foreach (var window in group)
                {
                    ids.Add(window.tabWindowId.ToString());
                }
                config.connectGroups.Add(string.Join(",", ids.ToArray()));
            }
        }

        /// <summary>config のグループ構成を復元する。不明な ID や非表示ウィンドウは無視する</summary>
        public void RestoreGroups()
        {
            foreach (var entry in config.connectGroups)
            {
                var members = new List<IConnectableWindow>();
                foreach (var idText in entry.Split(','))
                {
                    int id;
                    if (!int.TryParse(idText, out id))
                    {
                        continue;
                    }
                    var window = FindWindow(id);
                    if (window != null && window.isShowWnd && IsConnectCapable(window) &&
                        FindGroup(window) == null && !members.Contains(window))
                    {
                        members.Add(window);
                    }
                }

                if (members.Count >= 2)
                {
                    groups.Add(members);
                }
            }
        }

        private IConnectableWindow FindWindow(int tabWindowId)
        {
            foreach (var sub in EnumerateAllConnectables())
            {
                if (sub.tabWindowId == tabWindowId)
                {
                    return sub;
                }
            }
            return null;
        }

        public override void OnPluginDisable()
        {
            EndDrag();
        }
    }
}
