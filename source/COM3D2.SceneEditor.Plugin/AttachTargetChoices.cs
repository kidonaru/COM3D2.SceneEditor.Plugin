using System.Collections.Generic;
using MTEP = COM3D2.MotionTimelineEditor.Plugin;

namespace COM3D2.SceneEditor.Plugin
{
    using AttachPoint = PhotoTransTargetObject.AttachPoint;

    /// <summary>アタッチ先の選択肢 1 つ。slotNo が ModelAttachTarget.ModelSlotNo ならモデル</summary>
    public class AttachTargetChoice
    {
        public string label;
        public int slotNo;
        public string modelName;
    }

    /// <summary>
    /// モデルのアタッチ先の選択肢 (なし / メイド / モデル)。
    /// Inspector のモデル管理行とキーフレーム Inspector で同じ並びにする
    /// </summary>
    public static class AttachTargetChoices
    {
        private static MTEP.StudioModelManager modelManager => MTEP.StudioModelManager.instance;

        /// <summary>self はアタッチする側。null (一括編集) なら自分・子孫の除外をせず、適用時の循環判定に任せる</summary>
        public static void Fill(List<AttachTargetChoice> items, MTEP.StudioModelStat self)
        {
            items.Clear();
            items.Add(new AttachTargetChoice { label = "未選択", slotNo = -1, modelName = "" });

            foreach (var maidCache in MTEP.MaidManager.instance.maidCaches)
            {
                items.Add(new AttachTargetChoice { label = maidCache.fullName, slotNo = maidCache.slotNo, modelName = "" });
            }

            foreach (var model in modelManager.models)
            {
                // 一括編集 (self なし) でも、プロバイダが対応していないモデルは付けられないので出さない
                var canAttach = self != null
                    ? modelManager.CanAttachToModel(self, model)
                    : model.transform != null && MTEP.ModelHackManager.instance.CanAttachToModel(model);
                if (!canAttach)
                {
                    continue;
                }
                items.Add(new AttachTargetChoice
                {
                    label = "モデル: " + model.displayName,
                    slotNo = MTEP.ModelAttachTarget.ModelSlotNo,
                    modelName = model.name,
                });
            }
        }

        /// <summary>今のアタッチ先に当たる添字。一覧に無ければ 0 (未選択)</summary>
        public static int IndexOf(List<AttachTargetChoice> items, int slotNo, string modelName)
        {
            var isModel = MTEP.ModelAttachTarget.IsModelTarget(slotNo, modelName);
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (isModel)
                {
                    if (item.modelName == modelName)
                    {
                        return i;
                    }
                }
                else if (item.slotNo == slotNo && string.IsNullOrEmpty(item.modelName))
                {
                    return i;
                }
            }
            return 0;
        }

        /// <summary>Inspector のモデル行用。stat へ選択を書く (キーは呼び出し側で自動登録する)</summary>
        public static void ApplyTo(MTEP.StudioModelStat model, AttachTargetChoice choice)
        {
            if (MTEP.ModelAttachTarget.IsModelTarget(choice.slotNo, choice.modelName))
            {
                model.attachMaidSlotNo = choice.slotNo;
                model.attachPoint = AttachPoint.Head;
                model.attachModelName = choice.modelName;
                return;
            }

            model.attachMaidSlotNo = choice.slotNo;
            model.attachModelName = "";
            if (model.attachPoint == AttachPoint.Null)
            {
                model.attachPoint = AttachPoint.Head;
            }
        }
    }
}
