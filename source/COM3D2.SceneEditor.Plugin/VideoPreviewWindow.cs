using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 表示形式が「プレビュー」の動画をウィンドウ内に描画する。
    /// ゲーム画面には出さずここだけに映すための表示形式で、
    /// 表示サイズ (ウィンドウサイズ比) と透過度を反映する
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

            var prevColor = GUI.color;
            GUI.color = BackgroundColor;
            GUI.DrawTexture(localRect, Texture2D.whiteTexture);
            GUI.color = prevColor;

            // 動画本数を減らすと番号だけ残るため、参照する前に本数を確認する
            if (!movieManager.IsValidIndex(videoIndex))
            {
                _view.Init(localRect);
                _view.DrawLabel("この番号の動画はありません", -1, ROW_HEIGHT, textColor: Color.gray);
                return;
            }

            var settings = movieManager.GetSettings(videoIndex);
            if (settings.displayType != MTEP.VideoDisplayType.GUI)
            {
                _view.Init(localRect);
                _view.DrawLabel("表示形式が「プレビュー」ではありません", -1, ROW_HEIGHT, textColor: Color.gray);
                return;
            }

            // メタデータ確定前はサイズ 0 のダミーが返ることがあり、そのままだと CoverRect が NaN になる
            var texture = movieManager.GetTexture(videoIndex);
            if (texture == null || texture.width <= 0 || texture.height <= 0)
            {
                _view.Init(localRect);
                _view.DrawLabel("動画が読み込まれていません", -1, ROW_HEIGHT, textColor: Color.gray);
                return;
            }

            // MediaFoundation 等ではテクスチャが上下反転しているため UV 側で戻す
            var texCoords = movieManager.RequiresVerticalFlip(videoIndex)
                ? new Rect(0f, 1f, 1f, -1f)
                : new Rect(0f, 0f, 1f, 1f);

            // 領域いっぱいまで拡大するため、はみ出した分はグループでクリップする
            var drawRect = CoverRect(localRect, (float)texture.width / texture.height, settings.guiScale);
            drawRect.x -= localRect.x;
            drawRect.y -= localRect.y;

            GUI.BeginGroup(localRect);
            {
                // 透過度は動画にだけ効かせ、グリッドは設定どおりの色で描く
                var prevTextureColor = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, settings.guiAlpha);
                GUI.DrawTextureWithTexCoords(drawRect, texture, texCoords, true);
                GUI.color = prevTextureColor;

                if (config.isGridVisibleInVideo && GridRenderer.isGridEnabled)
                {
                    DrawGrid(drawRect);
                }
            }
            GUI.EndGroup();
        }

        /// <summary>
        /// 動画面を等分するグリッドを 1px 線で重ねる。
        /// 3D 表示の動画面に MoviePlayerImpl が描くものと同じ設定を使い、
        /// ゲーム画面に動画面を持たないプレビュー形式でもここで確認できるようにする
        /// </summary>
        private void DrawGrid(Rect videoRect)
        {
            var count = Mathf.Max(config.gridCountInVideo, 1);
            var color = config.gridColorInVideo;
            color.a = config.gridAlphaInVideo;

            var prevColor = GUI.color;
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
        /// 領域をアスペクト比を保って覆う中央寄せ矩形を返す。短辺側は領域からはみ出す。
        /// scale はウィンドウサイズに対する表示倍率 (1 で領域いっぱい)
        /// </summary>
        private static Rect CoverRect(Rect area, float aspectRatio, float scale)
        {
            var width = area.width;
            var height = width / aspectRatio;
            if (height < area.height)
            {
                height = area.height;
                width = height * aspectRatio;
            }

            width *= scale;
            height *= scale;

            return new Rect(
                area.x + (area.width - width) * 0.5f,
                area.y + (area.height - height) * 0.5f,
                width,
                height);
        }
    }
}
