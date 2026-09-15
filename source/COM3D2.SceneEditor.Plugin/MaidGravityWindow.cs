using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// スタジオモードの重力ウィンドウ相当。
    /// 髪・スカートの揺れものにかかる力の向きをカテゴリ単位で編集する
    /// </summary>
    public class MaidGravityWindow : MaidWindowBase
    {
        public static readonly int WINDOW_ID = 8903380;

        private static readonly int TAB_WIDTH = 80;

        /// <summary>ウィンドウ内の内部タブ。MaidGravityController.categories と同じ並び</summary>
        private enum GravityTabType
        {
            髪,
            スカート,
        }

        private GravityTabType _tabType = GravityTabType.髪;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "重力";

        private static MaidGravityWindow _instance = null;
        public static MaidGravityWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new MaidGravityWindow();
                }
                return _instance;
            }
        }

        private MaidGravityWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.maidGravityPosX;
            y = config.maidGravityPosY;
            width = config.maidGravityWidth;
            height = config.maidGravityHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.maidGravityPosX = x;
            config.maidGravityPosY = y;
            config.maidGravityWidth = width;
            config.maidGravityHeight = height;
        }

        public override bool savedVisible
        {
            get => config.maidGravityVisible;
            set => config.maidGravityVisible = value;
        }

        protected override void DrawMaidContent(Maid target)
        {
            if (target == null)
            {
                return;
            }
            if (target.body0 == null || !target.body0.isLoadedBody)
            {
                view.DrawLabel("ボディの読み込みを待っています", -1, ROW_HEIGHT);
                return;
            }

            _tabType = DrawInnerTabs(_tabType, TAB_WIDTH);

            view.DrawHorizontalLine(Color.gray);
            view.AddSpace(5);

            // タブ切替はゲートの対象外にするため、タブを描いた後で判定する
            TimelineLayerGate.Begin(view, typeof(MTEP.GravityTimelineLayer), target, ROW_HEIGHT);

            // 最後の要素なので高さ -1（残り全部）でウィンドウの伸縮に追従させる
            view.BeginScrollView(-1, -1, GUIView.AutoScrollViewRect, false, true);

            GravityRowDrawer.Draw(view, target, MaidGravityController.categories[(int)_tabType], ROW_HEIGHT);

            view.EndScrollView();
        }
    }
}
