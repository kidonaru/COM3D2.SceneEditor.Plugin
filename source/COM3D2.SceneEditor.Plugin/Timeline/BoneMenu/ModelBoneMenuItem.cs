
namespace COM3D2.MotionTimelineEditor.Plugin
{
    // PartsEdit 連携は未移植 (partsEditHack が常に null) のため、現状は base への委譲のみ有効
    public class ModelBoneMenuItem : BoneMenuItem
    {
        public override bool isSelectedMenu
        {
            get => base.isSelectedMenu;
            set
            {
                base.isSelectedMenu = value;

                if (partsEditHack == null || maidCache == null)
                {
                    return;
                }

                var bone = modelManager.GetBone(name);
                if (bone == null)
                {
                    return;
                }

                if (value)
                {
                    partsEditHack.targetSelectMode = 1;
                    partsEditHack.SetObject(bone.model.transform.gameObject);
                    MTEUtils.ExecuteNextFrame(() =>
                    {
                        partsEditHack.SetBone(bone.transform);
                    });
                }
                else
                {
                    partsEditHack.SetBone(null);
                }
            }
        }

        public ModelBoneMenuItem(string name, string displayName) : base(name, displayName)
        {
        }
    }
}