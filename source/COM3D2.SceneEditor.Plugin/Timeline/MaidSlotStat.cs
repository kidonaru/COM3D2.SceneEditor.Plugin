using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    // MTE からの移植にあたり、マテリアル編集機能 (ModelMaterialController) は
    // スコープ外のため削除している
    public class MaidSlotStat
    {
        public string name { get; private set; }
        public string displayName { get; private set; }

        public TBodySkin bodySkin { get; private set; }
        public GameObject obj => bodySkin.obj;
        public Transform transform => bodySkin.obj_tr;

        public MPN mpn => bodySkin?.m_ParentMPN ?? MPN.null_mpn;
        public MaidProp prop => bodySkin.m_mp;

        public MaidSlotStat()
        {
        }

        public MaidSlotStat(TBodySkin bodySkin, string displayName)
        {
            this.bodySkin = bodySkin;
            this.name = bodySkin.Category;
            this.displayName = displayName;
        }
    }
}
