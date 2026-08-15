using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>指プリセット 1 件分。手指・足指の全部位の状態を持つ</summary>
    public class FingerPresetData
    {
        public List<FingerUnitState> units = new List<FingerUnitState>();
    }

    /// <summary>
    /// 指プリセットの XML 保存/読み込み。
    /// 1 ファイルで手指と足指の両方を扱う
    /// </summary>
    public static class MaidFingerPresetManager
    {
        /// <summary>プリセット名の最大長。表情プリセット (MaidFacePresetManager) に合わせる</summary>
        private const int MAX_PRESET_NAME_LENGTH = 250;

        public static string presetFolderPath
            => Path.Combine(PluginUtils.PluginDataPath, "FingerPreset");

        private static readonly XmlSerializer _serializer =
            new XmlSerializer(typeof(FingerPresetData));

        /// <summary>プリセットが対象とする全部位</summary>
        public static readonly FingerBlendType[] BlendTypes =
        {
            FingerBlendType.RightArm,
            FingerBlendType.LeftArm,
            FingerBlendType.RightLeg,
            FingerBlendType.LeftLeg,
        };

        /// <summary>
        /// 保存済みプリセット名の一覧。
        /// GUI 描画中に呼ばれるため失敗しても例外は投げず、空一覧を返す
        /// </summary>
        public static List<string> GetPresetNames()
        {
            try
            {
                if (!Directory.Exists(presetFolderPath))
                {
                    return new List<string>();
                }

                return Directory.GetFiles(presetFolderPath, "*.xml")
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .Select(Path.GetFileNameWithoutExtension)
                    .ToList();
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                return new List<string>();
            }
        }

        public static bool Exists(string presetName)
        {
            return File.Exists(GetPresetFilePath(presetName));
        }

        /// <summary>プリセット名の検証。問題があればエラーメッセージ、なければ null を返す</summary>
        public static string ValidatePresetName(string presetName)
        {
            if (string.IsNullOrEmpty(presetName))
            {
                return "プリセット名を入力してください";
            }
            if (presetName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return "プリセット名に使用できない文字が含まれています";
            }
            if (presetName.Length > MAX_PRESET_NAME_LENGTH)
            {
                return "プリセット名が長すぎます（" + MAX_PRESET_NAME_LENGTH + "文字まで）";
            }
            return null;
        }

        /// <summary>手指・足指の現在の状態を保存する。上書き確認は UI 側の責務</summary>
        public static void SavePreset(
            MaidFingerBlendController controller,
            string presetName)
        {
            try
            {
                var data = new FingerPresetData();
                foreach (var type in BlendTypes)
                {
                    var unit = controller.GetUnit(type);
                    if (unit == null)
                    {
                        DialogPopupWindow.ShowDialog("指の状態を取得できませんでした");
                        return;
                    }
                    data.units.Add(unit.CaptureState());
                }

                if (!Directory.Exists(presetFolderPath))
                {
                    Directory.CreateDirectory(presetFolderPath);
                }
                using (var stream = File.Create(GetPresetFilePath(presetName)))
                {
                    _serializer.Serialize(stream, data);
                }
                MTEUtils.Log("指プリセットを保存しました: {0}", presetName);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                DialogPopupWindow.ShowDialog("指プリセットの保存に失敗しました");
            }
        }

        /// <summary>
        /// プリセットを読み込んで適用する。記録されていない部位には触れない。
        /// targets を渡すとその部位だけを適用する（null なら記録されている全部位）
        /// </summary>
        public static void LoadPreset(
            MaidFingerBlendController controller,
            string presetName,
            ICollection<FingerBlendType> targets = null)
        {
            var filePath = GetPresetFilePath(presetName);
            if (!File.Exists(filePath))
            {
                DialogPopupWindow.ShowDialog("指プリセットが見つかりません: " + presetName);
                return;
            }

            var data = LoadData(filePath);
            if (data == null)
            {
                DialogPopupWindow.ShowDialog("指プリセットの読み込みに失敗しました");
                return;
            }

            // GUI 描画中に呼ばれるため、適用中に落ちても描画を壊さないよう握りつぶす
            try
            {
                foreach (var state in data.units)
                {
                    if (targets != null && !targets.Contains(state.type))
                    {
                        continue;
                    }

                    var unit = controller.GetUnit(state.type);
                    if (unit == null)
                    {
                        continue;
                    }
                    unit.RestoreState(state);
                    unit.Apply();
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                DialogPopupWindow.ShowDialog("指プリセットの適用に失敗しました");
            }
        }

        /// <summary>XML を削除する。確認は UI 側の責務</summary>
        public static void DeletePreset(string presetName)
        {
            try
            {
                var filePath = GetPresetFilePath(presetName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    MTEUtils.Log("指プリセットを削除しました: {0}", presetName);
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                DialogPopupWindow.ShowDialog("指プリセットの削除に失敗しました");
            }
        }

        /// <summary>1 ファイル読み込み。壊れていても呼び出し元がエラー表示できるよう null を返す</summary>
        private static FingerPresetData LoadData(string filePath)
        {
            try
            {
                using (var stream = File.OpenRead(filePath))
                {
                    return (FingerPresetData)_serializer.Deserialize(stream);
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                return null;
            }
        }

        private static string GetPresetFilePath(string presetName)
        {
            return Path.Combine(presetFolderPath, presetName + ".xml");
        }
    }
}
