using System;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// レイヤー固有の編集 UI (ITimelineLayer.DrawWindow) を描くホストウィンドウ。
    /// 中身は各レイヤーの実装に委ねる薄いホストで、
    /// ここではレイヤーと操作対象メイドの選択だけを持つ
    /// </summary>
    public class TimelineLayerWindow : EditorSubWindow
    {
        public static readonly int WINDOW_ID = 8903387;

        protected override int windowId => WINDOW_ID;
        protected override string windowTitle => "レイヤー編集";
        // 内部スクロールを持たないレイヤー (SubCamera 等) の項目が隠れないよう、
        // 既定の下限 (200x160) より大きく取る
        protected override int minWidth => 400;
        protected override int minHeight => 400;

        private static readonly int ROW_HEIGHT = 20;

        /// <summary>直近に描画で例外を投げたレイヤー。毎フレーム同じ例外を出し続けないよう記録する</summary>
        private Type _drawFailedLayerType = null;

        private readonly GUIView _view = new GUIView();

        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.TimelineData timeline => timelineManager.timeline;
        private static MTEP.ITimelineLayer currentLayer => timelineManager.currentLayer;
        private static MTEP.MaidManager maidManager => MTEP.MaidManager.instance;

        private readonly GUIComboBox<MTEP.TimelineLayerInfo> _layerComboBox
            = new GUIComboBox<MTEP.TimelineLayerInfo>
        {
            getName = (layerInfo, index) => layerInfo.displayName,
            onSelected = (layerInfo, index) =>
            {
                instance.ResetCurrentLayerDraw();
                timelineManager.ChangeActiveLayer(layerInfo.layerType, maidManager.maidSlotNo);
            },
            contentSize = new Vector2(150, 300),
        };

        private readonly GUIComboBox<MTEP.MaidCache> _maidComboBox = new GUIComboBox<MTEP.MaidCache>
        {
            getName = (maidCache, _) => maidCache == null ? "未選択" : maidCache.fullName,
            onSelected = (maidCache, index) =>
            {
                instance.ResetCurrentLayerDraw();
                maidManager.ChangeMaid(maidCache.maid);
            },
            buttonSize = new Vector2(150, 20),
            contentSize = new Vector2(150, 300),
        };

        private static TimelineLayerWindow _instance = null;
        public static TimelineLayerWindow instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TimelineLayerWindow();
                }
                return _instance;
            }
        }

        private TimelineLayerWindow()
        {
        }

        protected override void LoadPlacement(out int x, out int y, out int width, out int height)
        {
            x = config.timelineLayerPosX;
            y = config.timelineLayerPosY;
            width = config.timelineLayerWidth;
            height = config.timelineLayerHeight;
        }

        protected override void StorePlacement(int x, int y, int width, int height)
        {
            config.timelineLayerPosX = x;
            config.timelineLayerPosY = y;
            config.timelineLayerWidth = width;
            config.timelineLayerHeight = height;
        }

        public override bool savedVisible
        {
            get => config.timelineLayerVisible;
            set => config.timelineLayerVisible = value;
        }

        protected override void DrawContent()
        {
            DrawBody();

            // 各レイヤーの DrawWindow はコンボのポップアップ描画をホストに委ねているため、
            // 早期 return しても飛ばさないよう本体とは分けて必ず呼ぶ
            ComboBoxPopupWindow.instance.ProcessFocus(_view, this);
        }

        private void DrawBody()
        {
            _view.Init(ToLocalRect(contentRect));

            if (timeline == null || currentLayer == null)
            {
                _view.DrawLabel("タイムラインが読み込まれていません", -1, ROW_HEIGHT, Color.yellow);
                return;
            }

            DrawHeader();

            DrawLayerWindow();
        }

        /// <summary>
        /// レイヤー側の編集 UI を描く。
        /// 各レイヤーの DrawWindow は本ウィンドウが初めて呼ぶ経路のため、
        /// 1 レイヤーの不具合で編集セッション全体が止まらないよう例外を切り離す
        /// </summary>
        private void DrawLayerWindow()
        {
            var layerType = currentLayer.layerType;

            try
            {
                // DrawWindow は内部で自前のスクロールビューを張るため、ここでは包まない
                // (ネストしたスクロールは GUIView が非対応)
                currentLayer.DrawWindow(_view);

                if (_drawFailedLayerType == layerType)
                {
                    _drawFailedLayerType = null;
                }
            }
            catch (Exception e)
            {
                // 毎フレーム描画されるため、同じレイヤーのログは 1 回だけ出す
                if (_drawFailedLayerType != layerType)
                {
                    _drawFailedLayerType = layerType;
                    MTEUtils.LogError("レイヤーの編集 UI を描画できませんでした: {0}", layerType.Name);
                    MTEUtils.LogException(e);
                }
            }

            if (_drawFailedLayerType == layerType)
            {
                _view.DrawLabel("このレイヤーの編集 UI でエラーが発生しました", -1, ROW_HEIGHT, Color.yellow);
            }
        }

        /// <summary>
        /// レイヤー / 操作対象の切替時に、切り替え前のレイヤーへ描画状態のリセットを通知する。
        /// 既定実装は何もしないが、状態を持つレイヤーが出たときに切替が反映されるようにしておく
        /// </summary>
        private void ResetCurrentLayerDraw()
        {
            currentLayer?.ResetDraw(_view);
            _drawFailedLayerType = null;
        }

        /// <summary>編集対象のレイヤーと操作対象メイドの選択</summary>
        private void DrawHeader()
        {
            _view.BeginHorizontal();
            {
                _view.margin = 0;

                _layerComboBox.currentItem = timelineManager.GetLayerInfo(currentLayer.layerType);
                _layerComboBox.items = timelineManager.usingLayerInfoList;
                _layerComboBox.DrawButton("レイヤー", _view);

                if (currentLayer.hasSlotNo)
                {
                    _view.AddSpace(10);
                    _view.DrawLabel("操作対象", 60, ROW_HEIGHT);

                    _maidComboBox.currentIndex = currentLayer.slotNo;
                    _maidComboBox.items = maidManager.maidCaches;
                    _maidComboBox.DrawButton(_view);
                }

                _view.margin = GUIView.defaultMargin;
            }
            _view.EndLayout();

            _view.DrawHorizontalLine(Color.gray);
        }
    }
}
