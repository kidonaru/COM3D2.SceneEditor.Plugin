using UnityEngine;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// ライトのパラメータのコピー & ペースト用クリップボード。
    /// 別のライトへ設定を写すためのもので、プロセス内でのみ保持する。
    /// メインライトと追加ライトの間で写すこともでき、相手側に無い項目は既定値で埋まる
    /// </summary>
    public static class LightClipboard
    {
        private class Data
        {
            public LightType type;
            public bool enabled;
            public Vector3 position;
            public Vector3 rotation;
            public Color color;
            public float intensity;
            public float range;
            public float spotAngle;
            public float shadowStrength;
            public float shadowBias;
            public LightTargetMode target;
            public int maidSlotNo;
            public Vector3 followOffset;
        }

        private static Data _data = null;

        public static bool hasData => _data != null;

        /// <summary>追加ライトの値を記録する。followLight が null なら追従は「なし」として記録する</summary>
        public static void Copy(Light light, MTEP.MaidFollowLight followLight)
        {
            _data = new Data
            {
                type = light.type,
                enabled = light.enabled,
                position = light.transform.position,
                rotation = light.transform.eulerAngles,
                color = light.color,
                intensity = light.intensity,
                range = light.range,
                spotAngle = light.spotAngle,
                shadowStrength = light.shadowStrength,
                shadowBias = light.shadowBias,
                target = LightTarget.FromCullingMask(light.cullingMask),
                maidSlotNo = followLight != null ? followLight.maidSlotNo : -1,
                followOffset = followLight != null ? followLight.offset : Vector3.zero,
            };
        }

        /// <summary>メインライトの値を記録する。追加ライトに無い項目は生成時の既定値にする</summary>
        public static void CopyMain(Light light)
        {
            _data = new Data
            {
                type = LightType.Directional,
                enabled = true,
                position = StudioLightManager.DefaultPosition,
                rotation = light.transform.eulerAngles,
                color = light.color,
                intensity = light.intensity,
                range = StudioLightManager.DefaultRange,
                spotAngle = StudioLightManager.DefaultSpotAngle,
                shadowStrength = light.shadowStrength,
                shadowBias = light.shadowBias,
                target = LightTargetMode.All,
                maidSlotNo = -1,
                followOffset = Vector3.zero,
            };
        }

        /// <summary>追加ライトへ反映する。followLight が null なら追従は触らない</summary>
        public static void Paste(Light light, MTEP.MaidFollowLight followLight)
        {
            if (_data == null || light == null)
            {
                return;
            }

            StudioLightManager.instance.SetLightType(light, _data.type);
            light.enabled = _data.enabled;
            light.transform.position = _data.position;
            light.transform.eulerAngles = _data.rotation;
            light.color = _data.color;
            light.intensity = _data.intensity;
            light.range = _data.range;
            light.spotAngle = _data.spotAngle;
            light.shadowStrength = _data.shadowStrength;
            light.shadowBias = _data.shadowBias;
            light.cullingMask = LightTarget.ToCullingMask(_data.target);

            if (followLight != null)
            {
                followLight.maidSlotNo = _data.maidSlotNo;
                followLight.offset = _data.followOffset;
            }
        }

        /// <summary>
        /// メインライトへ反映する。LightMain の setter が無い shadowBias だけ Light へ直接書く
        /// （LightRowDrawer のメインライト行と同じ扱い）。平行光源なので位置は持たず、種別・範囲・照射対象・追従も反映しない
        /// </summary>
        public static void PasteMain(LightMain lightMain, Light light)
        {
            if (_data == null || lightMain == null || light == null)
            {
                return;
            }

            lightMain.SetRotation(_data.rotation);
            lightMain.SetColor(_data.color);
            lightMain.SetIntensity(_data.intensity);
            lightMain.SetShadowStrength(_data.shadowStrength);
            light.shadowBias = _data.shadowBias;
        }
    }
}
