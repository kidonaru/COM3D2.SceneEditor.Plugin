using System;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// BGM (SoundMgr / PhotoSoundData) 周りの共通処理
    /// </summary>
    public static class BgmUtils
    {
        /// <summary>SoundMgr を使えるか。使えない間は再生状態を「無音」と断定できない</summary>
        public static bool isAvailable
            => GameMain.Instance != null && GameMain.Instance.SoundMgr != null;

        /// <summary>
        /// BGM 一覧を用意する。フォトモード未経由だと未構築のため初回にここで構築する。
        /// 一覧は背景（マイルーム）と違いゲーム中に増減しないため、作り直しは用意しない。
        /// 使える一覧が得られたかを返す
        /// </summary>
        public static bool EnsureSoundDataLoaded()
        {
            if (PhotoSoundData.data == null)
            {
                try
                {
                    PhotoSoundData.Create();
                }
                catch (Exception e)
                {
                    // ファイルシステム未初期化などで失敗しても描画は続行する
                    // (一覧は空表示になり、開き直せば作り直される)
                    MTEUtils.LogException(e);
                }
            }
            return PhotoSoundData.data != null && PhotoSoundData.data.Count > 0;
        }

        /// <summary>
        /// 再生中の BGM のファイル名。無音なら null。
        /// AudioSource の clip 名が PhotoSoundData.file_name と一致する (例: "BGM020.ogg")
        /// </summary>
        public static string GetPlayingFileName()
        {
            if (!isAvailable)
            {
                return null;
            }

            var source = GameMain.Instance.SoundMgr.GetAudioSourceBgm();
            if (source == null || !source.isPlaying || source.clip == null)
            {
                return null;
            }
            return source.clip.name;
        }

        /// <summary>BGM を停止する</summary>
        public static void Stop()
        {
            if (isAvailable)
            {
                GameMain.Instance.SoundMgr.StopBGM(0f);
            }
        }
    }
}
