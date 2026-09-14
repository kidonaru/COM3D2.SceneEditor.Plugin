using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// タイムライン操作のキー入力。
    /// ウィンドウの表示状態には依らず、プラグイン表示中は常に効く
    /// </summary>
    public static class TimelineKeyInput
    {
        private static Config config => ConfigManager.instance.config;
        private static MTEP.SceneEditorHack studioHack => MTEP.SceneEditorHack.instance;
        private static MTEP.MaidManager maidManager => MTEP.MaidManager.instance;
        private static MTEP.TimelineManager timelineManager => MTEP.TimelineManager.instance;
        private static MTEP.ITimelineLayer currentLayer => timelineManager.currentLayer;

        /// <summary>タイムライン操作のキー入力を処理する</summary>
        public static void Update()
        {
            if (!config.isTimelineKeyInputEnabled)
            {
                return;
            }

            // テキスト入力中は誤発動を防ぐため無視する
            if (GUIUtility.keyboardControl != 0)
            {
                return;
            }

            if (studioHack == null || maidManager.maid == null ||
                !timelineManager.IsValidData())
            {
                return;
            }

            if (config.GetKeyDown(KeyBindType.AddKeyFrame))
            {
                timelineManager.AddKeyFrameDiff();
            }
            if (config.GetKeyDown(KeyBindType.AddKeyFrameAll))
            {
                currentLayer.AddKeyFrameAll();
            }
            if (config.GetKeyDown(KeyBindType.RemoveKeyFrame))
            {
                timelineManager.RemoveSelectedFrame();
            }
            if (config.GetKeyDownRepeat(KeyBindType.PrevFrame))
            {
                SeekFrameWithScroll(timelineManager.currentFrameNo - 1);
            }
            if (config.GetKeyDownRepeat(KeyBindType.NextFrame))
            {
                SeekFrameWithScroll(timelineManager.currentFrameNo + 1);
            }
            if (config.GetKeyDownRepeat(KeyBindType.PrevKeyFrame))
            {
                var prevFrame = timelineManager.GetPrevFrame(timelineManager.currentFrameNo);
                if (prevFrame != null)
                {
                    SeekFrameWithScroll(prevFrame.frameNo);
                }
            }
            if (config.GetKeyDownRepeat(KeyBindType.NextKeyFrame))
            {
                var nextFrame = timelineManager.GetNextFrame(timelineManager.currentFrameNo);
                if (nextFrame != null)
                {
                    SeekFrameWithScroll(nextFrame.frameNo);
                }
            }
            if (config.GetKeyDown(KeyBindType.Play))
            {
                if (currentLayer.isAnmPlaying)
                {
                    timelineManager.Pause();
                }
                else
                {
                    timelineManager.Play();
                }
            }
            if (config.GetKeyDown(KeyBindType.Copy))
            {
                timelineManager.CopyFramesToClipboard();
            }
            if (config.GetKeyDown(KeyBindType.Paste))
            {
                timelineManager.PasteFramesFromClipboard(false);
            }
            if (config.GetKeyDown(KeyBindType.FlipPaste))
            {
                timelineManager.PasteFramesFromClipboard(true);
            }
            if (config.GetKeyDown(KeyBindType.PoseCopy))
            {
                timelineManager.CopyPoseToClipboard();
            }
            if (config.GetKeyDown(KeyBindType.PosePaste))
            {
                timelineManager.PastePoseFromClipboard();
            }
        }

        /// <summary>フレーム移動し、タイムラインの表示位置も追従させる (操作ウィンドウのボタンからも呼ぶ)</summary>
        public static void SeekFrameWithScroll(int frameNo)
        {
            timelineManager.SeekCurrentFrame(frameNo);
            TimelineWindow.instance.FixScrollPosition();
        }
    }
}
