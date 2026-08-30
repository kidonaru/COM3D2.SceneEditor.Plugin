using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// MTE 由来の演出状態 (テキスト / サブカメラ / ポストエフェクト) のプリセット断面。
    /// 実体は Unity コンポーネント側にしか無いため、値をここで DTO へ吸い出す。
    /// 要素数はマネージャ側プロパティが所有しており、タイムライン未読込でも保存・復元できる
    /// </summary>
    public static class MteEffectsSnapshot
    {
        // 適用時の上限。手編集や破損 XML の異常値で大量生成しないよう UI と同じ範囲へ丸める
        private const int MaxTextCount = 16;
        private const int MaxParaffinCount = 8;
        private const int MaxDistanceFogCount = 4;
        private const int MaxRimlightCount = 8;

        private static MTEP.TimelineTextManager textManager
            => MTEP.TimelineTextManager.instance;
        private static MTEP.SubCameraManager subCameraManager
            => MTEP.SubCameraManager.instance;
        private static MTEP.PostEffectManager postEffectManager
            => MTEP.PostEffectManager.instance;

        public static ScenePresetEffects CaptureState()
        {
            var data = new ScenePresetEffects();
            CaptureTexts(data);
            CaptureSubCameras(data);
            CapturePostEffects(data);
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
            ApplyPostEffects(data);
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

            var count = Mathf.Min(data.texts.Count, MaxTextCount);
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

        private static void CapturePostEffects(ScenePresetEffects data)
        {
            for (var i = 0; i < postEffectManager.GetParaffinCount(); i++)
            {
                // 適用中の実体を直に直列化しないようコピーを取る
                var copy = new MTEP.ColorParaffinData();
                copy.CopyFrom(postEffectManager.GetParaffinData(i));
                data.paraffins.Add(copy);
            }
            data.paraffinEnabled = postEffectManager.paraffin.enabled;

            for (var i = 0; i < postEffectManager.GetDistanceFogCount(); i++)
            {
                var copy = new MTEP.DistanceFogData();
                copy.CopyFrom(postEffectManager.GetDistanceFogData(i));
                data.distanceFogs.Add(copy);
            }
            data.distanceFogEnabled = postEffectManager.distanceFog.enabled;

            for (var i = 0; i < postEffectManager.GetRimlightCount(); i++)
            {
                var copy = new MTEP.RimlightData();
                copy.CopyFrom(postEffectManager.GetRimlightData(i));
                data.rimlights.Add(copy);
            }
            data.rimlightEnabled = postEffectManager.rimlight.enabled;
        }

        private static void ApplyPostEffects(ScenePresetEffects data)
        {
            var paraffinCount = Mathf.Min(data.paraffins.Count, MaxParaffinCount);
            var distanceFogCount = Mathf.Min(data.distanceFogs.Count, MaxDistanceFogCount);
            var rimlightCount = Mathf.Min(data.rimlights.Count, MaxRimlightCount);

            if (paraffinCount == 0 && distanceFogCount == 0 && rimlightCount == 0)
            {
                return;
            }

            // 記録があるグループだけ要素数を合わせてから一括で実体を作り直す。
            // InitPostEffects は 3 グループ全ての count プロパティを毎回突き合わせるが、
            // 未記録グループは count を触っていないため実体数は変わらない
            if (paraffinCount > 0)
            {
                postEffectManager.paraffinCount = paraffinCount;
            }
            if (distanceFogCount > 0)
            {
                postEffectManager.distanceFogCount = distanceFogCount;
            }
            if (rimlightCount > 0)
            {
                postEffectManager.rimlightCount = rimlightCount;
            }
            postEffectManager.InitPostEffects();

            for (var i = 0; i < paraffinCount; i++)
            {
                postEffectManager.ApplyParaffin(i, data.paraffins[i]);
            }
            if (paraffinCount > 0)
            {
                // Apply* は個別データが有効だとマスターを ON にするため、保存値で確定させる
                postEffectManager.paraffin.enabled = data.paraffinEnabled;
            }

            for (var i = 0; i < distanceFogCount; i++)
            {
                postEffectManager.ApplyDistanceFog(i, data.distanceFogs[i]);
            }
            if (distanceFogCount > 0)
            {
                postEffectManager.distanceFog.enabled = data.distanceFogEnabled;
            }

            for (var i = 0; i < rimlightCount; i++)
            {
                postEffectManager.ApplyRimlight(i, data.rimlights[i]);
            }
            if (rimlightCount > 0)
            {
                postEffectManager.rimlight.enabled = data.rimlightEnabled;
            }
        }
    }
}
