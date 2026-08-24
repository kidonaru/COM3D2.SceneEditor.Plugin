using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムライン下部の実値カーブエディタ。
    /// 選択中キーフレームのチャンネル値をカーブとして描画し、
    /// キー点の値ドラッグとタンジェントハンドル編集を提供する
    /// </summary>
    public class TimelineCurveEditor
    {
        public const float TOGGLE_BAR_HEIGHT = 16f;
        private const int MIN_PANE_HEIGHT = 80;
        private const int MAX_PANE_HEIGHT = 400;
        private const float TOGGLE_BUTTON_WIDTH = 80f;

        private static MTEP.Config config => MTEP.ConfigManager.instance.config;
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.ITimelineLayer currentLayer => timelineManager.currentLayer;
        private static HashSet<MTEP.BoneData> selectedBones => timelineManager.selectedBones;

        private static TimelineCurveEditor _instance = null;
        public static TimelineCurveEditor instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TimelineCurveEditor();
                }
                return _instance;
            }
        }

        private TimelineCurveEditor()
        {
        }

        public bool isOpen
        {
            get => config.isCurveEditorOpen;
            set
            {
                if (config.isCurveEditorOpen == value) return;
                config.isCurveEditorOpen = value;
                config.dirty = true;
            }
        }

        public float paneHeight => config.curveEditorHeight;

        /// <summary>ペイン高さのドラッグリサイズ状態 (menuWidth リサイザと同じ流儀)</summary>
        private readonly GUIView.DragInfo _resizeDragInfo = new GUIView.DragInfo();

        /// <summary>開閉トグルと高さリサイズを持つバーの描画</summary>
        public void DrawToggleBar(GUIView view, Rect barRect)
        {
            view.currentPos = new Vector2(barRect.x, barRect.y);
            view.DrawTexture(GUIView.texWhite, barRect.width, barRect.height,
                new Color(0.2f, 0.2f, 0.2f, 0.8f));

            view.currentPos = new Vector2(barRect.x + 5, barRect.y);
            if (view.DrawButton(isOpen ? "▼ カーブ" : "▲ カーブ", TOGGLE_BUTTON_WIDTH, TOGGLE_BAR_HEIGHT))
            {
                isOpen = !isOpen;
            }

            if (!isOpen)
            {
                return;
            }

            // 開いている間はトグルボタン右のバー領域を上下ドラッグして高さを変更する
            var resizeX = barRect.x + 5 + TOGGLE_BUTTON_WIDTH;
            var resizeWidth = Mathf.Max(0f, barRect.xMax - resizeX);
            view.currentPos = new Vector2(resizeX, barRect.y);
            view.InvokeActionOnDragging(
                resizeWidth,
                barRect.height,
                _resizeDragInfo,
                new Vector2(0f, config.curveEditorHeight),
                null,
                value =>
                {
                    // DragInfo は上方向ドラッグで y が増える。上へ引くほどペインを高くする
                    config.curveEditorHeight = Mathf.Clamp(
                        (int)value.y, MIN_PANE_HEIGHT, MAX_PANE_HEIGHT);
                    config.dirty = true;
                });
        }

        /// <summary>カーブ描画領域。paneRect はウィンドウローカル座標</summary>
        public void DrawPane(GUIView view, Rect paneRect, float scrollX, float frameWidth, bool guiEnabled)
        {
            view.currentPos = new Vector2(paneRect.x, paneRect.y);
            view.DrawTexture(GUIView.texWhite, paneRect.width, paneRect.height, config.curveBgColor);
        }
    }
}
