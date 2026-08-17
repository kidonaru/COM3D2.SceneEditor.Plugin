using System;
using System.IO;
using System.Reflection;
using System.Linq;
using UnityEngine;
using System.Diagnostics;
using System.Collections;
using System.Text.RegularExpressions;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// タイムライン機能のパス・補間ユーティリティ。
    /// MTE から移植。タイムラインファイルの保存先は MTE と互換 (PhotoData\_Timeline)
    /// </summary>
    public static class PluginUtils
    {
        /// <summary>タイムライン設定の保存先 (SceneEditor の設定ディレクトリ配下)</summary>
        public static string ConfigPath
        {
            get => MTEUtils.CombinePaths(
                SceneEditor.Plugin.PluginUtils.PluginDataPath, "Timeline.xml");
        }

        public static string TimelineDirPath
        {
            get
            {
                string path = PhotoWindowManager.path_photo_folder + "_Timeline";
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                return path;
            }
        }

        public static Camera MainCamera
        {
            get => GameMain.Instance.MainCamera.camera;
        }

        public static string GetTimelinePath(string anmName, string directoryName)
        {
            return MTEUtils.CombinePaths(TimelineDirPath, directoryName, anmName + ".xml");
        }

        public static string ConvertThumPath(string path)
        {
            return Path.ChangeExtension(path, ".png");
        }

        private static readonly Regex _regexGroup = new Regex(@"\(\d+\)$", RegexOptions.Compiled);
        private static readonly Regex _regexMyRoomId = new Regex(@"MYR_\d+", RegexOptions.Compiled);

        public static int ExtractGroup(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return 0;
            }
            var match = _regexGroup.Match(input);
            if (match.Success)
            {
                return int.Parse(match.Value.Substring(1, match.Value.Length - 2));
            }
            return 0;
        }

        public static string RemoveGroupSuffix(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }
            return _regexGroup.Replace(input, "").Trim();
        }

        public static int ExtractMyRoomId(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return 0;
            }
            var match = _regexMyRoomId.Match(input);
            if (match.Success)
            {
                return int.Parse(match.Value.Substring(4));
            }
            return 0;
        }

        public static string GetGroupSuffix(int group)
        {
            if (group > 0)
            {
                return string.Format(" ({0})", group);
            }
            return "";
        }

        // エルミート曲線の補間計算
        public static float Hermite(
            float t0,
            float t1,
            float v0,
            float v1,
            float outTangent,
            float inTangent,
            float t)
        {
            float dt = t1 - t0;
            if (dt == 0) return v0; // 時間差がない場合は開始値を返す

            float t2 = t * t;
            float t3 = t2 * t;

            // エルミート基底関数
            float h00 = 2f * t3 - 3f * t2 + 1f;
            float h10 = t3 - 2f * t2 + t;
            float h01 = -2f * t3 + 3f * t2;
            float h11 = t3 - t2;

            // エルミート補間の計算
            float y = h00 * v0 +
                      h10 * dt * outTangent +
                      h01 * v1 +
                      h11 * dt * inTangent;

            return y;
        }

        // エルミート曲線の補間計算（簡略化版）
        public static float HermiteSimplified(
            float outTangent,
            float inTangent,
            float t)
        {
            return Hermite(0, 1, 0, 1, outTangent, inTangent, t);
        }

        // ValueDataの補間
        public static float HermiteValue(
            float t0,
            float t1,
            ValueData start,
            ValueData end,
            float t)
        {
            return Hermite(
                t0,
                t1,
                start.value,
                end.value,
                start.outTangent.value,
                end.inTangent.value,
                t);
        }

        // ValueDatasの補間
        public static float[] HermiteValues(
            float t0,
            float t1,
            ValueData[] start,
            ValueData[] end,
            float t)
        {
            var ret = new float[start.Length];
            for (int i = 0; i < start.Length; i++)
            {
                ret[i] = HermiteValue(t0, t1, start[i], end[i], t);
            }
            return ret;
        }

        // Vector3の補間
        public static Vector3 HermiteVector3(
            float t0,
            float t1,
            ValueData[] start,
            ValueData[] end,
            float t)
        {
            return HermiteValues(
                t0,
                t1,
                start,
                end,
                t
            ).ToVector3();
        }

        // Vector4の補間
        public static Vector4 HermiteVector4(
            float t0,
            float t1,
            ValueData[] start,
            ValueData[] end,
            float t)
        {
            return HermiteValues(
                t0,
                t1,
                start,
                end,
                t
            ).ToVector4();
        }

        // Colorの補間
        public static Color HermiteColor(
            float t0,
            float t1,
            ValueData[] start,
            ValueData[] end,
            float t)
        {
            return HermiteValues(
                t0,
                t1,
                start,
                end,
                t
            ).ToColor();
        }

        // Quaternionの補間
        public static Quaternion HermiteQuaternion(
            float t0,
            float t1,
            ValueData[] start,
            ValueData[] end,
            float t)
        {
            return HermiteValues(
                t0,
                t1,
                start,
                end,
                t
            ).ToQuaternion();
        }
    }
}