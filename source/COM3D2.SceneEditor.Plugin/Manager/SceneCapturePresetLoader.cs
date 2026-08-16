using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>SceneCapture プリセットを SceneEditor 内部形式へ変換した結果</summary>
    public class SceneCaptureConvertedPreset
    {
        public ScenePresetCamera camera;
        public ScenePresetBackground background;
        public ScenePresetLight light;

        /// <summary>外部プロバイダへそのまま渡す &lt;Preset&gt; XML 全体</summary>
        public string rawXml;

        public bool hasModels;
        public bool hasEffects;
    }

    /// <summary>
    /// SceneCapture プラグインのプリセット XML（ルート &lt;Preset&gt;）を読み取り、
    /// カメラ・背景・ライトを SceneEditor の内部形式へ変換する。
    /// Models / Effects の中身は解釈せず、外部プロバイダへの委譲可否だけ判定する
    /// </summary>
    public static class SceneCapturePresetLoader
    {
        // 要素が欠落・不正だったときのフォールバック。
        // ライト系は SceneEditor 自身の既定値 (StudioLightManager) に揃え、
        // 以下はゲーム側の初期値に合わせている
        private const float DEFAULT_SHADOW_STRENGTH = 0.098f;
        private const float DEFAULT_CAMERA_DISTANCE = 2f;
        private const float DEFAULT_CAMERA_FOV = 35f;

        public static SceneCaptureConvertedPreset Parse(string xmlText)
        {
            var doc = XDocument.Parse(xmlText);
            var root = doc.Root;
            if (root == null || root.Name.LocalName != "Preset")
            {
                throw new InvalidDataException("SceneCapture プリセットのルート要素が不正です");
            }

            var result = new SceneCaptureConvertedPreset { rawXml = xmlText };

            // 1 セクションの失敗で全体を止めない。変換できたものだけ適用する
            try { result.camera = ParseCamera(root.Element("Camera")); }
            catch (Exception e) { LogSectionError("Camera", e); }
            try { result.background = ParseBackground(root.Element("Misc")); }
            catch (Exception e) { LogSectionError("Misc", e); }
            try { result.light = ParseLights(root.Element("Lights"), root.Element("LightShafts")); }
            catch (Exception e) { LogSectionError("Lights", e); }

            result.hasModels = root.Element("Models") != null
                && root.Element("Models").Elements("Model").Any();
            result.hasEffects = root.Element("Effects") != null
                && root.Element("Effects").Elements().Any();

            return result;
        }

        private static void LogSectionError(string section, Exception e)
        {
            MTEUtils.LogWarning("SceneCapture プリセットの {0} を変換できませんでした", section);
            MTEUtils.LogException(e);
        }

        /// <summary>
        /// Camera: Position は注視点、Rotation は transform euler。
        /// SceneEditor のオービット表現へは yaw=euler.y / pitch=euler.x / roll=euler.z で写す
        /// </summary>
        private static ScenePresetCamera ParseCamera(XElement e)
        {
            if (e == null)
            {
                return null;
            }
            var rotation = ParseVector3(Value(e, "Rotation"));
            return new ScenePresetCamera
            {
                targetPos = ParseVector3(Value(e, "Position")),
                yaw = rotation.y,
                pitch = rotation.x,
                roll = rotation.z,
                distance = ParseFloat(Value(e, "Distance"), DEFAULT_CAMERA_DISTANCE),
                fov = ParseFloat(Value(e, "FieldOfView"), DEFAULT_CAMERA_FOV),
            };
        }

        /// <summary>背景を消していることを表す SceneCapture 側のラベル</summary>
        private const string BACKGROUND_HIDE_LABEL = "非表示";

        /// <summary>SceneCapture の背景ラベルの区切り（"カテゴリ: 名前"）</summary>
        private const string BACKGROUND_LABEL_SEPARATOR = ": ";

        /// <summary>
        /// Misc/Background: prefab 名ではなく、SceneCapture の背景コンボボックスの
        /// 表示ラベル "カテゴリ: 名前"（未設定なら空、背景なしなら "非表示"）が入っている。
        /// 空なら背景を触らない (null)。
        /// SceneCapture は背景の位置・回転・背景色を持たないため、位置回転は原点、色は触らない
        /// </summary>
        private static ScenePresetBackground ParseBackground(XElement misc)
        {
            var label = misc != null ? Value(misc, "Background") : null;
            if (string.IsNullOrEmpty(label))
            {
                return null;
            }

            var state = new ScenePresetBackground
            {
                position = Vector3.zero,
                rotation = Vector3.zero,
                hasBgColor = false,
            };

            if (label == BACKGROUND_HIDE_LABEL)
            {
                state.deleted = true;
                return state;
            }

            var separator = label.IndexOf(BACKGROUND_LABEL_SEPARATOR, StringComparison.Ordinal);
            if (separator > 0)
            {
                state.bgId = BackgroundUtils.GetBgIdByCategoryName(
                    label.Substring(0, separator),
                    label.Substring(separator + BACKGROUND_LABEL_SEPARATOR.Length));
            }

            // 未導入 MOD の背景などは引けない。誤った背景に化けさせず、そのまま維持する
            if (state.bgId == null)
            {
                MTEUtils.LogWarning("SceneCapture プリセットの背景が見つかりません: {0}", label);
                return null;
            }
            return state;
        }

        /// <summary>
        /// Lights: 先頭 1 灯はゲームのメインライト、以降は追加ライト。
        /// LightShafts はシャフト固有要素を捨て、共通 12 要素だけ追加ライトとして写す
        /// </summary>
        private static ScenePresetLight ParseLights(XElement lights, XElement lightShafts)
        {
            var state = new ScenePresetLight();

            var entries = lights != null
                ? lights.Elements("Light").ToList() : new List<XElement>();
            if (entries.Count > 0)
            {
                // 1 灯目が壊れていても追加ライトは活かしたいので個別に握る
                try
                {
                    var main = entries[0];
                    state.mainRotation = ParseVector3(Value(main, "EulerAngles"));
                    state.mainColor = ParseColor32(Value(main, "Color"));
                    state.mainIntensity = ParseFloat(
                        Value(main, "Intensity"), StudioLightManager.DefaultIntensity);
                    state.mainShadowStrength = ParseFloat(
                        Value(main, "shadowStrength"), DEFAULT_SHADOW_STRENGTH);
                    state.hasMain = true;
                }
                catch (Exception e)
                {
                    LogSectionError("Lights/Light[0]", e);
                }
            }

            AddAdditionalLights(state, entries.Skip(1), "Lights/Light");
            if (lightShafts != null)
            {
                AddAdditionalLights(
                    state, lightShafts.Elements("LightShaft"), "LightShafts/LightShaft");
            }

            // メインも追加も無いプリセットではライトを触らない
            return state.hasMain || state.additionalLights.Count > 0 ? state : null;
        }

        /// <summary>1 灯の変換失敗で他の灯を巻き添えにしないよう、要素ごとに握って積む</summary>
        private static void AddAdditionalLights(
            ScenePresetLight state, IEnumerable<XElement> entries, string section)
        {
            foreach (var entry in entries)
            {
                try
                {
                    state.additionalLights.Add(ParseAdditionalLight(entry));
                }
                catch (Exception e)
                {
                    LogSectionError(section, e);
                }
            }
        }

        private static ScenePresetAdditionalLight ParseAdditionalLight(XElement e)
        {
            return new ScenePresetAdditionalLight
            {
                type = ParseLightType(Value(e, "Type")),
                position = ParseVector3(Value(e, "Position")),
                rotation = ParseVector3(Value(e, "EulerAngles")),
                color = ParseColor32(Value(e, "Color")),
                intensity = ParseFloat(
                    Value(e, "Intensity"), StudioLightManager.DefaultIntensity),
                range = ParseFloat(Value(e, "Range"), StudioLightManager.DefaultRange),
                spotAngle = ParseFloat(
                    Value(e, "SpotAngle"), StudioLightManager.DefaultSpotAngle),
                enabled = ParseBool(Value(e, "Enabled"), true),
            };
        }

        /// <summary>
        /// 追加ライトの種別。SceneEditor の追加ライトは Point / Spot しか扱えないため、
        /// SceneCapture 側に普通に出てくる Directional は Point へ丸めて警告を残す
        /// </summary>
        private static int ParseLightType(string s)
        {
            var type = ParseInt(s, (int)LightType.Point);
            if (type == (int)LightType.Point || type == (int)LightType.Spot)
            {
                return type;
            }

            MTEUtils.LogWarning(
                "SceneCapture の追加ライト種別 {0} は未対応のため Point として追加します",
                (LightType)type);
            return (int)LightType.Point;
        }

        private static string Value(XElement parent, string name)
        {
            var child = parent.Element(name);
            return child != null ? child.Value : null;
        }

        // SceneCapture の書式: float は InvariantCulture、Vector3 "x,y,z"、Color32 "r,g,b,a" (0-255)

        private static float ParseFloat(string s, float fallback)
        {
            float v;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                ? v : fallback;
        }

        private static int ParseInt(string s, int fallback)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)
                ? v : fallback;
        }

        private static bool ParseBool(string s, bool fallback)
        {
            bool v;
            return bool.TryParse(s, out v) ? v : fallback;
        }

        private static Vector3 ParseVector3(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return Vector3.zero;
            }
            var parts = s.Split(',');
            if (parts.Length != 3)
            {
                throw new FormatException("Vector3 の書式が不正です: " + s);
            }
            return new Vector3(
                ParseFloat(parts[0], 0f), ParseFloat(parts[1], 0f), ParseFloat(parts[2], 0f));
        }

        private static Color ParseColor32(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return Color.white;
            }
            var parts = s.Split(',');
            if (parts.Length != 4)
            {
                throw new FormatException("Color32 の書式が不正です: " + s);
            }
            return new Color32(
                ParseColorComponent(parts[0]), ParseColorComponent(parts[1]),
                ParseColorComponent(parts[2]), ParseColorComponent(parts[3]));
        }

        /// <summary>
        /// 色成分 1 つ。外部 XML の値をそのまま byte へ落とすと
        /// 範囲外の値が下位バイトだけ残って別の色に化けるためクランプする
        /// </summary>
        private static byte ParseColorComponent(string s)
        {
            return (byte)Mathf.Clamp(ParseInt(s, 255), 0, 255);
        }
    }
}
