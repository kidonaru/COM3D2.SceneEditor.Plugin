using UnityEngine;
using PEData = COM3D2.MotionTimelineEditor.PostEffects;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class DepthOfFieldData
    {
        public bool enabled;
        public float focalLength;
        public float focalSize;
        public float aperture;
        public float maxBlurSize;
        public int maidSlotNo;

        public static DepthOfFieldData Lerp(
            DepthOfFieldData a,
            DepthOfFieldData b,
            float t)
        {
            return new DepthOfFieldData
            {
                enabled = a.enabled,
                focalLength = Mathf.Lerp(a.focalLength, b.focalLength, t),
                focalSize = Mathf.Lerp(a.focalSize, b.focalSize, t),
                aperture = Mathf.Lerp(a.aperture, b.aperture, t),
                maxBlurSize = Mathf.Lerp(a.maxBlurSize, b.maxBlurSize, t),
                maidSlotNo = a.maidSlotNo,
            };
        }
    }

    [System.Serializable]
    public struct GTToneMapData
    {
        public bool enabled;
        [Range(1f, 100f)]
        public float maxBrightness;
        [Range(0f, 5f)]
        public float contrast;
        [Range(0f, 1f)]
        public float linearStart;
        [Range(0f, 1f)]
        public float linearLength;
        [Range(1f, 3f)]
        public float blackTightness;
        [Range(0f, 1f)]
        public float blackOffset;

        public static GTToneMapData Create()
        {
            return new GTToneMapData
            {
                enabled = false,
                maxBrightness = 1.0f,
                contrast = 1.0f,
                linearStart = 0.22f,
                linearLength = 0.4f,
                blackTightness = 1.33f,
                blackOffset = 0.0f
            };
        }

        public void CopyFrom(GTToneMapData other)
        {
            enabled = other.enabled;
            maxBrightness = other.maxBrightness;
            contrast = other.contrast;
            linearStart = other.linearStart;
            linearLength = other.linearLength;
            blackTightness = other.blackTightness;
            blackOffset = other.blackOffset;
        }

        public bool Equals(GTToneMapData other)
        {
            return enabled == other.enabled &&
                   maxBrightness.Equals(other.maxBrightness) &&
                   contrast.Equals(other.contrast) &&
                   linearStart.Equals(other.linearStart) &&
                   linearLength.Equals(other.linearLength) &&
                   blackTightness.Equals(other.blackTightness) &&
                   blackOffset.Equals(other.blackOffset);
        }

        public static GTToneMapData Lerp(GTToneMapData start, GTToneMapData end, float t)
        {
            GTToneMapData result = new GTToneMapData();
            result.enabled = start.enabled;
            result.maxBrightness = Mathf.Lerp(start.maxBrightness, end.maxBrightness, t);
            result.contrast = Mathf.Lerp(start.contrast, end.contrast, t);
            result.linearStart = Mathf.Lerp(start.linearStart, end.linearStart, t);
            result.linearLength = Mathf.Lerp(start.linearLength, end.linearLength, t);
            result.blackTightness = Mathf.Lerp(start.blackTightness, end.blackTightness, t);
            result.blackOffset = Mathf.Lerp(start.blackOffset, end.blackOffset, t);
            return result;
        }
    }

    /// <summary>
    /// ポストエフェクトのタイムライン窓口。値の実体は PostEffects.Plugin が所有しており、
    /// ここは PostEffectsClient (リフレクション経由の TimelineBridge) への委譲と
    /// DTO 変換だけを持つ。未接続時は PostEffectsClient 側が既定値を返して
    /// 書き込みを捨てるため、実体への書き込みにはここでガードを置かない。
    /// 逆向き (実体 → タイムライン) の SyncCountsFromHost だけは、取得失敗時の
    /// 既定値でタイムラインを潰さないよう例外的にガードする
    /// </summary>
    public class PostEffectManager : ManagerBase
    {
        private static PostEffectManager _instance;
        public static PostEffectManager instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new PostEffectManager();
                }

                return _instance;
            }
        }

        public int depthOfFieldMaidSlotId = -1;

        public bool paraffinEnabled
        {
            get => PostEffectsClient.paraffinEnabled;
            set => PostEffectsClient.paraffinEnabled = value;
        }

        public bool distanceFogEnabled
        {
            get => PostEffectsClient.distanceFogEnabled;
            set => PostEffectsClient.distanceFogEnabled = value;
        }

        public bool rimlightEnabled
        {
            get => PostEffectsClient.rimlightEnabled;
            set => PostEffectsClient.rimlightEnabled = value;
        }

        private PostEffectManager()
        {
        }

        public override void Init()
        {
        }

        public override void OnLoad()
        {
            DisableAllEffects();
            InitPostEffects();
        }

        public override void OnPluginDisable()
        {
            DisableAllEffects();
        }

        public void InitPostEffects()
        {
            if (timeline != null)
            {
                // TimelineXml 読込で timeline 側だけ変わった場合に実体数を追随させる
                PostEffectsClient.paraffinCount = timeline.paraffinCount;
                PostEffectsClient.distanceFogCount = timeline.distanceFogCount;
                PostEffectsClient.rimlightCount = timeline.rimlightCount;
            }
        }

        /// <summary>
        /// 実体側 (PostEffects.Plugin) の要素数をタイムラインへ取り込む。
        /// 増減 UI は実体側にしか無いため、そこでの変更を XML 保存対象へ反映する。
        /// 未接続・取得失敗時は 0 が返るため、タイムラインの値を潰さないよう何もしない
        /// </summary>
        public void SyncCountsFromHost()
        {
            if (timeline == null)
            {
                return;
            }

            if (!PostEffectsClient.TryGetCounts(out var paraffin, out var distanceFog, out var rimlight))
            {
                return;
            }

            timeline.paraffinCount = paraffin;
            timeline.distanceFogCount = distanceFog;
            timeline.rimlightCount = rimlight;
        }

        public void DisableAllEffects()
        {
            PostEffectsClient.paraffinEnabled = false;
            PostEffectsClient.distanceFogEnabled = false;
            PostEffectsClient.rimlightEnabled = false;

            var dof = PostEffectsClient.GetDepthOfField();
            dof.enabled = false;
            PostEffectsClient.ApplyDepthOfField(dof);

            var toneMap = PostEffectsClient.GetGTToneMap();
            toneMap.enabled = false;
            PostEffectsClient.ApplyGTToneMap(toneMap);

            var bloom = PostEffectsClient.GetBloom();
            bloom.enabled = false;
            PostEffectsClient.ApplyBloom(bloom);
        }

        public DepthOfFieldData GetDepthOfFieldData()
        {
            var setting = PostEffectsClient.GetDepthOfField();
            return new DepthOfFieldData
            {
                enabled = setting.enabled,
                focalLength = setting.focalLength,
                focalSize = setting.focalSize,
                aperture = setting.aperture,
                maxBlurSize = setting.maxBlurSize,
                // メイド追従は共有 DTO 側で maidFocus + maidIndex に分かれている
                maidSlotNo = setting.maidFocus ? setting.maidIndex : -1,
            };
        }

        public void ApplyDepthOfField(DepthOfFieldData data)
        {
            var setting = PostEffectsClient.GetDepthOfField();
            setting.enabled = data.enabled;
            setting.focalLength = data.focalLength;
            setting.focalSize = data.focalSize;
            setting.aperture = data.aperture;
            setting.maxBlurSize = data.maxBlurSize;
            setting.maidFocus = data.maidSlotNo >= 0;
            setting.maidIndex = data.maidSlotNo >= 0 ? data.maidSlotNo : 0;
            PostEffectsClient.ApplyDepthOfField(setting);
            depthOfFieldMaidSlotId = data.maidSlotNo;
        }

        public PEData.ParaffinData GetParaffinData(int index)
        {
            return PostEffectsClient.GetParaffinData(index);
        }

        public void ApplyParaffin(int index, PEData.ParaffinData data)
        {
            PostEffectsClient.ApplyParaffin(index, data);
        }

        public PEData.DistanceFogData GetDistanceFogData(int index)
        {
            return PostEffectsClient.GetDistanceFogData(index);
        }

        public void ApplyDistanceFog(int index, PEData.DistanceFogData data)
        {
            PostEffectsClient.ApplyDistanceFog(index, data);
        }

        public PEData.RimlightData GetRimlightData(int index)
        {
            return PostEffectsClient.GetRimlightData(index);
        }

        public void ApplyRimlight(int index, PEData.RimlightData data)
        {
            PostEffectsClient.ApplyRimlight(index, data);
        }

        public GTToneMapData GetGTToneMapData()
        {
            var setting = PostEffectsClient.GetGTToneMap();
            return new GTToneMapData
            {
                enabled = setting.enabled,
                maxBrightness = setting.maxBrightness,
                contrast = setting.contrast,
                linearStart = setting.linearStart,
                linearLength = setting.linearLength,
                blackTightness = setting.blackTightness,
                blackOffset = setting.blackOffset,
            };
        }

        public void ApplyGTToneMap(GTToneMapData data)
        {
            var setting = PostEffectsClient.GetGTToneMap();
            setting.enabled = data.enabled;
            setting.maxBrightness = data.maxBrightness;
            setting.contrast = data.contrast;
            setting.linearStart = data.linearStart;
            setting.linearLength = data.linearLength;
            setting.blackTightness = data.blackTightness;
            setting.blackOffset = data.blackOffset;
            PostEffectsClient.ApplyGTToneMap(setting);
        }

        // ブルームは共有 DTO をそのまま流す (実体側と同じ平置き構造のため変換が要らない)
        public PEData.BloomData GetBloomData()
        {
            return PostEffectsClient.GetBloom();
        }

        public void ApplyBloom(PEData.BloomData data)
        {
            PostEffectsClient.ApplyBloom(data);
        }
    }
}
