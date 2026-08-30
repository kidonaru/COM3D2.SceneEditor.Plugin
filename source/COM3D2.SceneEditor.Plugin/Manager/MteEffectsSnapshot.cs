using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// MTE 由来の演出状態 (テキスト / サブカメラ) のプリセット断面。
    /// 実体は Unity コンポーネント側にしか無いため、値をここで DTO へ吸い出す。
    /// 要素数はマネージャ側プロパティが所有しており、タイムライン未読込でも保存・復元できる
    /// </summary>
    public static class MteEffectsSnapshot
    {
        private static MTEP.TimelineTextManager textManager
            => MTEP.TimelineTextManager.instance;
        private static MTEP.SubCameraManager subCameraManager
            => MTEP.SubCameraManager.instance;

        public static ScenePresetEffects CaptureState()
        {
            var data = new ScenePresetEffects();
            CaptureTexts(data);
            CaptureSubCameras(data);
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
    }
}
