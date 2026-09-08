using System.Collections.Generic;

namespace COM3D2.SceneEditor.Plugin
{
    /// <summary>動画全本のスナップショット (本数含む)。適用は変わった本だけ再読込する</summary>
    public class VideoSnapshot : PresetDtoSnapshot<List<ScenePresetVideo>>
    {
        public static VideoSnapshot Capture()
        {
            var snapshot = new VideoSnapshot();
            snapshot.Init();
            return snapshot;
        }

        protected override List<ScenePresetVideo> CaptureState() => MteEffectsSnapshot.CaptureVideos();
        protected override void ApplyState(List<ScenePresetVideo> state) => MteEffectsSnapshot.ApplyVideos(state, reloadAll: false);
        protected override PresetDtoSnapshot<List<ScenePresetVideo>> CreateEmpty() => new VideoSnapshot();
    }
}
