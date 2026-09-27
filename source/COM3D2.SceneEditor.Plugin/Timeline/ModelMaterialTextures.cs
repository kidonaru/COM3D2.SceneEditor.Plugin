using System.Collections.Generic;
using UnityEngine;
using SE = COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// マテリアル 1 件のテクスチャ差し替え。差し替え前のテクスチャを控え、
    /// 読み込んだテクスチャはここが所有して、外すときに破棄する
    /// </summary>
    public class ModelMaterialTextures
    {
        private class Slot
        {
            public string property;
            public string file;
            public Texture original;
            /// <summary>読み込んだテクスチャ。ファイルが見つからなければ null (指定だけ残して保存からは消さない)</summary>
            public Texture2D loaded;
        }

        private readonly List<Slot> _slots = new List<Slot>();

        public int count => _slots.Count;

        private Slot Find(string property)
        {
            return _slots.Find(s => s.property == property);
        }

        public string GetFile(string property)
        {
            var slot = Find(property);
            return slot != null ? slot.file : null;
        }

        public bool IsMissing(string property)
        {
            var slot = Find(property);
            return slot != null && slot.loaded == null;
        }

        public void GetOverrides(List<SE.MaterialTextureOverride> result)
        {
            result.Clear();
            foreach (var slot in _slots)
            {
                result.Add(new SE.MaterialTextureOverride(slot.property, slot.file));
            }
            SE.MaterialTextureOverride.Sort(result);
        }

        /// <summary>
        /// 差し替える。同じファイルを読み込み済みなら何もしない (Undo/Redo の再適用で読み直さない)。
        /// 見つからないファイルは指定だけ残し、マテリアルは元のテクスチャに戻す
        /// </summary>
        public void Change(Material material, string property, string file)
        {
            var slot = Find(property);
            if (slot != null && slot.file == file && slot.loaded != null)
            {
                return;
            }
            if (slot == null)
            {
                slot = new Slot
                {
                    property = property,
                    original = material.HasProperty(property) ? material.GetTexture(property) : null,
                };
                _slots.Add(slot);
            }

            var previous = slot.loaded;
            slot.file = file;
            slot.loaded = SE.MaterialTextureFiles.Load(property, file, slot.original);
            if (material.HasProperty(property))
            {
                material.SetTexture(property, slot.loaded != null ? slot.loaded : slot.original);
            }
            // マテリアルが新しいテクスチャを指した後で破棄する
            Destroy(previous);
        }

        public void Reset(Material material, string property)
        {
            var slot = Find(property);
            if (slot == null)
            {
                return;
            }
            _slots.Remove(slot);
            if (material != null && material.HasProperty(property))
            {
                material.SetTexture(property, slot.original);
            }
            Destroy(slot.loaded);
        }

        public void ResetAll(Material material)
        {
            for (var i = _slots.Count - 1; i >= 0; i--)
            {
                Reset(material, _slots[i].property);
            }
        }

        /// <summary>overrides の内容に揃える。載っていないプロパティの差し替えは外す</summary>
        public void SetAll(Material material, List<SE.MaterialTextureOverride> overrides)
        {
            for (var i = _slots.Count - 1; i >= 0; i--)
            {
                var property = _slots[i].property;
                if (!overrides.Exists(o => o.property == property))
                {
                    Reset(material, property);
                }
            }
            foreach (var entry in overrides)
            {
                Change(material, entry.property, entry.file);
            }
        }

        /// <summary>
        /// ゲーム側 (パーツ色の変更で _MainTex を作り直す等) が差し替えたテクスチャを取り込む。
        /// こちらの差し替えは外し、読み込んだテクスチャを破棄する。外したら true
        /// </summary>
        public bool DropExternallyReplaced(Material material)
        {
            var dropped = false;
            for (var i = _slots.Count - 1; i >= 0; i--)
            {
                var slot = _slots[i];
                if (slot.loaded == null || !material.HasProperty(slot.property)
                    || material.GetTexture(slot.property) == slot.loaded)
                {
                    continue;
                }
                _slots.RemoveAt(i);
                Destroy(slot.loaded);
                dropped = true;
            }
            return dropped;
        }

        /// <summary>シェーダー差し替え後に貼り直す。差し替え時にプロパティが無かった場合に備える</summary>
        public void Reapply(Material material)
        {
            foreach (var slot in _slots)
            {
                if (slot.loaded != null && material.HasProperty(slot.property))
                {
                    material.SetTexture(slot.property, slot.loaded);
                }
            }
        }

        /// <summary>Material が破棄・作り直されたとき。Material には触らず、読み込んだテクスチャだけ破棄する</summary>
        public void DestroyAll()
        {
            foreach (var slot in _slots)
            {
                Destroy(slot.loaded);
            }
            _slots.Clear();
        }

        private static void Destroy(Texture2D texture)
        {
            if (texture != null)
            {
                Object.Destroy(texture);
            }
        }
    }
}
