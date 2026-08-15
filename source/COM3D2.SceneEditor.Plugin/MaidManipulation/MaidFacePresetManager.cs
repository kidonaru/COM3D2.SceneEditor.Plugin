using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>表情プリセットのモーフ 1 つ分。値が 0 でないモーフだけ保存する</summary>
    public class FacePresetMorph
    {
        [XmlAttribute]
        public string name;
        [XmlAttribute]
        public float value;
    }

    /// <summary>ユーザー表情プリセット 1 件分。適用時は未記録のモーフを 0 に戻す</summary>
    public class FacePresetData
    {
        public bool mabataki;
        public List<FacePresetMorph> morphs = new List<FacePresetMorph>();
    }

    /// <summary>
    /// ユーザー表情プリセットの XML 保存/読み込み。
    /// シーンプリセットの表情記録 (ScenePresetManager) と同じ捕捉・適用ロジックを
    /// 表情単体向けに切り出したもの
    /// </summary>
    public static class MaidFacePresetManager
    {
        /// <summary>プリセット名の最大長。ポーズ保存 (MaidPoseFileManager) に合わせる</summary>
        private const int MAX_PRESET_NAME_LENGTH = 250;

        public static string presetFolderPath
            => Path.Combine(PluginUtils.PluginDataPath, "FacePreset");

        private static readonly XmlSerializer _serializer =
            new XmlSerializer(typeof(FacePresetData));

        /// <summary>GUI 描画中に呼ばれるため、列挙に失敗しても例外を投げず空リストを返す</summary>
        public static List<string> GetPresetNames()
        {
            try
            {
                if (!Directory.Exists(presetFolderPath))
                {
                    return new List<string>();
                }
                return Directory.GetFiles(presetFolderPath, "*.xml")
                    .Select(Path.GetFileNameWithoutExtension)
                    .OrderBy(name => name, StringComparer.Ordinal)
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
                return "表情名を入力してください";
            }
            if (presetName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return "表情名に使用できない文字が含まれています";
            }
            if (presetName.Length > MAX_PRESET_NAME_LENGTH)
            {
                return "表情名が長すぎます（" + MAX_PRESET_NAME_LENGTH + "文字まで）";
            }
            return null;
        }

        /// <summary>現在の表情を保存する。上書き確認は UI 側の責務</summary>
        public static void SavePreset(Maid maid, string presetName)
        {
            try
            {
                var data = Capture(maid);

                if (!Directory.Exists(presetFolderPath))
                {
                    Directory.CreateDirectory(presetFolderPath);
                }
                using (var stream = File.Create(GetPresetFilePath(presetName)))
                {
                    _serializer.Serialize(stream, data);
                }
                MTEUtils.Log("表情を保存しました: {0}", presetName);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                DialogPopupWindow.ShowDialog("表情の保存に失敗しました");
            }
        }

        /// <summary>プリセットを読み込んで適用する</summary>
        public static void LoadPreset(Maid maid, string presetName)
        {
            try
            {
                var filePath = GetPresetFilePath(presetName);
                if (!File.Exists(filePath))
                {
                    DialogPopupWindow.ShowDialog("表情ファイルが見つかりません: " + presetName);
                    return;
                }

                FacePresetData data;
                using (var stream = File.OpenRead(filePath))
                {
                    data = (FacePresetData)_serializer.Deserialize(stream);
                }
                Apply(maid, data);
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                DialogPopupWindow.ShowDialog("表情の読み込みに失敗しました");
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
                    MTEUtils.Log("表情を削除しました: {0}", presetName);
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogException(e);
                DialogPopupWindow.ShowDialog("表情の削除に失敗しました");
            }
        }

        /// <summary>現在の表情から保存データを組み立てる。値が 0 でないモーフだけ持つ</summary>
        private static FacePresetData Capture(Maid maid)
        {
            var data = new FacePresetData
            {
                mabataki = MaidFaceMorphController.GetMabataki(maid),
            };

            foreach (FaceMorphCategory category in Enum.GetValues(typeof(FaceMorphCategory)))
            {
                foreach (var def in MaidFaceMorphController.GetAvailableMorphs(maid, category))
                {
                    var value = MaidFaceMorphController.GetMorphValue(maid, def);
                    if (value != 0f)
                    {
                        data.morphs.Add(new FacePresetMorph { name = def.name, value = value });
                    }
                }
            }

            return data;
        }

        /// <summary>
        /// 保存データを適用する。未記録のモーフは 0 に戻し、保存時の表情をそのまま再現する
        /// </summary>
        private static void Apply(Maid maid, FacePresetData data)
        {
            var savedValues = new Dictionary<string, float>();
            foreach (var morph in data.morphs)
            {
                savedValues[morph.name] = morph.value;
            }

            foreach (FaceMorphCategory category in Enum.GetValues(typeof(FaceMorphCategory)))
            {
                foreach (var def in MaidFaceMorphController.GetAvailableMorphs(maid, category))
                {
                    float value;
                    if (!savedValues.TryGetValue(def.name, out value))
                    {
                        value = 0f;
                    }
                    MaidFaceMorphController.SetMorphValue(maid, def, value);
                }
            }

            MaidFaceMorphController.SetMabataki(maid, data.mabataki);
        }

        private static string GetPresetFilePath(string presetName)
        {
            return Path.Combine(presetFolderPath, presetName + ".xml");
        }
    }
}
