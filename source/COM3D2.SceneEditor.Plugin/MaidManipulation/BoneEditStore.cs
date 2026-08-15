using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>1 ボーン分の編集差分。リセットで戻せるよう元値も抱える</summary>
    public class BoneEditEntry
    {
        public string slotName;
        /// <summary>記録時にスロットへ載っていたモデルファイル名。着替え判定に使う</summary>
        public string itemFileName;
        public string boneName;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public Vector3 origPosition;
        public Quaternion origRotation;
        public Vector3 origScale;
    }

    /// <summary>メイド 1 人分のボーン編集差分ストア</summary>
    public class BoneEditStore
    {
        // slotName -> boneName -> entry
        private readonly Dictionary<string, Dictionary<string, BoneEditEntry>> _entries
            = new Dictionary<string, Dictionary<string, BoneEditEntry>>();

        public bool isEmpty => _entries.Values.All(d => d.Count == 0);

        /// <summary>現在の Transform 値を編集値として記録する。初回だけ元値を控える</summary>
        public void RecordEdit(string slotName, string itemFileName, Transform bone)
        {
            if (string.IsNullOrEmpty(slotName) || bone == null)
            {
                return;
            }

            Dictionary<string, BoneEditEntry> slotDic;
            if (!_entries.TryGetValue(slotName, out slotDic))
            {
                slotDic = new Dictionary<string, BoneEditEntry>();
                _entries[slotName] = slotDic;
            }

            BoneEditEntry entry;
            if (!slotDic.TryGetValue(bone.name, out entry))
            {
                entry = new BoneEditEntry
                {
                    slotName = slotName,
                    itemFileName = itemFileName,
                    boneName = bone.name,
                    origPosition = bone.localPosition,
                    origRotation = bone.localRotation,
                    origScale = bone.localScale,
                };
                slotDic[bone.name] = entry;
            }

            entry.position = bone.localPosition;
            entry.rotation = bone.localRotation;
            entry.scale = bone.localScale;
        }

        public BoneEditEntry GetEntry(string slotName, string boneName)
        {
            Dictionary<string, BoneEditEntry> slotDic;
            if (string.IsNullOrEmpty(slotName) || string.IsNullOrEmpty(boneName)
                || !_entries.TryGetValue(slotName, out slotDic))
            {
                return null;
            }

            BoneEditEntry entry;
            return slotDic.TryGetValue(boneName, out entry) ? entry : null;
        }

        /// <summary>スロットの編集済みボーン。列挙中の削除に耐えるようコピーを返す</summary>
        public List<BoneEditEntry> GetEntries(string slotName)
        {
            Dictionary<string, BoneEditEntry> slotDic;
            if (string.IsNullOrEmpty(slotName) || !_entries.TryGetValue(slotName, out slotDic))
            {
                return new List<BoneEditEntry>();
            }
            return slotDic.Values.ToList();
        }

        public List<BoneEditEntry> GetAllEntries()
        {
            return _entries.Values.SelectMany(d => d.Values).ToList();
        }

        /// <summary>元値を書き戻して記録を消す</summary>
        public void ResetBone(string slotName, Transform bone)
        {
            var entry = GetEntry(slotName, bone != null ? bone.name : null);
            if (entry == null)
            {
                return;
            }

            bone.localPosition = entry.origPosition;
            bone.localRotation = entry.origRotation;
            bone.localScale = entry.origScale;
            _entries[slotName].Remove(entry.boneName);
        }

        public void ResetSlot(string slotName, GameObject slotObj)
        {
            foreach (var entry in GetEntries(slotName))
            {
                ResetBone(slotName, SlotBoneManager.FindBone(slotObj, entry.boneName));
            }
            // ボーンが見つからず戻せなかった分も含めて記録を捨てる
            _entries.Remove(slotName);
        }

        /// <summary>装着アイテムが変わっていたら記録を破棄する（Transform には触らない）</summary>
        public void DiscardSlotIfItemChanged(string slotName, string currentFileName)
        {
            Dictionary<string, BoneEditEntry> slotDic;
            if (!_entries.TryGetValue(slotName, out slotDic) || slotDic.Count == 0)
            {
                return;
            }

            // 同一スロットの記録は必ず同じアイテムのものなので先頭 1 件で判定できる
            if (slotDic.Values.First().itemFileName != currentFileName)
            {
                _entries.Remove(slotName);
            }
        }

        /// <summary>記録済みの編集値をボーン名で引き当てて再適用する</summary>
        public void ReapplySlot(string slotName, GameObject slotObj)
        {
            if (slotObj == null)
            {
                return;
            }

            foreach (var entry in GetEntries(slotName))
            {
                var bone = SlotBoneManager.FindBone(slotObj, entry.boneName);
                if (bone == null)
                {
                    // 構成違いでボーンが無い場合は記録を残したまま飛ばす
                    continue;
                }
                bone.localPosition = entry.position;
                bone.localRotation = entry.rotation;
                bone.localScale = entry.scale;
            }
        }

        /// <summary>全エントリのディープコピー。履歴スナップショットに使う</summary>
        public List<BoneEditEntry> CaptureEntries()
        {
            var result = new List<BoneEditEntry>();
            foreach (var entry in GetAllEntries())
            {
                result.Add(CopyEntry(entry));
            }
            return result;
        }

        /// <summary>
        /// スナップショットのエントリで丸ごと置き換える (Transform には触らない)。
        /// スロットのリセットを undo したときに記録を復活させるために使う
        /// </summary>
        public void RestoreEntries(List<BoneEditEntry> entries)
        {
            _entries.Clear();
            if (entries == null)
            {
                return;
            }

            foreach (var entry in entries)
            {
                Dictionary<string, BoneEditEntry> slotDic;
                if (!_entries.TryGetValue(entry.slotName, out slotDic))
                {
                    slotDic = new Dictionary<string, BoneEditEntry>();
                    _entries[entry.slotName] = slotDic;
                }
                slotDic[entry.boneName] = CopyEntry(entry);
            }
        }

        private static BoneEditEntry CopyEntry(BoneEditEntry entry)
        {
            return new BoneEditEntry
            {
                slotName = entry.slotName,
                itemFileName = entry.itemFileName,
                boneName = entry.boneName,
                position = entry.position,
                rotation = entry.rotation,
                scale = entry.scale,
                origPosition = entry.origPosition,
                origRotation = entry.origRotation,
                origScale = entry.origScale,
            };
        }

        public void Clear()
        {
            _entries.Clear();
        }
    }
}
