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
            CaptureTexts(data);
            CaptureSubCameras(data);
            data.sound = CaptureSound();
            data.video = CaptureVideo();
            return data;
        }

        /// <summary>null (旧プリセット / 未保存) なら何もしない</summary>
        public static void ApplyState(ScenePresetEffects data)
        {
            if (data == null)
            {
                return;
            }
            ApplyTexts(data);
            ApplySubCameras(data);
            ApplySound(data.sound);
            ApplyVideo(data.video);
        }

        private static void CaptureTexts(ScenePresetEffects data)
        {
            foreach (var freeTextSet in textManager.TextData)
            {
                var text = freeTextSet.text;
                var rect = freeTextSet.rect;
                if (text == null || rect == null)
                {
                    continue;
                }

                data.texts.Add(new ScenePresetText
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
        }

        private static void ApplyTexts(ScenePresetEffects data)
        {
            // 空 = 保存時に実体なし。「未記録」と区別できないため触らない
            if (data.texts.Count == 0)
            {
                return;
            }

            // 手編集や破損 XML の異常値で大量生成しないよう UI と同じ上限へ丸める
            var count = Mathf.Min(data.texts.Count, MTEP.TimelineTextManager.MaxTextCount);
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

                var src = data.texts[i];
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

        private static void CaptureSubCameras(ScenePresetEffects data)
        {
            foreach (var cameraData in subCameraManager.subCameras)
            {
                if (cameraData.camera == null)
                {
                    continue;
                }

                var follow = cameraData.follow;
                data.subCameras.Add(new ScenePresetSubCamera
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
        }

        private static void ApplySubCameras(ScenePresetEffects data)
        {
            if (data.subCameras.Count == 0)
            {
                return;
            }

            subCameraManager.SetCameraCount(data.subCameras.Count);

            var subCameras = subCameraManager.subCameras;
            for (var i = 0; i < data.subCameras.Count && i < subCameras.Count; i++)
            {
                var cameraData = subCameras[i];
                if (cameraData.camera == null)
                {
                    continue;
                }

                var src = data.subCameras[i];
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

        private static ScenePresetSound CaptureSound()
        {
            var settings = bgmManager.settings;
            return new ScenePresetSound
            {
                // 無音は空文字。適用時に「停止」として働く
                gameBgmFile = BgmUtils.GetPlayingFileName() ?? "",
                bgmPath = settings.bgmPath,
                bpm = settings.bpm,
                isShowBPMLine = settings.isShowBPMLine,
                bpmLineOffsetFrame = settings.bpmLineOffsetFrame,
            };
        }

        /// <summary>null (v29 以前 / 未記録) なら何もしない</summary>
        private static void ApplySound(ScenePresetSound src)
        {
            if (src == null)
            {
                return;
            }

            var settings = bgmManager.settings;
            settings.bgmPath = src.bgmPath;
            settings.bpm = src.bpm;
            settings.isShowBPMLine = src.isShowBPMLine;
            settings.bpmLineOffsetFrame = src.bpmLineOffsetFrame;
            // パスが空なら Stop だけが走る
            bgmManager.Reload();

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

        private static ScenePresetVideo CaptureVideo()
        {
            var s = movieManager.settings;
            return new ScenePresetVideo
            {
                enabled = s.enabled,
                displayType = (int)s.displayType,
                path = s.path,
                position = s.position,
                rotation = s.rotation,
                scale = s.scale,
                startTime = s.startTime,
                volume = s.volume,
                alpha = s.alpha,
                guiPosition = s.guiPosition,
                guiScale = s.guiScale,
                guiAlpha = s.guiAlpha,
                backmostPosition = s.backmostPosition,
                backmostScale = s.backmostScale,
                backmostAlpha = s.backmostAlpha,
                frontmostPosition = s.frontmostPosition,
                frontmostScale = s.frontmostScale,
                frontmostAlpha = s.frontmostAlpha,
            };
        }

        /// <summary>null (v29 以前 / 未記録) なら何もしない</summary>
        private static void ApplyVideo(ScenePresetVideo src)
        {
            if (src == null)
            {
                return;
            }

            var s = movieManager.settings;
            s.enabled = src.enabled;
            s.displayType = (MTEP.VideoDisplayType)src.displayType;
            s.path = src.path;
            s.position = src.position;
            s.rotation = src.rotation;
            s.scale = src.scale;
            s.startTime = src.startTime;
            s.volume = src.volume;
            s.alpha = src.alpha;
            s.guiPosition = src.guiPosition;
            s.guiScale = src.guiScale;
            s.guiAlpha = src.guiAlpha;
            s.backmostPosition = src.backmostPosition;
            s.backmostScale = src.backmostScale;
            s.backmostAlpha = src.backmostAlpha;
            s.frontmostPosition = src.frontmostPosition;
            s.frontmostScale = src.frontmostScale;
            s.frontmostAlpha = src.frontmostAlpha;
            // 無効やパス空なら Unload だけが走る (LoadMovie は isEnabled を見る)
            movieManager.ReloadMovie();
        }
    }
}
