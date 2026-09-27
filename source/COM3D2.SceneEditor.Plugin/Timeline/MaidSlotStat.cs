using System.Collections.Generic;
using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class MaidSlotStat : IModelStat
    {
        /// <summary>
        /// マテリアル編集の対象スロットと表示名。並びがそのままスロット選択の表示順になるため List で持つ。
        /// 脱衣の対象 (DressUtils.DressSlotJpNameMap) と違い、体・頭・髪など脱がせない部位も含む。
        /// 精液・モザイク・男性用のスロットは対象外。COM3D2.5 のサブスロット (subNo 1 以降) も扱わない
        /// </summary>
        public static readonly List<KeyValuePair<TBody.SlotID, string>> MaterialSlots = new List<KeyValuePair<TBody.SlotID, string>>
        {
            Slot(TBody.SlotID.body, "体"),
            Slot(TBody.SlotID.head, "頭"),
            Slot(TBody.SlotID.eye, "目"),
            Slot(TBody.SlotID.hairF, "髪 前"),
            Slot(TBody.SlotID.hairR, "髪 後"),
            Slot(TBody.SlotID.hairS, "髪 横"),
#if COM3D25
            Slot(TBody.SlotID.hairS_2, "髪 横 2"),
#endif
            Slot(TBody.SlotID.hairT, "髪 エクステ"),
#if COM3D25
            Slot(TBody.SlotID.hairT_2, "髪 エクステ 2"),
#endif
            Slot(TBody.SlotID.hairAho, "アホ毛"),
            Slot(TBody.SlotID.underhair, "アンダーヘア"),

#if COM3D25
            Slot(TBody.SlotID.outerwear, "アウター"),
            Slot(TBody.SlotID.jacket, "ジャケット"),
            Slot(TBody.SlotID.vest, "ベスト"),
            Slot(TBody.SlotID.shirt, "シャツ"),
#endif
            Slot(TBody.SlotID.wear, "トップス"),
            Slot(TBody.SlotID.onepiece, "ワンピース"),
            Slot(TBody.SlotID.mizugi, "水着"),
#if COM3D25
            Slot(TBody.SlotID.mizugi_top, "水着 上"),
            Slot(TBody.SlotID.mizugi_buttom, "水着 下"),
#endif
            Slot(TBody.SlotID.skirt, "ボトムス"),
            Slot(TBody.SlotID.panz, "パンツ"),
#if COM3D25
            Slot(TBody.SlotID.slip, "スリップ"),
#endif
            Slot(TBody.SlotID.bra, "ブラ"),
            Slot(TBody.SlotID.stkg, "靴下"),
            Slot(TBody.SlotID.shoes, "靴"),
            Slot(TBody.SlotID.glove, "手袋"),

            Slot(TBody.SlotID.headset, "ヘッドドレス"),
            Slot(TBody.SlotID.accHat, "帽子"),
#if COM3D25
            Slot(TBody.SlotID.accHat_2, "帽子 2"),
#endif
            Slot(TBody.SlotID.accKamiSubL, "リボン L"),
            Slot(TBody.SlotID.accKamiSubR, "リボン R"),
            Slot(TBody.SlotID.accKami_1_, "前髪 1"),
            Slot(TBody.SlotID.accKami_2_, "前髪 2"),
            Slot(TBody.SlotID.accKami_3_, "前髪 3"),

            Slot(TBody.SlotID.accHead, "アイマスク"),
#if COM3D25
            Slot(TBody.SlotID.accHead_2, "アイマスク 2"),
            Slot(TBody.SlotID.accFace, "顔"),
#endif
            Slot(TBody.SlotID.accHana, "鼻"),
            Slot(TBody.SlotID.accMiMiL, "耳 L"),
            Slot(TBody.SlotID.accMiMiR, "耳 R"),
            Slot(TBody.SlotID.accNipL, "乳首 L"),
            Slot(TBody.SlotID.accNipR, "乳首 R"),
            Slot(TBody.SlotID.accKubi, "ネックレス"),
            Slot(TBody.SlotID.accKubiwa, "チョーカー"),
            Slot(TBody.SlotID.accHa, "歯"),
            Slot(TBody.SlotID.accHeso, "へそ"),
            Slot(TBody.SlotID.accUde, "腕"),
#if COM3D25
            Slot(TBody.SlotID.accUde_2, "腕 2"),
#endif
            Slot(TBody.SlotID.accAshi, "足首"),
#if COM3D25
            Slot(TBody.SlotID.accAshi_2, "足首 2"),
            Slot(TBody.SlotID.accKoshi, "腰"),
#endif
            Slot(TBody.SlotID.accSenaka, "背中"),
            Slot(TBody.SlotID.accShippo, "尻尾"),
            Slot(TBody.SlotID.accAnl, "アナル"),
            Slot(TBody.SlotID.accVag, "膣"),
            Slot(TBody.SlotID.accXXX, "前穴"),
            Slot(TBody.SlotID.kubiwa, "首輪"),
            Slot(TBody.SlotID.megane, "メガネ"),
            Slot(TBody.SlotID.HandItemL, "左手アイテム"),
            Slot(TBody.SlotID.HandItemR, "右手アイテム"),
            Slot(TBody.SlotID.kousoku_upper, "拘束 上"),
            Slot(TBody.SlotID.kousoku_lower, "拘束 下"),
        };

        private static KeyValuePair<TBody.SlotID, string> Slot(TBody.SlotID slotId, string displayName)
        {
            return new KeyValuePair<TBody.SlotID, string>(slotId, displayName);
        }

        public string name { get; private set; }
        public string displayName { get; private set; }

        public TBodySkin bodySkin { get; private set; }
        public GameObject obj => bodySkin.obj;
        public Transform transform => bodySkin.obj_tr;

        public MPN mpn => bodySkin?.m_ParentMPN ?? MPN.null_mpn;
        public MaidProp prop => bodySkin.m_mp;

        public ModelMaterialController modelMaterialController { get; private set; }

        public List<ModelMaterial> materials
        {
            get
            {
                if (modelMaterialController != null)
                {
                    return modelMaterialController.materials;
                }
                return new List<ModelMaterial>();
            }
        }

        public MaidSlotStat()
        {
        }

        public MaidSlotStat(TBodySkin bodySkin, string displayName)
        {
            this.bodySkin = bodySkin;
            this.name = bodySkin.Category;
            this.displayName = displayName;

            CreateControllers();
        }

        private void CreateControllers()
        {
            modelMaterialController = ModelMaterialController.GetOrCreate(this);
        }

        public ModelMaterial GetMaterial(int index)
        {
            if (modelMaterialController != null)
            {
                return modelMaterialController.GetMaterial(index);
            }
            return null;
        }
    }
}
