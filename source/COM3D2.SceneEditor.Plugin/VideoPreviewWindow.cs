using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// 再生中の動画をウィンドウ内に表示するプレビュー。
    /// 表示形式 (GUI / 3D / 最背面 / 最前面) に関係なく、MediaPlayer のテクスチャを直接描くため
    /// ゲーム画面上で見えにくい配置でも内容を確認できる
    /// </summary>
    public class VideoPreviewWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903397;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "動画プレビュー";

        private static readonly int ROW_HEIGHT = 20;

        /// <summary>動画未読込時の背景 (レターボックスと共通)</summary>
        private static readonly Color BackgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);

        private static MTEP.MovieManager movieManager => MTEP.MovieManager.instance;

        private readonly GUIView _view = new GUIView();

        private static VideoPreviewWindow _instance = null;
        public static VideoPreviewWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new VideoPreviewWindow();
                }
                return _instance;
            }
        }

        private VideoPreviewWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.videoPreviewPosX;
            y = config.videoPreviewPosY;
            width = config.videoPreviewWidth;
            height = config.videoPreviewHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.videoPreviewPosX = x;
            config.videoPreviewPosY = y;
            config.videoPreviewWidth = width;
            config.videoPreviewHeight = height;
        }

        public override bool savedVisible
        {
            get => config.videoPreviewVisible;
            set => config.videoPreviewVisible = value;
        }

        protected override void DrawContent()
        {
            var localRect = ToLocalRect(contentRect);

            var prevColor = GUI.color;
            GUI.color = BackgroundColor;
            GUI.DrawTexture(localRect, Texture2D.whiteTexture);
            GUI.color = prevColor;

            // メタデータ確定前はサイズ 0 のダミーが返ることがあり、そのままだと FitRect が NaN になる
            var texture = movieManager.texture;
            if (texture == null || texture.width <= 0 || texture.height <= 0)
            {
                _view.Init(localRect);
                _view.DrawLabel("動画が読み込まれていません", -1, ROW_HEIGHT, textColor: Color.gray);
                return;
            }

            var drawRect = FitRect(localRect, (float)texture.width / texture.height);

            // MediaFoundation 等ではテクスチャが上下反転しているため UV 側で戻す
            var texCoords = movieManager.requiresVerticalFlip
                ? new Rect(0f, 1f, 1f, -1f)
                : new Rect(0f, 0f, 1f, 1f);
            GUI.DrawTextureWithTexCoords(drawRect, texture, texCoords, false);

            if (config.isGridVisibleInVideo && GridRenderer.isGridEnabled)
            {
                DrawGrid(drawRect);
            }
        }

        /// <summary>
        /// 動画面を等分するグリッドを 1px 線で重ねる。
        /// 3D 表示の動画面に MoviePlayerImpl が描くものと同じ設定を使い、
        /// GUI 表示 (面に重ねられない) でもここで確認できるようにする
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

        /// <summary>領域内にアスペクト比を保って収まる中央寄せ矩形を返す</summary>
        private static Rect FitRect(Rect area, float aspectRatio)
        {
            var width = area.width;
            var height = width / aspectRatio;
            if (height > area.height)
            {
                height = area.height;
                width = height * aspectRatio;
            }
            return new Rect(
                area.x + (area.width - width) * 0.5f,
                area.y + (area.height - height) * 0.5f,
                width,
                height);
        }
    }
}
