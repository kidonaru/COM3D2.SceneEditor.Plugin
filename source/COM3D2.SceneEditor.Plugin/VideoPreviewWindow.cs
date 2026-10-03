using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 動画をウィンドウ内に描画する。表示形式や有効・無効に関係なく
    /// MediaPlayer のテクスチャを直接描くため、ゲーム画面に出していない動画も確認できる。
    /// 表示形式「プレビュー」のときだけ表示サイズ (ウィンドウサイズ比) と透過度を反映する
    /// </summary>
    public class VideoPreviewWindow : EditorSubWindow
    {
        /// <summary>
        /// 先頭の ID。動画の添字を足したものを各ウィンドウの ID にするため、
        /// WINDOW_ID 〜 WINDOW_ID + MaxVideoCount - 1 を予約済みとして扱う
        /// </summary>
        public static readonly int WINDOW_ID = 8903397;

        protected override int windowId => WINDOW_ID + videoIndex;
        /// <summary>番号は VideoWindow の操作対象コンボと同じ 1 始まり</summary>
        protected override string windowTitle => "動画プレビュー (" + (videoIndex + 1) + ")";

        /// <summary>このウィンドウが表示する動画の添字</summary>
        private readonly int videoIndex;

        private static readonly int ROW_HEIGHT = 20;

        /// <summary>動画未読込時の背景 (レターボックスと共通)</summary>
        private static readonly Color BackgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);

        private static MTEP.MovieManager movieManager => MTEP.MovieManager.instance;

        private readonly GUIView _view = new GUIView();

        // ツールバー項目の幅
        private const float ZOOM_BUTTON_WIDTH = 24f;
        private const float ZOOM_LABEL_WIDTH = 44f;
        private const float RESET_BUTTON_WIDTH = 56f;

        private readonly GUIView _toolbarView = ViewToolbarDrawer.CreateView(FRAME);

        // 表示確認用のズーム・位置ずらし。動画の設定ではないので保存しない
        private float _zoom = 1f;
        private Vector2 _pan = Vector2.zero;

        // 位置ずらしのドラッグ。開始時の値からの差で求める (理由は VideoPreviewViewMath.DragPan)
        private int _panControlId = 0;
        private Vector2 _dragStartMouse;
        private Vector2 _dragStartPan;

        /// <summary>直近に描いた動画の等倍サイズ。ツールバーのズームでも位置ずらしの範囲に使う</summary>
        private Vector2 _baseSize = Vector2.zero;

        /// <summary>動画を描けているか。描けていないときはツールバーも入力も無効にする</summary>
        private bool _hasVideo = false;

        /// <summary>
        /// 表示サイズと透過度は表示形式「プレビュー」専用の設定なので、
        /// ゲーム画面にも出す表示形式では等倍・不透明で描く
        /// </summary>
        private bool usesPreviewSettings
            => movieManager.IsValidIndex(videoIndex)
                && movieManager.GetSettings(videoIndex).displayType == MTEP.VideoDisplayType.GUI;

        private float previewScale
            => usesPreviewSettings ? movieManager.GetSettings(videoIndex).guiScale : 1f;

        protected override float windowAlpha
            => usesPreviewSettings ? movieManager.GetSettings(videoIndex).guiAlpha : 1f;

        /// <summary>内容の左ドラッグは位置ずらしに使うため、ウィンドウの移動はヘッダーだけにする</summary>
        protected override bool allowContentDrag => false;

        private static VideoPreviewWindow[] _instances = null;

        /// <summary>動画本数の上限ぶんのウィンドウ。登録とメニュー生成で使う</summary>
        public static VideoPreviewWindow[] instances
        {
            get
            {
                if (_instances == null)
                {
                    _instances = new VideoPreviewWindow[MTEP.MovieManager.MaxVideoCount];
                    for (var i = 0; i < _instances.Length; i++)
                    {
                        _instances[i] = new VideoPreviewWindow(i);
                    }
                }
                return _instances;
            }
        }

        public static VideoPreviewWindow GetInstance(int index)
        {
            var list = instances;
            return list[Mathf.Clamp(index, 0, list.Length - 1)];
        }

        private VideoPreviewWindow(int videoIndex)
        {
            this.videoIndex = videoIndex;
        }

        private Config.VideoPreviewPlacement placement => config.GetVideoPreview(videoIndex);

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            var placement = this.placement;
            x = placement.posX;
            y = placement.posY;
            width = placement.width;
            height = placement.height;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            var placement = this.placement;
            placement.posX = x;
            placement.posY = y;
            placement.width = width;
            placement.height = height;
        }

        public override bool savedVisible
        {
            get => placement.visible;
            set => placement.visible = value;
        }

        protected override void DrawContent()
        {
            var localRect = ToLocalRect(contentRect);

            // コントロール ID はイベントごとに同じ順で取る必要があるため、早期 return より前で取る
            _panControlId = GUIUtility.GetControlID(FocusType.Passive);
            _hasVideo = false;

            // 動画を失ってもドラッグを解放できるよう、離す処理だけは早期 return より前で受ける
            var e = Event.current;
            if (e.GetTypeForControl(_panControlId) == EventType.MouseUp &&
                GUIUtility.hotControl == _panControlId)
            {
                GUIUtility.hotControl = 0;
                e.Use();
            }

            // ウィンドウ全体に掛かっている不透明度 (windowAlpha) を打ち消さないよう掛け合わせる
            var prevColor = GUI.color;
            GUI.color = new Color(
                BackgroundColor.r, BackgroundColor.g, BackgroundColor.b, BackgroundColor.a * prevColor.a);
            GUI.DrawTexture(localRect, Texture2D.whiteTexture);
            GUI.color = prevColor;

            // 動画本数を減らすと番号だけ残るため、参照する前に本数を確認する
            if (!movieManager.IsValidIndex(videoIndex))
            {
                DrawPlaceholder(localRect, "この番号の動画はありません");
                return;
            }

            // メタデータ確定前はサイズ 0 のダミーが返ることがあり、そのままだと CoverRect が NaN になる
            var texture = movieManager.GetTexture(videoIndex);
            if (texture == null || texture.width <= 0 || texture.height <= 0)
            {
                DrawPlaceholder(localRect, "動画が読み込まれていません");
                return;
            }

            // MediaFoundation 等ではテクスチャが上下反転しているため UV 側で戻す
            var texCoords = movieManager.RequiresVerticalFlip(videoIndex)
                ? new Rect(0f, 1f, 1f, -1f)
                : new Rect(0f, 0f, 1f, 1f);

            _hasVideo = true;
            _baseSize = CoverSize(localRect, (float)texture.width / texture.height, previewScale);
            HandleViewInput(localRect);

            // 領域いっぱいまで拡大するため、はみ出した分はグループでクリップする
            var drawRect = VideoPreviewViewMath.ApplyView(_baseSize, localRect, _zoom, _pan);
            drawRect.x -= localRect.x;
            drawRect.y -= localRect.y;

            GUI.BeginGroup(localRect);
            {
                // 透過度はウィンドウ全体 (windowAlpha) に掛かっているのでここでは触らない
                GUI.DrawTextureWithTexCoords(drawRect, texture, texCoords, true);

                if (config.isGridVisibleInVideo && GridRenderer.isGridEnabled)
                {
                    DrawGrid(drawRect);
                }
            }
            GUI.EndGroup();
        }

        /// <summary>
        /// 動画を描けないときの案内文。
        /// DrawLabel は色指定つきだと GUI.color を白へ戻すため、
        /// ウィンドウ全体に掛けた不透明度 (windowAlpha) を自前で戻す
        /// </summary>
        private void DrawPlaceholder(Rect localRect, string message)
        {
            var prevColor = GUI.color;

            _view.Init(localRect);
            _view.DrawLabel(message, -1, ROW_HEIGHT, textColor: Color.gray);

            GUI.color = prevColor;
        }

        /// <summary>
        /// 動画面を等分するグリッドを 1px 線で重ねる。
        /// 3D 表示の動画面に MoviePlayerImpl が描くものと同じ設定を使い、
        /// ゲーム画面に動画面を持たないプレビュー形式でもここで確認できるようにする
        /// </summary>
        private void DrawGrid(Rect videoRect)
        {
            var count = Mathf.Max(config.gridCountInVideo, 1);
            var prevColor = GUI.color;

            var color = config.gridColorInVideo;
            color.a = config.gridAlphaInVideo * prevColor.a;

            GUI.color = color;

            // 外周は動画の縁と重なるだけなので画面分割グリッドと同じく描かない
            for (var i = 1; i < count; i++)
            {
                var ratio = (float)i / count;
                var x = videoRect.x + videoRect.width * ratio;
                var y = videoRect.y + videoRect.height * ratio;
                GUI.DrawTexture(new Rect(x - 0.5f, videoRect.y, 1f, videoRect.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(videoRect.x, y - 0.5f, videoRect.width, 1f), Texture2D.whiteTexture);
            }

            GUI.color = prevColor;
        }

        /// <summary>
        /// 領域をアスペクト比を保って覆うサイズを返す。短辺側は領域からはみ出す。
        /// scale はウィンドウサイズに対する表示倍率 (1 で領域いっぱい)
        /// </summary>
        private static Vector2 CoverSize(Rect area, float aspectRatio, float scale)
        {
            var width = area.width;
            var height = width / aspectRatio;
            if (height < area.height)
            {
                height = area.height;
                width = height * aspectRatio;
            }

            return new Vector2(width, height) * scale;
        }

        /// <summary>ツールバーの帯 (ウィンドウローカル座標)。内容領域の上端に重ねる</summary>
        private static Rect GetToolbarLocalRect(Rect localRect)
        {
            // 項目: - / 倍率 / + / リセット。マージンは項目間の 3 箇所分
            var width = FRAME * 2 + ViewToolbarDrawer.ITEM_MARGIN * 3
                + ZOOM_BUTTON_WIDTH * 2 + ZOOM_LABEL_WIDTH + RESET_BUTTON_WIDTH;
            return new Rect(localRect.x, localRect.y, width, ViewToolbarDrawer.TOOLBAR_HEIGHT);
        }

        /// <summary>ツールバーを出すか。動画を描けていて、マウスがこのウィンドウの上にある間</summary>
        private bool IsToolbarVisible()
        {
            if (!_hasVideo)
            {
                return false;
            }
            var guiPos = InputRemapper.rawGuiPosition;
            return windowRect.Contains(guiPos) &&
                !GuiWindowTracker.IsOverWindowExcept(windowId, guiPos);
        }

        /// <summary>
        /// ホイールでカーソル位置を中心にズームし、左ドラッグで位置をずらす。
        /// ドラッグはホットコントロールで捕まえ、ウィンドウ外へ出ても続ける。
        /// ウィンドウ外のマウスイベントは e.type が Ignore になるため、GetTypeForControl で読む
        /// </summary>
        private void HandleViewInput(Rect localRect)
        {
            var e = Event.current;
            switch (e.GetTypeForControl(_panControlId))
            {
                case EventType.ScrollWheel:
                    if (localRect.Contains(e.mousePosition) && e.delta.y != 0f)
                    {
                        ZoomAt(localRect, e.mousePosition, e.delta.y < 0f ? 1 : -1);
                        e.Use();
                    }
                    break;

                case EventType.MouseDown:
                    // ツールバーのボタンとリサイズのつかみ範囲は後段で処理するので奪わない
                    if (e.button == 0 &&
                        localRect.Contains(e.mousePosition) &&
                        !(IsToolbarVisible() && GetToolbarLocalRect(localRect).Contains(e.mousePosition)) &&
                        !IsOverResizeHandle(InputRemapper.rawGuiPosition))
                    {
                        GUIUtility.hotControl = _panControlId;
                        _dragStartMouse = e.mousePosition;
                        _dragStartPan = _pan;
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == _panControlId)
                    {
                        _pan = VideoPreviewViewMath.DragPan(
                            _dragStartPan, localRect, _baseSize * _zoom, e.mousePosition - _dragStartMouse);
                        e.Use();
                    }
                    break;
            }
        }

        /// <summary>
        /// 描かれなくなるとドラッグを離すイベントを受けられないため、
        /// 握ったままのホットコントロールを手放す (残すと他のコントロールが押せなくなる)
        /// </summary>
        private void CancelPanDrag()
        {
            if (_panControlId != 0 && GUIUtility.hotControl == _panControlId)
            {
                GUIUtility.hotControl = 0;
            }
        }

        protected override void OnShowChanged(bool visible)
        {
            if (!visible)
            {
                CancelPanDrag();
            }
        }

        protected override void OnTabVisibleChanged(bool visible)
        {
            if (!visible)
            {
                CancelPanDrag();
            }
        }

        private void ZoomAt(Rect localRect, Vector2 cursor, int direction)
        {
            var newZoom = VideoPreviewViewMath.StepZoom(_zoom, direction);
            _pan = VideoPreviewViewMath.ZoomAt(_pan, localRect, _baseSize, cursor, _zoom, newZoom);
            _zoom = newZoom;
        }

        private void ResetView()
        {
            _zoom = 1f;
            _pan = Vector2.zero;
        }

        /// <summary>ズームの - / 倍率 / + / リセット。動画の上に重ね、マウスオーバー中だけ出す</summary>
        protected override void DrawToolbar()
        {
            if (!IsToolbarVisible())
            {
                return;
            }

            var localRect = ToLocalRect(contentRect);
            var rect = GetToolbarLocalRect(localRect);
            ViewToolbarDrawer.DrawBackground(rect);

            var view = _toolbarView;
            view.Init(rect.x, rect.y, rect.width, rect.height);
            view.BeginHorizontal();

            // ボタンからのズームは表示領域の中心を基準にする
            var height = ViewToolbarDrawer.ITEM_HEIGHT;
            if (view.DrawButton("-", ZOOM_BUTTON_WIDTH, height))
            {
                ZoomAt(localRect, localRect.center, -1);
            }
            view.DrawLabel(VideoPreviewViewMath.FormatZoom(_zoom), ZOOM_LABEL_WIDTH, height,
                style: GUIView.gsLabelRight);
            if (view.DrawButton("+", ZOOM_BUTTON_WIDTH, height))
            {
                ZoomAt(localRect, localRect.center, 1);
            }
            if (view.DrawButton("リセット", RESET_BUTTON_WIDTH, height))
            {
                ResetView();
            }

            view.EndLayout();
        }
    }
}
