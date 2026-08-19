using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using COM3D2.MotionTimelineEditor;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>
    /// COM3D2.PartsEditWithStudio.Plugin のプリセット XML (ObjectData) と互換の DTO。
    /// 同プラグインの DLL には依存せず、ファイル形式だけを合わせる。
    /// ルート要素名・子要素名は先方の XmlSerializer 出力と一致させること (変更禁止)
    /// </summary>
    [XmlRoot("ObjectData")]
    public class PartsEditPresetData
    {
        public string version = "0.1.6";
        public string slotName = "";
        public bool bMaidParts = true;
        public TransformData rootData = null;
        public bool bYure = true;
        [XmlArrayItem("TransformData")]
        public List<TransformData> transformDataList = new List<TransformData>();

        public class TransformData
        {
            public string name;
            public Vec3 position = new Vec3();
            public Quat rotation = new Quat { w = 1f };
            public Vec3 scale = new Vec3 { x = 1f, y = 1f, z = 1f };
        }

        // Unity の Vector3/Quaternion を直接シリアライズしない (ScenePresetData と同じ方針)。
        // 先方が吐く rotation 内の eulerAngles 要素は未定義のため読み飛ばされる
        public class Vec3
        {
            public float x;
            public float y;
            public float z;

            public Vector3 ToVector3() => new Vector3(x, y, z);
            public static Vec3 From(Vector3 v) => new Vec3 { x = v.x, y = v.y, z = v.z };
        }

        public class Quat
        {
            public float x;
            public float y;
            public float z;
            public float w;

            public Quaternion ToQuaternion() => new Quaternion(x, y, z, w);
            public static Quat From(Quaternion q) => new Quat { x = q.x, y = q.y, z = q.z, w = q.w };
        }
    }

    /// <summary>
    /// PartsEdit 互換プリセットの読み書き。
    /// 保存先は PartsEdit 本体と同じ UnityInjector\Config\PartsEdit\ を共用し、相互運用できるようにする
    /// </summary>
    public static class PartsEditPresetIO
    {
        public static readonly string directoryPath =
            Path.Combine(PluginUtils.UserDataPath, "PartsEdit");

        private static readonly XmlSerializer _serializer =
            new XmlSerializer(typeof(PartsEditPresetData));

        /// <summary>ファイル名の長さ上限。パス全体の長さ制限に引っかかる前に弾く</summary>
        private const int MAX_PRESET_NAME_LENGTH = 250;

        private static string GetPresetFilePath(string presetName)
        {
            return Path.Combine(directoryPath, presetName + ".xml");
        }

        /// <summary>プリセット名 (拡張子なし) の一覧。毎回ディレクトリを見に行く</summary>
        public static List<string> GetPresetNames()
        {
            if (!Directory.Exists(directoryPath))
            {
                return new List<string>();
            }
            return Directory.GetFiles(directoryPath, "*.xml", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
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

        /// <summary>XML を削除する。確認は UI 側の責務</summary>
        public static void Delete(string presetName)
        {
            var path = GetPresetFilePath(presetName);
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    MTEUtils.Log("PartsEdit プリセットを削除しました: {0}", presetName);
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogError("PartsEdit プリセットの削除に失敗しました: {0}\n{1}", path, e);
            }
        }

        /// <summary>プリセットを読み込む。失敗時は null (ログ出力あり)</summary>
        public static PartsEditPresetData Load(string presetName)
        {
            var path = GetPresetFilePath(presetName);
            try
            {
                using (var reader = new StreamReader(path, new UTF8Encoding(false)))
                {
                    var data = (PartsEditPresetData)_serializer.Deserialize(reader);
                    // 保存先は PartsEdit 本体と共用で手編集もされ得る。
                    // xsi:nil 等でリストが null になっても以降で参照するため、ここで潰しておく
                    if (data != null && data.transformDataList == null)
                    {
                        data.transformDataList = new List<PartsEditPresetData.TransformData>();
                    }
                    return data;
                }
            }
            catch (Exception e)
            {
                MTEUtils.LogError("PartsEdit プリセットの読み込みに失敗しました: {0}\n{1}", path, e);
                return null;
            }
        }

        /// <summary>
        /// 選択中スロットの編集済みボーンをプリセットとして保存する。
        /// PartsEdit 本体は全ボーンを保存するが、こちらは編集差分のみ書く
        /// (先方のロード処理は列挙されたボーンだけ適用するため相互運用に支障はない)
        /// </summary>
        public static bool Save(Maid maid, string slotName, string presetName, BoneEditStore store)
        {
            var slotObj = SlotBoneManager.GetSlotObject(maid, slotName);
            if (slotObj == null)
            {
                return false;
            }

            var data = new PartsEditPresetData
            {
                slotName = slotName,
                // PartsEdit と同じスロット単位の判定で現在の揺れ状態を書く
                bYure = SlotYureUtil.GetSlotYureState(maid, slotName),
                // 先方の GetFileList(category, name) が rootData.name を参照するため null にしない。
                // 値は保存時点のスナップショット (こちらではルートを編集しない)
                rootData = new PartsEditPresetData.TransformData
                {
                    name = slotObj.name,
                    position = PartsEditPresetData.Vec3.From(slotObj.transform.localPosition),
                    rotation = PartsEditPresetData.Quat.From(slotObj.transform.localRotation),
                    scale = PartsEditPresetData.Vec3.From(slotObj.transform.localScale),
                },
            };

            foreach (var entry in store.GetEntries(slotName))
            {
                data.transformDataList.Add(new PartsEditPresetData.TransformData
                {
                    name = entry.boneName,
                    position = PartsEditPresetData.Vec3.From(entry.position),
                    rotation = PartsEditPresetData.Quat.From(entry.rotation),
                    scale = PartsEditPresetData.Vec3.From(entry.scale),
                });
            }

            return WriteFile(presetName, data);
        }

        /// <summary>
        /// モデルの編集済みボーンをプリセットとして保存する。
        /// PartsEdit のモデル用プリセット (bMaidParts=false, slotName 空, bYure=false) と互換
        /// </summary>
        public static bool SaveModel(GameObject modelObj, string presetName, BoneEditStore store)
        {
            if (modelObj == null)
            {
                return false;
            }

            var data = new PartsEditPresetData
            {
                slotName = "",
                bMaidParts = false,
                bYure = false,
                // 先方の GetFileList(category, name) が rootData.name を参照するため null にしない。
                // 値は保存時点のスナップショット (こちらではルートを編集しない)
                rootData = new PartsEditPresetData.TransformData
                {
                    name = modelObj.name,
                    position = PartsEditPresetData.Vec3.From(modelObj.transform.localPosition),
                    rotation = PartsEditPresetData.Quat.From(modelObj.transform.localRotation),
                    scale = PartsEditPresetData.Vec3.From(modelObj.transform.localScale),
                },
            };

            foreach (var entry in store.GetEntries(BoneEditManager.ModelSlotKey))
            {
                data.transformDataList.Add(new PartsEditPresetData.TransformData
                {
                    name = entry.boneName,
                    position = PartsEditPresetData.Vec3.From(entry.position),
                    rotation = PartsEditPresetData.Quat.From(entry.rotation),
                    scale = PartsEditPresetData.Vec3.From(entry.scale),
                });
            }

            return WriteFile(presetName, data);
        }

        /// <summary>DTO を XML ファイルへ書く (BOM 無し UTF-8、PartsEdit 本体と同じ)</summary>
        private static bool WriteFile(string presetName, PartsEditPresetData data)
        {
            var path = GetPresetFilePath(presetName);
            try
            {
                Directory.CreateDirectory(directoryPath);
                using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
                {
                    _serializer.Serialize(writer, data);
                }
                return true;
            }
            catch (Exception e)
            {
                MTEUtils.LogError("PartsEdit プリセットの保存に失敗しました: {0}\n{1}", path, e);
                return false;
            }
        }

        /// <summary>
        /// プリセット内のボーン TRS のうち、選択中スロットに実在するボーンの Transform を列挙する。
        /// 履歴記録 (BeforeEdit) の対象集合と適用対象の両方に使う
        /// </summary>
        public static List<Transform> ResolveBones(GameObject slotObj, PartsEditPresetData data)
        {
            var result = new List<Transform>();
            foreach (var trsData in data.transformDataList)
            {
                var bone = SlotBoneManager.FindBone(slotObj, trsData.name);
                if (bone != null)
                {
                    result.Add(bone);
                }
            }
            return result;
        }

        /// <summary>
        /// プリセットを選択中スロットへ適用する。ファイル内の slotName は無視する。
        /// rootData は扱わない (ボーン TRS と bYure のみ)。
        /// 見つからないボーンはスキップし、現在値と同値のボーンは編集扱いにしない
        /// </summary>
        public static int Apply(Maid maid, string slotName, PartsEditPresetData data, BoneEditStore store)
        {
            var slotObj = SlotBoneManager.GetSlotObject(maid, slotName);
            if (slotObj == null)
            {
                return 0;
            }

            var itemFileName = SlotBoneManager.GetSlotItemFileName(maid, slotName);
            var applied = ApplyTransformList(slotObj, slotName, itemFileName, data, store);

            // 保存時のスロット揺れ状態を復元する (bYure)。OFF のプリセットでは
            // ボーン編集値が物理に毎フレーム上書きされるのを防ぐ役目も兼ねる
            SlotYureUtil.SetSlotYureState(maid, slotName, data.bYure);

            return applied;
        }

        /// <summary>
        /// プリセットをモデルへ適用する。ボーン TRS のみ扱い、
        /// rootData は適用しない (モデルルートの配置は外部プラグイン管理のため)。
        /// PartsEdit 本体はモデルにも rootData.scale を適用するが、ここでは触らない
        /// </summary>
        public static int ApplyModel(GameObject modelObj, PartsEditPresetData data, BoneEditStore store)
        {
            if (modelObj == null)
            {
                return 0;
            }
            return ApplyTransformList(modelObj, BoneEditManager.ModelSlotKey, null, data, store);
        }

        /// <summary>
        /// プリセット内のボーン TRS を rootObj 配下へ適用し、適用数を返す。
        /// 見つからないボーンはスキップし、現在値と同値のボーンは編集扱いにしない
        /// </summary>
        private static int ApplyTransformList(GameObject rootObj, string slotKey,
            string itemFileName, PartsEditPresetData data, BoneEditStore store)
        {
            var applied = 0;
            foreach (var trsData in data.transformDataList)
            {
                var bone = SlotBoneManager.FindBone(rootObj, trsData.name);
                if (bone == null)
                {
                    continue;
                }

                var position = trsData.position.ToVector3();
                var rotation = trsData.rotation.ToQuaternion();
                var scale = trsData.scale.ToVector3();

                // PartsEdit 本体は未編集ボーンも全て保存するため、値が変わらないものまで
                // 編集済み (*) にしないよう同値はスキップする
                if (bone.localPosition == position
                    && bone.localRotation == rotation
                    && bone.localScale == scale)
                {
                    continue;
                }

                // 先に呼んで適用前の値を元値として控える (ScenePresetManager.ApplyBoneEdits と同じ)
                store.RecordEdit(slotKey, itemFileName, bone);

                bone.localPosition = position;
                bone.localRotation = rotation;
                bone.localScale = scale;

                store.RecordEdit(slotKey, itemFileName, bone);
                applied++;
            }
            return applied;
        }
    }
}
