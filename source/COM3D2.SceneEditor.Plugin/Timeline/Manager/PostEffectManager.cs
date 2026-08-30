// ポストエフェクトの実体は PostEffects.Plugin が持つ。alias の理由は PostEffectsBridge を参照
extern alias PostEffectsPlugin;
using UnityEngine;
using PEP = PostEffectsPlugin::COM3D25.PostEffects.Plugin;

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
    /// ここは TimelineBridge への素通し委譲と DTO 変換だけを持つ。
    /// 全メンバが「PostEffects.Plugin 導入済み」を前提にするため、
    /// 未導入時のガードは呼び出し元 (TimelineIntegration / UI) が行うこと
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

        // 各エフェクト数の上限。実体側 (PostEffects.Plugin) のシェーダーバッファ上限に従う。
        // 静的フィールドにするとこのクラスへ触れただけで PEP の型ロードを誘発し、
        // 未導入環境で isAvailable ガードを迲回してしまうためプロパティにする
        public static int MaxParaffinCount => PEP.TimelineBridge.MaxParaffinCount;
        public static int MaxDistanceFogCount => PEP.TimelineBridge.MaxDistanceFogCount;
        public static int MaxRimlightCount => PEP.TimelineBridge.MaxRimlightCount;

        /// <summary>パラフィン数。実体は PostEffects.Plugin 側が所有する。
        /// タイムライン読込中は timeline 側 (TimelineXml に保存) と同期する</summary>
        public int paraffinCount
        {
            get => PEP.TimelineBridge.paraffinCount;
            set
            {
                PEP.TimelineBridge.paraffinCount = value;
                if (timeline != null)
                {
                    timeline.paraffinCount = value;
                }
            }
        }

        /// <summary>距離フォグ数。所有者と同期規約はパラフィンと同じ</summary>
        public int distanceFogCount
        {
            get => PEP.TimelineBridge.distanceFogCount;
            set
            {
                PEP.TimelineBridge.distanceFogCount = value;
                if (timeline != null)
                {
                    timeline.distanceFogCount = value;
                }
            }
        }

        /// <summary>リムライト数。所有者と同期規約はパラフィンと同じ</summary>
        public int rimlightCount
        {
            get => PEP.TimelineBridge.rimlightCount;
            set
            {
                PEP.TimelineBridge.rimlightCount = value;
                if (timeline != null)
                {
                    timeline.rimlightCount = value;
                }
            }
        }

        public bool paraffinEnabled
        {
            get => PEP.TimelineBridge.paraffinEnabled;
            set => PEP.TimelineBridge.paraffinEnabled = value;
        }

        public bool distanceFogEnabled
        {
            get => PEP.TimelineBridge.distanceFogEnabled;
            set => PEP.TimelineBridge.distanceFogEnabled = value;
        }

        public bool rimlightEnabled
        {
            get => PEP.TimelineBridge.rimlightEnabled;
            set => PEP.TimelineBridge.rimlightEnabled = value;
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
                PEP.TimelineBridge.paraffinCount = timeline.paraffinCount;
                PEP.TimelineBridge.distanceFogCount = timeline.distanceFogCount;
                PEP.TimelineBridge.rimlightCount = timeline.rimlightCount;
            }
        }

        public void DisableAllEffects()
        {
            PEP.TimelineBridge.paraffinEnabled = false;
            PEP.TimelineBridge.distanceFogEnabled = false;
            PEP.TimelineBridge.rimlightEnabled = false;

            var dof = PEP.TimelineBridge.GetDepthOfField();
            dof.enabled = false;
            PEP.TimelineBridge.ApplyDepthOfField(dof);

            var toneMap = PEP.TimelineBridge.GetGTToneMap();
            toneMap.enabled = false;
            PEP.TimelineBridge.ApplyGTToneMap(toneMap);
        }

        public DepthOfFieldData GetDepthOfFieldData()
        {
            var setting = PEP.TimelineBridge.GetDepthOfField();
            return new DepthOfFieldData
            {
                enabled = setting.enabled,
                focalLength = setting.focalLength,
                focalSize = setting.focalSize,
                aperture = setting.aperture,
                maxBlurSize = setting.maxBlurSize,
                // メイド追従は PEP 側で maidFocus + maidIndex に分かれている
                maidSlotNo = setting.maidFocus ? setting.maidIndex : -1,
            };
        }

        public void ApplyDepthOfField(DepthOfFieldData data)
        {
            var setting = PEP.TimelineBridge.GetDepthOfField();
            setting.enabled = data.enabled;
            setting.focalLength = data.focalLength;
            setting.focalSize = data.focalSize;
            setting.aperture = data.aperture;
            setting.maxBlurSize = data.maxBlurSize;
            setting.maidFocus = data.maidSlotNo >= 0;
            setting.maidIndex = data.maidSlotNo >= 0 ? data.maidSlotNo : 0;
            PEP.TimelineBridge.ApplyDepthOfField(setting);
            depthOfFieldMaidSlotId = data.maidSlotNo;

            studioHack.OnUpdateDepthOfField();
        }

        public PEP.ColorParaffinData GetParaffinData(int index)
        {
            return PEP.TimelineBridge.GetParaffinData(index);
        }

        public void ApplyParaffin(int index, PEP.ColorParaffinData data)
        {
            PEP.TimelineBridge.ApplyParaffin(index, data);
        }

        public PEP.DistanceFogData GetDistanceFogData(int index)
        {
            return PEP.TimelineBridge.GetDistanceFogData(index);
        }

        public void ApplyDistanceFog(int index, PEP.DistanceFogData data)
        {
            PEP.TimelineBridge.ApplyDistanceFog(index, data);
        }

        public PEP.RimlightData GetRimlightData(int index)
        {
            return PEP.TimelineBridge.GetRimlightData(index);
        }

        public void ApplyRimlight(int index, PEP.RimlightData data)
        {
            PEP.TimelineBridge.ApplyRimlight(index, data);
        }

        public GTToneMapData GetGTToneMapData()
        {
            var setting = PEP.TimelineBridge.GetGTToneMap();
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
            var setting = PEP.TimelineBridge.GetGTToneMap();
            setting.enabled = data.enabled;
            setting.maxBrightness = data.maxBrightness;
            setting.contrast = data.contrast;
            setting.linearStart = data.linearStart;
            setting.linearLength = data.linearLength;
            setting.blackTightness = data.blackTightness;
            setting.blackOffset = data.blackOffset;
            PEP.TimelineBridge.ApplyGTToneMap(setting);
        }
    }
}
