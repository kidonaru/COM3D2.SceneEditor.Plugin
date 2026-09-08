using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>サブカメラ全台のスナップショット (台数含む)</summary>
    public class SubCameraSnapshot : PresetDtoSnapshot<List<ScenePresetSubCamera>>
    {
        public static SubCameraSnapshot Capture() => (SubCameraSnapshot)new SubCameraSnapshot().Init();

        protected override List<ScenePresetSubCamera> CaptureState() => MteEffectsSnapshot.CaptureSubCameras();
        protected override void ApplyState(List<ScenePresetSubCamera> state) => MteEffectsSnapshot.ApplySubCameras(state);
        protected override PresetDtoSnapshot<List<ScenePresetSubCamera>> CreateEmpty() => new SubCameraSnapshot();
    }
}
