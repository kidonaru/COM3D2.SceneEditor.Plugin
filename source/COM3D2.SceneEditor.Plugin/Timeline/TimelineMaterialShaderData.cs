namespace COM3D2.MotionTimelineEditor.Plugin
{
    /// <summary>
    /// タイムラインに保存するマテリアルのシェーダー変更 1 件。
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

        public string shader = "";

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
            return IsSameTarget(other) && shader == other.shader;
        }

        public TimelineMaterialShaderData Clone()
        {
            return (TimelineMaterialShaderData)MemberwiseClone();
        }

        public void FromXml(TimelineMaterialShaderXml xml)
        {
            maidSlotNo = xml.maidSlotNo;
            owner = xml.owner ?? "";
            material = xml.material ?? "";
            index = xml.index;
            shader = xml.shader ?? "";
        }

        public TimelineMaterialShaderXml ToXml()
        {
            return new TimelineMaterialShaderXml
            {
                maidSlotNo = maidSlotNo,
                owner = owner,
                material = material,
                index = index,
                shader = shader,
            };
        }
    }
}
