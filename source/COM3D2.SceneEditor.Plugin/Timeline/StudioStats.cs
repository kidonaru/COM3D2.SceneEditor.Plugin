using UnityEngine;

namespace COM3D2.MotionTimelineEditor.Plugin
{
    using AttachPoint = PhotoTransTargetObject.AttachPoint;

    // MTE の StudioModelStat.cs / StudioLightStat.cs の最小移植。
    // モデル・ライト管理 (StudioModelManager 等) は未移植のため、
    // タイムラインデータの XML 互換とレイヤー interface の維持に必要なメンバーだけを残している

    public enum StudioModelType
    {
        Mod,
        Prefab,
        Asset,
        MyRoom,
    }

    public class StudioModelStat
    {
        public string name { get; set; }
        public string displayName { get; set; }
        public int group { get; set; }
        public AttachPoint attachPoint { get; set; }
        public int attachMaidSlotNo { get; set; } = -1;
        public string pluginName { get; set; }
        public bool visible { get; set; }
        public Transform transform { get; set; }
    }

    public class StudioLightStat
    {
        public string name { get; set; }
        public LightType type { get; set; } = LightType.Directional;
    }
}
