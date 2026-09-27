using System.Collections.Generic;
using SE = COM3D2.SceneEditor.Plugin;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// タイムラインに保存するマテリアルのシェーダー・テクスチャ変更 1 件。
    /// キーフレームではなく、読込時に 1 回だけ適用する定義の値
    /// </summary>
    public class TimelineMaterialShaderData
    {
        /// <summary>メイドならタイムラインのメイド番号、モデルなら -1</summary>
        public int maidSlotNo = -1;

        /// <summary>メイドはスロット名 (MaidSlotStat.name)、モデルはタイムラインのモデル名</summary>
        public string owner = "";

        /// <summary>Unity マテリアル名 (ModelMaterial.displayName)</summary>
        public string material = "";

        /// <summary>所有者内のマテリアル位置。同名マテリアルの同定に使う</summary>
        public int index;

        /// <summary>差し替えたシェーダー名。空ならシェーダーは元のまま</summary>
        public string shader = "";

        /// <summary>テクスチャ差し替え (プロパティ順)</summary>
        public List<SE.MaterialTextureOverride> textures = new List<SE.MaterialTextureOverride>();

        public bool hasChanges => shader.Length > 0 || textures.Count > 0;

        public bool IsSameTarget(TimelineMaterialShaderData other)
        {
            return other != null
                && maidSlotNo == other.maidSlotNo
                && owner == other.owner
                && material == other.material
                && index == other.index;
        }

        public bool ContentEquals(TimelineMaterialShaderData other)
        {
            return IsSameTarget(other) && shader == other.shader
                && SE.MaterialTextureOverride.ListEquals(textures, other.textures);
        }

        public TimelineMaterialShaderData Clone()
        {
            var clone = (TimelineMaterialShaderData)MemberwiseClone();
            // 要素は不変なので一覧だけ複製する
            clone.textures = new List<SE.MaterialTextureOverride>(textures);
            return clone;
        }

        public void FromXml(TimelineMaterialShaderXml xml)
        {
            maidSlotNo = xml.maidSlotNo;
            owner = xml.owner ?? "";
            material = xml.material ?? "";
            index = xml.index;
            shader = xml.shader ?? "";

            // フォルダ外を指す指定と、同じプロパティの 2 件目以降は読まない
            textures = new List<SE.MaterialTextureOverride>();
            if (xml.textures != null)
            {
                foreach (var texture in xml.textures)
                {
                    string file;
                    if (texture == null
                        || !SE.MaterialTextureCatalog.TryNormalize(texture.property, texture.file, out file)
                        || textures.Exists(t => t.property == texture.property))
                    {
                        continue;
                    }
                    textures.Add(new SE.MaterialTextureOverride(texture.property, file));
                }
            }
            SE.MaterialTextureOverride.Sort(textures);
        }

        public TimelineMaterialShaderXml ToXml()
        {
            var xml = new TimelineMaterialShaderXml
            {
                maidSlotNo = maidSlotNo,
                owner = owner,
                material = material,
                index = index,
                // 空なら要素ごと書かない
                shader = shader.Length > 0 ? shader : null,
            };
            foreach (var texture in textures)
            {
                xml.textures.Add(new TimelineMaterialTextureXml { property = texture.property, file = texture.file });
            }
            return xml;
        }
    }
}
