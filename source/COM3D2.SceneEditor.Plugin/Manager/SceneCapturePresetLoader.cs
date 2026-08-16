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
                distance = ParseFloat(Value(e, "Distance"), 2f),
                fov = ParseFloat(Value(e, "FieldOfView"), 35f),
            };
        }

        /// <summary>
        /// Misc/Background: 背景プレハブ名の文字列だけが入っている。
        /// 空なら背景は触らない (null)。id を逆引きできなければ prefab 名で復元させる。
        /// SceneCapture は背景の位置・回転・背景色を持たないため、位置回転は原点、色は触らない
        /// </summary>
        private static ScenePresetBackground ParseBackground(XElement misc)
        {
            var bgName = misc != null ? Value(misc, "Background") : null;
            if (string.IsNullOrEmpty(bgName))
            {
                return null;
            }

            BackgroundUtils.EnsureBgDataLoaded();
            var bgId = BackgroundUtils.GetBgId(bgName);
            return new ScenePresetBackground
            {
                bgId = bgId,
                bgPrefabName = bgId == null ? bgName : null,
                position = Vector3.zero,
                rotation = Vector3.zero,
                hasBgColor = false,
            };
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
                var main = entries[0];
                state.hasMain = true;
                state.mainRotation = ParseVector3(Value(main, "EulerAngles"));
                state.mainColor = ParseColor32(Value(main, "Color"));
                state.mainIntensity = ParseFloat(Value(main, "Intensity"), 0.95f);
                state.mainShadowStrength = ParseFloat(Value(main, "shadowStrength"), 0.098f);
            }

            foreach (var e in entries.Skip(1))
            {
                state.additionalLights.Add(ParseAdditionalLight(e));
            }
            if (lightShafts != null)
            {
                foreach (var e in lightShafts.Elements("LightShaft"))
                {
                    state.additionalLights.Add(ParseAdditionalLight(e));
                }
            }

            // メインも追加も無いプリセットではライトを触らない
            return state.hasMain || state.additionalLights.Count > 0 ? state : null;
        }

        private static ScenePresetAdditionalLight ParseAdditionalLight(XElement e)
        {
            return new ScenePresetAdditionalLight
            {
                type = ParseInt(Value(e, "Type"), (int)LightType.Point),
                position = ParseVector3(Value(e, "Position")),
                rotation = ParseVector3(Value(e, "EulerAngles")),
                color = ParseColor32(Value(e, "Color")),
                intensity = ParseFloat(Value(e, "Intensity"), 0.95f),
                range = ParseFloat(Value(e, "Range"), 10f),
                spotAngle = ParseFloat(Value(e, "SpotAngle"), 30f),
                enabled = ParseBool(Value(e, "Enabled"), true),
            };
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
                (byte)ParseInt(parts[0], 255), (byte)ParseInt(parts[1], 255),
                (byte)ParseInt(parts[2], 255), (byte)ParseInt(parts[3], 255));
        }
    }
}
