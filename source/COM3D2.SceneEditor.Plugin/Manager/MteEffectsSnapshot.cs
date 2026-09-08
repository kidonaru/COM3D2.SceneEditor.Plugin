using System.Collections.Generic;
using COM3D2.MotionTimelineEditor;
using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// MTE 由来の演出状態 (テキスト / サブカメラ / サウンド / 動画) のプリセット断面。
    /// 実体は Unity コンポーネント側にしか無いため、値をここで DTO へ吸い出す。
    /// 要素数はマネージャ側プロパティが所有しており、タイムライン未読込でも保存・復元できる
    /// </summary>
    public static class MteEffectsSnapshot
    {
        private static MTEP.TimelineTextManager textManager
            => MTEP.TimelineTextManager.instance;
        private static MTEP.SubCameraManager subCameraManager
            => MTEP.SubCameraManager.instance;
        private static MTEP.BGMManager bgmManager => MTEP.BGMManager.instance;
        private static MTEP.MovieManager movieManager => MTEP.MovieManager.instance;

        public static ScenePresetEffects CaptureState()
        {
            var data = new ScenePresetEffects();
            data.texts = CaptureTexts();
            data.subCameras = CaptureSubCameras();
            data.sound = CaptureSound();
            data.videos = CaptureVideos();
            return data;
        }

        /// <summary>null (旧プリセット / 未保存) なら何もしない</summary>
        public static void ApplyState(ScenePresetEffects data)
        {
            if (data == null)
            {
                return;
            }
            ApplyTexts(data.texts);
            ApplySubCameras(data.subCameras);
            ApplySound(data.sound);
            ApplyVideos(data.videos, reloadAll: true);
        }

        /// <summary>フリーテキスト全件を DTO へ吸い出す。履歴とプリセットで共用</summary>
        public static List<ScenePresetText> CaptureTexts()
        {
            var texts = new List<ScenePresetText>();
            foreach (var freeTextSet in textManager.TextData)
            {
                var text = freeTextSet.text;
                var rect = freeTextSet.rect;
                if (text == null || rect == null)
                {
                    continue;
                }

                texts.Add(new ScenePresetText
                {
                    text = text.text,
                    font = text.font != null ? text.font.name : "",
                    fontSize = text.fontSize,
                    lineSpacing = text.lineSpacing,
                    alignment = (int)text.alignment,
                    position = rect.localPosition,
                    rotation = rect.eulerAngles,
                    scale = rect.localScale,
                    color = text.color,
                    sizeDeltaX = rect.sizeDelta.x,
                    sizeDeltaY = rect.sizeDelta.y,
                });
            }
            return texts;
        }

        /// <summary>フリーテキスト全件を書き戻す。履歴とプリセットで共用</summary>
        public static void ApplyTexts(List<ScenePresetText> texts)
        {
            // 空 = 保存時に実体なし。「未記録」と区別できないため触らない
            if (texts == null || texts.Count == 0)
            {
                return;
            }

            // 手編集や破損 XML の異常値で大量生成しないよう UI と同じ上限へ丸める
            var count = Mathf.Min(texts.Count, MTEP.TimelineTextManager.MaxTextCount);
            textManager.textCount = count;
            // タイムライン読込中はテキストレイヤーの LateUpdate も作り直すが、
            // 未読込時はここが唯一の生成経路のため直接呼ぶ (InitTexts は冪等)
            textManager.InitTexts();

            for (var i = 0; i < count; i++)
            {
                if (!textManager.IsValidIndex(i))
                {
                    break;
                }
                var freeTextSet = textManager.GetFreeTextSet(i);
                var text = freeTextSet.text;
                var rect = freeTextSet.rect;
                if (text == null || rect == null)
                {
                    continue;
                }

                var src = texts[i];
                text.text = src.text;
                if (!string.IsNullOrEmpty(src.font))
                {
                    text.font = textManager.GetFont(src.font);
                }
                text.fontSize = src.fontSize;
                text.lineSpacing = src.lineSpacing;
                text.alignment = (TextAnchor)src.alignment;
                text.color = src.color;
                rect.localPosition = src.position;
                rect.eulerAngles = src.rotation;
                rect.localScale = src.scale;
                rect.sizeDelta = new Vector2(src.sizeDeltaX, src.sizeDeltaY);

                textManager.UpdateFreeTextSet(i, freeTextSet);
            }
        }

        /// <summary>サブカメラ全台を DTO へ吸い出す。履歴とプリセットで共用</summary>
        public static List<ScenePresetSubCamera> CaptureSubCameras()
        {
            var subCameras = new List<ScenePresetSubCamera>();
            foreach (var cameraData in subCameraManager.subCameras)
            {
                if (cameraData.camera == null)
                {
                    continue;
                }

                var follow = cameraData.follow;
                subCameras.Add(new ScenePresetSubCamera
                {
                    visible = cameraData.visible,
                    fieldOfView = cameraData.camera.fieldOfView,
                    // 追従中はオフセットが入る (SubCameraData のプロパティ準拠)
                    position = cameraData.position,
                    rotation = cameraData.rotation.eulerAngles,
                    viewportX = cameraData.viewportRect.x,
                    viewportY = cameraData.viewportRect.y,
                    viewportWidth = cameraData.viewportRect.width,
                    viewportHeight = cameraData.viewportRect.height,
                    maidSlotNo = follow.maidSlotNo,
                    maidPointType = (int)follow.maidPointType,
                    followRotation = follow.followRotation,
                });
            }
            return subCameras;
        }

        /// <summary>サブカメラ全台を書き戻す。履歴とプリセットで共用</summary>
        public static void ApplySubCameras(List<ScenePresetSubCamera> srcList)
        {
            if (srcList == null || srcList.Count == 0)
            {
                return;
            }

            subCameraManager.SetCameraCount(srcList.Count);

            var subCameras = subCameraManager.subCameras;
            for (var i = 0; i < srcList.Count && i < subCameras.Count; i++)
            {
                var cameraData = subCameras[i];
                if (cameraData.camera == null)
                {
                    continue;
                }

                var src = srcList[i];
                // 追従設定を先に入れることで position / rotation プロパティの
                // 書き込み先 (オフセット / ワールド値) を保存時と一致させる
                var follow = cameraData.follow;
                follow.maidSlotNo = src.maidSlotNo;
                follow.maidPointType = (MTEP.MaidPointType)src.maidPointType;
                follow.followRotation = src.followRotation;

                cameraData.position = src.position;
                cameraData.rotation = Quaternion.Euler(src.rotation);
                cameraData.camera.fieldOfView = src.fieldOfView;
                cameraData.ApplyViewport(new Rect(
                    src.viewportX, src.viewportY, src.viewportWidth, src.viewportHeight));
                cameraData.visible = src.visible;
            }
        }

        /// <summary>BGM ファイル設定だけ (ゲーム BGM は含まない)。履歴とプリセットで共用</summary>
        public static ScenePresetSound CaptureBgmSettings()
        {
            var settings = bgmManager.settings;
            return new ScenePresetSound
            {
                bgmPath = settings.bgmPath,
                bpm = settings.bpm,
                isShowBPMLine = settings.isShowBPMLine,
                bpmLineOffsetFrame = settings.bpmLineOffsetFrame,
            };
        }

        private static ScenePresetSound CaptureSound()
        {
            var data = CaptureBgmSettings();
            // 無音は空文字。適用時に「停止」として働く
            data.gameBgmFile = BgmUtils.GetPlayingFileName() ?? "";
            return data;
        }

        /// <summary>
        /// BGM ファイル設定を書き戻す。パスが変わったときだけ読み直す
        /// (BPM だけの変更で曲を止めない)
        /// </summary>
        public static void ApplyBgmSettings(ScenePresetSound src)
        {
            var settings = bgmManager.settings;
            var pathChanged = settings.bgmPath != src.bgmPath;
            settings.bgmPath = src.bgmPath;
            settings.bpm = src.bpm;
            settings.isShowBPMLine = src.isShowBPMLine;
            settings.bpmLineOffsetFrame = src.bpmLineOffsetFrame;
            if (pathChanged)
            {
                // パスが空なら Stop だけが走る
                bgmManager.Reload();
            }
        }

        /// <summary>null (v29 以前 / 未記録) なら何もしない</summary>
        private static void ApplySound(ScenePresetSound src)
        {
            if (src == null)
            {
                return;
            }

            // プリセットは従来どおり必ず 1 回読み直す。
            // ApplyBgmSettings はパスが変わったときだけ Reload するので、
            // 変わらなかったときにここで補う (呼び出し前のパスで判定する)
            var prevBgmPath = bgmManager.settings.bgmPath;
            ApplyBgmSettings(src);
            if (prevBgmPath == src.bgmPath)
            {
                bgmManager.Reload();
            }

            // タイムライン BGM ファイルが読めていてタイムライン再生中なら、次フレームの
            // BGMManager.Update が Play() → SoundMgr.StopBGM でゲーム BGM を止めてしまう。
            // 一瞬鳴って止まるより復元しない方が分かりやすいので、警告を出してスキップする
            var timeline = MTEP.TimelineManager.instance.timeline;
            var isTimelinePlaying = timeline != null && timeline.defaultLayer.isAnmPlaying;
            if (bgmManager.IsLoaded() && isTimelinePlaying)
            {
                if (!string.IsNullOrEmpty(src.gameBgmFile))
                {
                    MTEUtils.LogWarning("タイムライン BGM 再生中のためゲームBGMは復元しません: {0}", src.gameBgmFile);
                }
                return;
            }

            ApplyGameBgm(src.gameBgmFile);
        }

        private static void ApplyGameBgm(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                BgmUtils.Stop();
                return;
            }

            if (!BgmUtils.EnsureSoundDataLoaded())
            {
                MTEUtils.LogWarning("BGM一覧を取得できないためゲームBGMを復元できません: {0}", fileName);
                return;
            }

            var soundData = PhotoSoundData.Get(fileName);
            if (soundData == null)
            {
                MTEUtils.LogWarning("プリセットのゲームBGMが見つかりません: {0}", fileName);
                return;
            }

            // 同じ曲が再生中でも頭出しとして再生し直す (サウンドウィンドウの一覧と同じ挙動)
            soundData.Play();
        }

        /// <summary>動画全本を DTO へ吸い出す。履歴とプリセットで共用</summary>
        public static List<ScenePresetVideo> CaptureVideos()
        {
            var videos = new List<ScenePresetVideo>();
            foreach (var settings in movieManager.settingsList)
            {
                videos.Add(new ScenePresetVideo
                {
                    enabled = settings.enabled,
                    displayType = (int)settings.displayType,
                    path = settings.path,
                    position = settings.position,
                    rotation = settings.rotation,
                    scale = settings.scale,
                    startTime = settings.startTime,
                    volume = settings.volume,
                    alpha = settings.alpha,
                    guiScale = settings.guiScale,
                    guiAlpha = settings.guiAlpha,
                    backmostPosition = settings.backmostPosition,
                    backmostScale = settings.backmostScale,
                    backmostAlpha = settings.backmostAlpha,
                    frontmostPosition = settings.frontmostPosition,
                    frontmostScale = settings.frontmostScale,
                    frontmostAlpha = settings.frontmostAlpha,
                });
            }
            return videos;
        }

        /// <summary>
        /// 空 (v31 以前 / 未保存) なら触らない。
        /// v31 以前は動画が 1 件しか無いため、適用すると本数も 1 本へ戻る。
        /// reloadAll=false (履歴) では、パス・表示形式が変わった本だけプレイヤーを作り直し、
        /// それ以外は値の反映だけにする (ReloadMovie は全本のデコーダ再生成で重い)
        /// </summary>
        public static void ApplyVideos(List<ScenePresetVideo> videos, bool reloadAll)
        {
            if (videos == null || videos.Count == 0)
            {
                return;
            }

            var current = reloadAll ? null : CaptureVideos();

            // 手編集や破損 XML の異常値で大量生成しないよう UI と同じ上限へ丸める
            var count = Mathf.Min(videos.Count, MTEP.MovieManager.MaxVideoCount);
            movieManager.videoCount = count;

            for (var i = 0; i < count; i++)
            {
                var src = videos[i];
                var settings = movieManager.GetSettings(i);
                settings.enabled = src.enabled;
                settings.displayType = (MTEP.VideoDisplayType)src.displayType;
                settings.path = src.path;
                settings.position = src.position;
                settings.rotation = src.rotation;
                settings.scale = src.scale;
                settings.startTime = src.startTime;
                settings.volume = src.volume;
                settings.alpha = src.alpha;
                settings.guiScale = src.guiScale;
                settings.guiAlpha = src.guiAlpha;
                settings.backmostPosition = src.backmostPosition;
                settings.backmostScale = src.backmostScale;
                settings.backmostAlpha = src.backmostAlpha;
                settings.frontmostPosition = src.frontmostPosition;
                settings.frontmostScale = src.frontmostScale;
                settings.frontmostAlpha = src.frontmostAlpha;

                if (reloadAll)
                {
                    continue;
                }

                var before = i < current.Count ? current[i] : null;
                switch (VideoReloadPolicy.Decide(before, src))
                {
                    case VideoApplyAction.Reload:
                        movieManager.ReloadMovie(i);
                        break;
                    case VideoApplyAction.Visible:
                        movieManager.LoadMovie(i);
                        movieManager.UpdateVisible(i);
                        break;
                }
                // Reload した本へ重ねても無害 (プレイヤー未生成なら WithPlayer が何もしない)
                movieManager.UpdateTransform(i);
                movieManager.UpdateMesh(i);
                movieManager.UpdateColor(i);
                movieManager.UpdateVolume(i);
                movieManager.UpdateSeekTime(i);
            }

            if (reloadAll)
            {
                // パス空の本は Unload だけが走る (LoadMovie は IsValidPath を見る)
                movieManager.ReloadMovie();
            }
        }
    }
}
