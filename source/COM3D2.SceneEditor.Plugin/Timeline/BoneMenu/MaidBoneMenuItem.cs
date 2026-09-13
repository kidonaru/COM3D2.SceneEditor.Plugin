namespace COM3D2.MotionTimelineEditor.Plugin
{
    public class MaidBoneMenuItem : BoneMenuItem
    {
        public readonly IKManager.BoneType boneType;

        public override bool isSelectedMenu
        {
            get => base.isSelectedMenu;
            set
            {
                if (partsEditHack != null)
                {
                    partsEditHack.SetBone(null);
                }
                base.isSelectedMenu = value;
            }
        }

        public MaidBoneMenuItem(string name, string displayName) : base(name, displayName)
        {
            this.boneType = BoneUtils.GetBoneTypeByName(name);
        }
    }
}
